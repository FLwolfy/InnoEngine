using Inno.Extensibility.Reload;
using System;
using System.Collections.Generic;
using System.Numerics;

using Inno.Extensibility.Modules;
using Inno.Adapter.Presentation;
using Inno.Editor.Core;
using Inno.Editor.Rendering;
using Inno.Rendering;
using Inno.Rendering.Runtime;

namespace Inno.Editor.Application;

internal sealed class EditorRenderingHostService :
    IEditorRenderingHost,
    IEditorPreviewService,
    IEditorReloadParticipant,
    IDisposable
{
    private readonly RenderRuntime m_runtime;
    private readonly IPresentationContext m_presentation;
    private readonly IDisposable m_reloadRegistration;
    private readonly Dictionary<string, ViewportState> m_viewports = new(StringComparer.Ordinal);
    private readonly Dictionary<RenderTextureArtifactReference, PreviewState> m_previews = [];
    private readonly Dictionary<ulong, PreviewState> m_previewsById = [];
    private ulong m_nextPreviewId;
    private bool m_disposed;

    internal EditorRenderingHostService(
        RenderRuntime runtime,
        IPresentationContext presentation,
        EditorReloadCoordinator reloads)
    {
        m_runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        m_presentation = presentation ?? throw new ArgumentNullException(nameof(presentation));
        ArgumentNullException.ThrowIfNull(reloads);
        m_reloadRegistration = reloads.Register(this);
    }

    /// <summary>Gets the active rendering-device generation.</summary>
    public uint deviceGeneration => m_runtime.deviceGeneration;

    /// <summary>Tries to resolve a standalone texture preview without blocking target compilation.</summary>
    public bool TryGetTexture(TextureAsset texture, out EditorPreviewHandle handle)
    {
        ArgumentNullException.ThrowIfNull(texture);
        return TryGetTextureArtifact(
            texture.GetTextureArtifactReference(),
            texture.width,
            texture.height,
            out handle);
    }

    /// <summary>Tries to resolve a named texture artifact preview without blocking target compilation.</summary>
    public bool TryGetTextureArtifact(
        RenderTextureArtifactReference texture,
        int pixelWidth,
        int pixelHeight,
        out EditorPreviewHandle handle)
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pixelWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pixelHeight);
        if (m_previews.TryGetValue(texture, out PreviewState? existing))
        {
            handle = existing.handle;
            return true;
        }
        m_runtime.resources.PrewarmTextureArtifact(texture);
        if (!m_runtime.resources.TryResolveTextureArtifact(texture, out PersistentTextureHandle resident))
        {
            handle = default;
            return false;
        }
        ulong value = ++m_nextPreviewId;
        if (value == 0)
            value = ++m_nextPreviewId;
        var preview = new PreviewState(
            texture,
            new EditorPreviewHandle(value, deviceGeneration, pixelWidth, pixelHeight),
            resident,
            m_presentation.RegisterTexture(resident));
        m_previews.Add(texture, preview);
        m_previewsById.Add(value, preview);
        handle = preview.handle;
        return true;
    }

    /// <summary>Draws one current-generation preview into the active presentation surface.</summary>
    public void Draw(EditorPreviewHandle handle, Vector2 logicalSize)
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
        if (!handle.isValid || handle.deviceGeneration != deviceGeneration
            || !m_previewsById.TryGetValue(handle.value, out PreviewState? preview)
            || preview.handle != handle)
        {
            throw new InvalidOperationException("The editor preview handle is stale or does not belong to this host.");
        }
        if (logicalSize.X <= 0f || logicalSize.Y <= 0f)
            throw new ArgumentOutOfRangeException(nameof(logicalSize), "Preview size must be positive.");
        m_presentation.DrawImage(preview.presentationTexture, logicalSize);
    }

    /// <summary>Releases one cached preview registration.</summary>
    public bool Release(EditorPreviewHandle handle)
    {
        if (!handle.isValid || handle.deviceGeneration != deviceGeneration
            || !m_previewsById.Remove(handle.value, out PreviewState? preview)
            || preview.handle != handle)
        {
            return false;
        }
        m_previews.Remove(preview.reference);
        _ = m_presentation.UnregisterTexture(preview.presentationTexture);
        return true;
    }

    /// <summary>Releases every cached preview registration.</summary>
    void IEditorPreviewService.ReleaseAll() => ReleaseAllPreviews();

    private void ReleaseAllPreviews()
    {
        foreach (PreviewState preview in m_previews.Values)
            _ = m_presentation.UnregisterTexture(preview.presentationTexture);
        m_previews.Clear();
        m_previewsById.Clear();
    }

    /// <summary>
    /// Submits validated work to the active backend for ordered processing.
    /// </summary>
    /// <param name="composition">
    /// The validated ordered viewport composition submitted for the current frame.
    /// </param>
    /// <returns>
    /// The validated editor viewport output that represents the completed operation.
    /// </returns>
    public EditorViewportOutput Submit(EditorViewportComposition composition)
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
        ArgumentNullException.ThrowIfNull(composition);
        RenderTextureDescriptor descriptor = CreateDescriptor(composition);
        if (!m_viewports.TryGetValue(composition.viewportId, out ViewportState? state))
        {
            state = new ViewportState(new RenderTexture($"Editor/{composition.viewportId}", descriptor));
            m_viewports.Add(composition.viewportId, state);
        }
        else if (!state.target.descriptor.Equals(descriptor))
        {
            state.target.Resize(descriptor);
            Unregister(state);
        }

        RenderTarget target = RenderTarget.FromTexture(state.target);
        var viewport = new RenderViewport(0, 0, composition.pixelWidth, composition.pixelHeight);
        foreach (EditorViewportLayer layer in composition.layers)
        {
            m_runtime.Submit(new RenderRequest(
                $"Editor:{composition.viewportId}:{layer.contributorId}",
                target,
                viewport,
                layer.pipeline,
                layer.data,
                layer.order));
        }

        if (m_runtime.targets.TryGetTexture(state.target, out PersistentTextureHandle resident)
            && resident != state.residentTexture)
        {
            Unregister(state);
            state.presentationTexture = m_presentation.RegisterTexture(resident);
            state.residentTexture = resident;
        }

        return new EditorViewportOutput(
            composition.viewportId,
            state.presentationTexture,
            composition.pixelWidth,
            composition.pixelHeight);
    }

    /// <summary>
    /// Renders the value presentation for the current editor frame.
    /// </summary>
    /// <param name="output">
    /// The import output writer that receives runtime data and dependency declarations.
    /// </param>
    /// <param name="logicalSize">
    /// The logical size consumed by draw; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    public void Draw(EditorViewportOutput output, Vector2 logicalSize)
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
        if (!output.isReady)
        {
            throw new InvalidOperationException(
                $"Editor viewport '{output.viewportId}' has no completed render texture yet.");
        }
        m_presentation.DrawImage(output.texture, logicalSize);
    }

    /// <summary>
    /// Releases the caller-owned value lifetime and its retained resources.
    /// </summary>
    /// <param name="viewportId">
    /// The viewport id text validated by the release operation.
    /// </param>
    public void Release(string viewportId)
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(viewportId);
        if (!m_viewports.Remove(viewportId, out ViewportState? state))
            return;
        Unregister(state);
        m_runtime.targets.Release(state.target);
    }

    /// <summary>
    /// Releases the caller-owned all lifetime and its retained resources.
    /// </summary>
    public void ReleaseAll()
    {
        if (m_disposed)
            return;
        foreach (ViewportState state in m_viewports.Values)
        {
            Unregister(state);
            m_runtime.targets.Release(state.target);
        }
        m_viewports.Clear();
    }

    /// <summary>
    /// Releases the resources owned by this implementation.
    /// </summary>
    public void Dispose()
    {
        if (m_disposed)
            return;
        m_reloadRegistration.Dispose();
        ReleaseAll();
        ReleaseAllPreviews();
        m_disposed = true;
    }

    IGenerationChange IEditorReloadParticipant.Capture(AssemblyReloadContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return new RenderingReloadTransaction(this, m_runtime.BeginExtensionReload());
    }

    void IEditorReloadParticipant.RefreshDiagnostics()
    {
    }

    private static RenderTextureDescriptor CreateDescriptor(EditorViewportComposition composition)
        => new(
            composition.pixelWidth,
            composition.pixelHeight,
            composition.targetFormat,
            RenderTextureUsage.ColorAttachment | RenderTextureUsage.Sampled);

    private void Unregister(ViewportState state)
    {
        if (state.presentationTexture.isValid)
            _ = m_presentation.UnregisterTexture(state.presentationTexture);
        state.presentationTexture = default;
        state.residentTexture = default;
    }

    private sealed class ViewportState(RenderTexture target)
    {
        internal RenderTexture target { get; } = target;
        internal PersistentTextureHandle residentTexture { get; set; }
        internal PresentationTextureHandle presentationTexture { get; set; }
    }

    private sealed class RenderingReloadTransaction(
        EditorRenderingHostService owner,
        IRenderRuntimeReloadTransaction session) : IGenerationChange
    {
        /// <summary>
        /// Builds and validates candidate state without changing the active generation.
        /// </summary>
        public void PrepareForActivation()
        {
            owner.ReleaseAll();
            owner.ReleaseAllPreviews();
        }

        /// <summary>
        /// Applies a validated change atomically at the caller-controlled commit point.
        /// </summary>
        public void Apply()
        {
            session.Prepare();
            session.Activate();
        }

        /// <summary>
        /// Finalizes candidate activation and releases temporary transaction state.
        /// </summary>
        public void Complete() => session.Complete();

        /// <summary>
        /// Restores the state captured before the current transaction began.
        /// </summary>
        public void RollbackStructure() => session.Rollback();

        /// <summary>
        /// Restores the state captured before the current transaction began.
        /// </summary>
        public void RestorePreviousState()
        {
        }
    }

    private sealed record PreviewState(
        RenderTextureArtifactReference reference,
        EditorPreviewHandle handle,
        PersistentTextureHandle residentTexture,
        PresentationTextureHandle presentationTexture);
}
