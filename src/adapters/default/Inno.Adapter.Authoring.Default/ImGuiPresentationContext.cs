using System;
using System.IO;
using System.Linq;
using System.Numerics;

using Inno.Adapter.Platform.Sdl3;
using Inno.Adapter.Presentation;
using Inno.Adapter.Presentation.ImGui;
using Inno.Rendering;
using Inno.Rendering.Assets;

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

        GraphicsPipelineDescriptor pipeline = CompilePipeline(options);
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

    private static GraphicsPipelineDescriptor CompilePipeline(PresentationBackendOptions options)
    {
        Directory.CreateDirectory(options.assetSourceDirectory);
        ShaderCompileTarget target = options.shaderCompiler.CreateTarget(
            options.renderDevice.capabilities,
            optimize: false,
            debugInformation: true);
        ShaderCompilationResult result = options.shaderCompiler.CompileAsync(
                CreateImGuiModule(),
                target,
                RenderShaderVariant.empty,
                options.assetSourceDirectory)
            .AsTask()
            .GetAwaiter()
            .GetResult();
        if (!result.succeeded || result.artifact is null)
        {
            string errors = string.Join(
                Environment.NewLine,
                result.diagnostics.Select(static diagnostic =>
                    $"[{diagnostic.code}] {diagnostic.message}"));
            throw new InvalidOperationException(
                $"The host presentation shader failed to compile:{Environment.NewLine}{errors}");
        }

        CompiledShaderPass pass = result.artifact.passes.Single(
            static candidate => string.Equals(candidate.definition.name, "ImGui", StringComparison.Ordinal));
        ReadOnlyMemory<byte> vertex = pass.stages.Single(
            static stage => stage.stage == ShaderStage.Vertex).bytes;
        ReadOnlyMemory<byte> fragment = pass.stages.Single(
            static stage => stage.stage == ShaderStage.Fragment).bytes;
        return new GraphicsPipelineDescriptor(
            vertex.Span,
            fragment.Span,
            [new RenderShaderBindingDescriptor(
                new RenderBindingId("s_tex"),
                RenderShaderBindingKind.Texture,
                slot: 0)],
            BgfxImGuiRenderer.vertexLayout,
            new RenderRasterState
            {
                cull = RenderCullMode.None,
                depthCompare = RenderDepthCompare.Always,
                depthWrite = false,
                blend = RenderBlendState.alpha,
                multisampling = true
            });
    }

    private static ShaderIRModule CreateImGuiModule()
    {
        var pass = new ShaderPassDefinition(
            "ImGui",
            ShaderProgramKind.Raster,
            renderState: new ShaderRenderState
            {
                cull = ShaderCullMode.None,
                depthCompare = ShaderCompareFunction.Always,
                depthWrite = false,
                blend = RenderBlendState.alpha,
                colorWriteMask = 0x0f
            });
        var definition = new ShaderDefinition(
            "Inno/Host/ImGui",
            [new ShaderPropertyDefinition(
                new ShaderPropertyId("s_tex"),
                "Texture",
                ShaderPropertyType.Texture2D,
                ShaderStage.Fragment,
                default)],
            [],
            [pass]);
        return new ShaderIRModule(
            definition,
            [new ShaderIRPass(
                pass,
                [
                    Stage(ShaderStage.Vertex, BgfxImGuiShaderSource.vertex, "Host/ImGui.vs.sc"),
                    Stage(ShaderStage.Fragment, BgfxImGuiShaderSource.fragment, "Host/ImGui.fs.sc")
                ],
                BgfxImGuiShaderSource.varying)]);
    }

    private static ShaderIRStageModule Stage(ShaderStage stage, string source, string path)
        => new(
            stage,
            "main",
            source,
            new ShaderSourceLocation(path, "ImGui", stage));

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
