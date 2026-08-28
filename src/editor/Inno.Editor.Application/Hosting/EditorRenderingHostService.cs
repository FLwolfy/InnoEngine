using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Inno.Assets;
using Inno.Assets.Core;
using Inno.Assets.File;
using Inno.Editor.Rendering;
using Inno.Platform.ImGui;
using Inno.Rendering;
using Inno.Rendering.Core;
using Inno.Rendering.ImGui;
using Inno.Rendering.Pipelines;

namespace Inno.Editor.Application;

internal sealed class EditorRenderingHostService : IEditorRenderingHost
{
    private readonly RenderingLayer m_renderingLayer;
    private readonly IRenderPipelineExecutor m_executor;
    private readonly BgfxImGuiRenderer m_renderer;
    private readonly PlatformImGuiContext m_imgui;
    private readonly Dictionary<string, ViewportState> m_viewports = new(StringComparer.Ordinal);
    private bool m_disposed;
    private string? m_activePipelineAssetPath;

    internal EditorRenderingHostService(
        RenderingLayer renderingLayer,
        IRenderPipelineExecutor executor,
        BgfxImGuiRenderer renderer,
        PlatformImGuiContext imgui)
    {
        m_renderingLayer = renderingLayer ?? throw new ArgumentNullException(nameof(renderingLayer));
        m_executor = executor ?? throw new ArgumentNullException(nameof(executor));
        m_renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
        m_imgui = imgui ?? throw new ArgumentNullException(nameof(imgui));
        AssetManager.AssetReloaded += OnAssetReloaded;
    }

    /// <inheritdoc />
    public string? activePipelineAssetPath => m_activePipelineAssetPath;

    /// <inheritdoc />
    public IReadOnlyList<EditorPipelineAssetInfo> GetPipelineAssets()
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
        var result = new List<EditorPipelineAssetInfo>();
        foreach (AssetFileEntry entry in AssetManager.GetFileSystemEntries(includeDirectories: false)
                     .Where(static value => value.extension == ".irenderpipeline")
                     .OrderBy(static value => value.relativePath, StringComparer.Ordinal))
        {
            try
            {
                if (AssetManager.TryLoad(entry.relativePath, out RenderPipelineAsset? asset)
                    && asset is not null)
                {
                    result.Add(new EditorPipelineAssetInfo(
                        entry.relativePath,
                        Path.GetFileNameWithoutExtension(entry.relativePath),
                        asset.defaultRenderPath));
                }
            }
            catch
            {
                // Invalid candidates remain visible through asset diagnostics and do not enter the picker.
            }
        }

        return result;
    }

    /// <inheritdoc />
    public bool TryActivatePipelineAsset(string assetPath)
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(assetPath);
        try
        {
            if (!AssetManager.TryLoad(assetPath, out RenderPipelineAsset? asset) || asset is null)
            {
                return false;
            }

            if (!m_renderingLayer.TryActivatePipelineAsset(asset))
            {
                return false;
            }

            m_activePipelineAssetPath = assetPath;
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <inheritdoc />
    public EditorViewportOutput Submit(EditorViewportRequest request)
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
        ArgumentNullException.ThrowIfNull(request);
        if (!m_viewports.TryGetValue(request.viewportId, out ViewportState? state))
        {
            state = new ViewportState(new RenderTexture(
                $"Editor/{request.viewportId}",
                CreateDescriptor(request.view.pixelWidth, request.view.pixelHeight)));
            m_viewports.Add(request.viewportId, state);
        }
        else if (state.target.descriptor.width != request.view.pixelWidth
                 || state.target.descriptor.height != request.view.pixelHeight)
        {
            state.target.Resize(CreateDescriptor(request.view.pixelWidth, request.view.pixelHeight));
            Unregister(state);
        }

        m_renderingLayer.Submit(new RenderRequest(
            $"Editor:{request.viewportId}",
            request.view,
            RenderTarget.FromTexture(state.target),
            request.renderPath,
            request.clearMode,
            request.backgroundColor,
            request.priority,
            request.enablePicking,
            request.selectedObjectId));

        if (m_executor.TryGetTargetTexture(state.target, out PersistentTextureHandle resident)
            && resident != state.residentTexture)
        {
            Unregister(state);
            state.presentationTexture = m_renderer.RegisterTexture(resident);
            state.residentTexture = resident;
        }

        return new EditorViewportOutput(
            request.viewportId,
            state.presentationTexture,
            request.view.pixelWidth,
            request.view.pixelHeight);
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
        {
            return;
        }

        Unregister(state);
        m_executor.ReleaseTarget(state.target);
    }

    /// <inheritdoc />
    public void ReleaseAll()
    {
        if (m_disposed)
        {
            return;
        }

        foreach (ViewportState state in m_viewports.Values)
        {
            Unregister(state);
            m_executor.ReleaseTarget(state.target);
        }

        m_viewports.Clear();
    }

    internal void Dispose()
    {
        if (m_disposed)
        {
            return;
        }

        ReleaseAll();
        AssetManager.AssetReloaded -= OnAssetReloaded;
        m_disposed = true;
    }

    private void OnAssetReloaded(AssetObject asset)
    {
        if (asset is RenderPipelineAsset pipeline
            && string.Equals(pipeline.sourcePath, m_activePipelineAssetPath, StringComparison.Ordinal))
        {
            _ = m_renderingLayer.TryActivatePipelineAsset(pipeline);
        }
    }

    private void Unregister(ViewportState state)
    {
        if (state.presentationTexture.isValid)
        {
            _ = m_renderer.UnregisterTexture(state.presentationTexture);
            state.presentationTexture = default;
            state.residentTexture = default;
        }
    }

    private static RenderTextureDescriptor CreateDescriptor(int width, int height)
        => new(
            width,
            height,
            RenderTextureFormat.RGBA8,
            RenderTextureUsage.ColorAttachment | RenderTextureUsage.Sampled);

    private sealed class ViewportState(RenderTexture target)
    {
        internal RenderTexture target { get; } = target;
        internal PersistentTextureHandle residentTexture { get; set; }
        internal ImGuiTextureHandle presentationTexture { get; set; }
    }
}
