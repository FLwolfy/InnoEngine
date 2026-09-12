using System;
using System.Numerics;

using Inno.Adapter.Platform.Sdl3;
using Inno.Adapter.Presentation;
using Inno.Adapter.Presentation.ImGui;
using Inno.Rendering;

namespace Inno.Adapter.Authoring.Default;

internal sealed class ImGuiPresentationContext : IPresentationContext
{
    private readonly Sdl3PlatformApplication m_application;
    private readonly Sdl3PlatformWindow m_window;
    private readonly BgfxImGuiRenderer m_renderer;
    private readonly PlatformImGuiContext m_context;
    private bool m_disposed;

    internal ImGuiPresentationContext(PresentationBackendOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        m_application = options.platformApplication as Sdl3PlatformApplication
            ?? throw new NotSupportedException(
                "The built-in ImGui presentation requires the selected platform adapter to support SDL3 integration.");
        m_window = options.window as Sdl3PlatformWindow
            ?? throw new NotSupportedException(
                "The built-in ImGui presentation requires a window created by the selected SDL3 platform adapter.");

        GraphicsPipelineDescriptor pipeline = BgfxImGuiShaderArtifacts.Load(options.renderDevice.capabilities.backend);
        m_renderer = new BgfxImGuiRenderer(options.renderDevice, pipeline);
        try
        {
            m_context = m_application.CreateImGuiContext(
                m_window,
                ToImGuiFlags(options.features),
                m_renderer);
        }
        catch
        {
            m_renderer.Dispose();
            throw;
        }
    }

    void IPresentationContext.SetLayoutFile(string? filePath) => m_context.SetIniFile(filePath);

    void IPresentationContext.LoadLayout(string? settings) => m_context.LoadIniSettings(settings);

    bool IPresentationContext.TryCaptureLayout(out string settings, bool force)
        => m_context.TryCaptureIniSettings(out settings, force);

    void IPresentationContext.RenderFrame(Action drawFrame)
    {
        ArgumentNullException.ThrowIfNull(drawFrame);
        _ = m_context.RenderFrame(drawFrame);
    }

    PresentationTextureHandle IPresentationContext.RegisterTexture(PersistentTextureHandle texture)
        => new(m_renderer.RegisterTexture(texture).value);

    bool IPresentationContext.UnregisterTexture(PresentationTextureHandle texture)
        => texture.isValid && m_renderer.UnregisterTexture(new ImGuiTextureHandle(texture.value));

    void IPresentationContext.DrawImage(PresentationTextureHandle texture, Vector2 size)
    {
        if (!texture.isValid)
            throw new ArgumentException("The presentation texture handle is invalid.", nameof(texture));
        m_context.DrawImage(new ImGuiTextureHandle(texture.value), size);
    }

    void IRenderFrameGraphContributor.PrepareFrame(ulong frameIndex) => m_renderer.PrepareFrame(frameIndex);

    void IRenderFrameGraphContributor.AddRenderPasses(RenderGraphBuilder graph, ulong frameIndex)
        => m_renderer.AddRenderPasses(graph, frameIndex);

    void IDisposable.Dispose()
    {
        if (m_disposed)
            return;
        m_disposed = true;
        m_application.DestroyImGuiContext(m_window);
        m_renderer.Dispose();
    }


    private static ImGuiContextFlags ToImGuiFlags(PresentationFeatures features)
    {
        ImGuiContextFlags result = ImGuiContextFlags.None;
        if ((features & PresentationFeatures.MultipleWindows) != 0)
            result |= ImGuiContextFlags.EnableViewports;
        if ((features & PresentationFeatures.Docking) != 0)
            result |= ImGuiContextFlags.EnableDocking;
        if ((features & PresentationFeatures.SmoothResize) != 0)
            result |= ImGuiContextFlags.EnableSmoothResize;
        return result;
    }
}
