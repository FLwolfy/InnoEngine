using System;
using System.Collections.Generic;
using System.Numerics;

using Inno.Extensibility.Modules;
using Inno.Editor.Core;
using Inno.Editor.Rendering;
using Inno.Platform.Sdl3.ImGui;
using Inno.Rendering;
using Inno.Rendering.Bgfx.ImGui;
using Inno.Rendering.Runtime;

namespace Inno.Editor.Application;

internal sealed class EditorRenderingHostService :
    IEditorRenderingHost,
    IEditorReloadParticipant,
    IDisposable
{
    private readonly BgfxImGuiRenderer m_renderer;
    private readonly RenderRuntimeLayer m_runtime;
    private readonly PlatformImGuiContext m_imgui;
    private readonly IDisposable m_reloadRegistration;
    private readonly Dictionary<string, ViewportState> m_viewports = new(StringComparer.Ordinal);
    private bool m_disposed;

    internal EditorRenderingHostService(
        RenderRuntimeLayer runtime,
        BgfxImGuiRenderer renderer,
        PlatformImGuiContext imgui,
        EditorReloadCoordinator reloads)
    {
        m_runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        m_renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
        m_imgui = imgui ?? throw new ArgumentNullException(nameof(imgui));
        ArgumentNullException.ThrowIfNull(reloads);
        m_reloadRegistration = reloads.Register(this);
    }

    /// <summary>
    /// Submits validated work to the active backend for ordered processing.
    /// </summary>
    /// <param name="request">
    /// The validated immutable request that defines this operation.
    /// </param>
    /// <returns>
    /// The validated editor viewport output that represents the completed operation.
    /// </returns>
    public EditorViewportOutput Submit(EditorViewportRequest request)
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
        ArgumentNullException.ThrowIfNull(request);
        RenderTextureDescriptor descriptor = CreateDescriptor(request);
        if (!m_viewports.TryGetValue(request.viewportId, out ViewportState? state))
        {
            state = new ViewportState(new RenderTexture($"Editor/{request.viewportId}", descriptor));
            m_viewports.Add(request.viewportId, state);
        }
        else if (!state.target.descriptor.Equals(descriptor))
        {
            state.target.Resize(descriptor);
            Unregister(state);
        }

        m_runtime.Submit(new RenderRequest(
            $"Editor:{request.viewportId}",
            RenderTarget.FromTexture(state.target),
            new RenderViewport(0, 0, request.pixelWidth, request.pixelHeight),
            request.pipeline,
            request.data,
            request.priority));

        if (m_runtime.targets.TryGetTexture(state.target, out PersistentTextureHandle resident)
            && resident != state.residentTexture)
        {
            Unregister(state);
            state.presentationTexture = m_renderer.RegisterTexture(resident);
            state.residentTexture = resident;
        }

        return new EditorViewportOutput(
            request.viewportId,
            state.presentationTexture,
            request.pixelWidth,
            request.pixelHeight);
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
        m_imgui.DrawImage(output.texture, logicalSize);
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
        m_disposed = true;
    }

    IEditorReloadTransaction IEditorReloadParticipant.Capture(AssemblyReloadContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return new RenderingReloadTransaction(m_runtime.BeginExtensionReload());
    }

    void IEditorReloadParticipant.RefreshDiagnostics()
    {
    }

    private static RenderTextureDescriptor CreateDescriptor(EditorViewportRequest request)
        => new(
            request.pixelWidth,
            request.pixelHeight,
            request.targetFormat,
            RenderTextureUsage.ColorAttachment | RenderTextureUsage.Sampled);

    private void Unregister(ViewportState state)
    {
        if (state.presentationTexture.isValid)
            _ = m_renderer.UnregisterTexture(state.presentationTexture);
        state.presentationTexture = default;
        state.residentTexture = default;
    }

    private sealed class ViewportState(RenderTexture target)
    {
        internal RenderTexture target { get; } = target;
        internal PersistentTextureHandle residentTexture { get; set; }
        internal ImGuiTextureHandle presentationTexture { get; set; }
    }

    private sealed class RenderingReloadTransaction(
        IRenderRuntimeReloadTransaction session) : IEditorReloadTransaction
    {
        /// <summary>
        /// Builds and validates candidate state without changing the active generation.
        /// </summary>
        public void PrepareForActivation()
        {
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
}
