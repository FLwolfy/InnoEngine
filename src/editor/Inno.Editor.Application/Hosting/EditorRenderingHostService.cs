using System;
using System.Collections.Generic;
using System.Numerics;

using Inno.Core.Assemblies;
using Inno.Editor.Core;
using Inno.Editor.Rendering;
using Inno.Platform.ImGui;
using Inno.Rendering;
using Inno.Rendering.Core;
using Inno.Rendering.ImGui;
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
        PlatformImGuiContext imgui)
    {
        m_runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        m_renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
        m_imgui = imgui ?? throw new ArgumentNullException(nameof(imgui));
        m_reloadRegistration = EditorReloadCoordinator.Register(this);
    }

    /// <inheritdoc />
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

    /// <inheritdoc />
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

    /// <inheritdoc />
    public void Release(string viewportId)
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(viewportId);
        if (!m_viewports.Remove(viewportId, out ViewportState? state))
            return;
        Unregister(state);
        m_runtime.targets.Release(state.target);
    }

    /// <inheritdoc />
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

    /// <inheritdoc />
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
        RenderRuntimeLayer.RenderRuntimeReloadSession session) : IEditorReloadTransaction
    {
        public void PrepareForActivation()
        {
        }

        public void Apply()
        {
            session.PrepareCandidate();
            session.Activate();
        }

        public void Complete() => session.Complete();

        public void RollbackStructure() => session.Rollback();

        public void RestorePreviousState()
        {
        }
    }
}
