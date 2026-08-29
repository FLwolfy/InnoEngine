using System;
using System.IO;
using System.Linq;

using Inno.Rendering;
using Inno.Rendering.Assets;
using Inno.Rendering.Core;
using Inno.Rendering.ImGui;

namespace Inno.Editor.Application;

internal static class EditorShaderBootstrap
{
    internal static GraphicsPipelineDescriptor Compile(
        ShaderCompiler shaderCompiler,
        GraphicsCapabilities capabilities,
        string assetsDirectory)
    {
        ArgumentNullException.ThrowIfNull(shaderCompiler);
        ArgumentNullException.ThrowIfNull(capabilities);
        ArgumentException.ThrowIfNullOrWhiteSpace(assetsDirectory);
        Directory.CreateDirectory(assetsDirectory);
        ShaderCompileTarget target = shaderCompiler.CreateTarget(
            capabilities,
            optimize: false,
            debugInformation: true);
        ShaderCompilationResult result = shaderCompiler.CompileAsync(
                CreateImGuiModule(),
                target,
                ShaderVariantKey.empty,
                assetsDirectory)
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
                $"The Editor ImGui shader failed to compile:{Environment.NewLine}{errors}");
        }

        CompiledShaderPass pass = result.artifact.passes.Single(
            static candidate => string.Equals(
                candidate.definition.name,
                "ImGui",
                StringComparison.Ordinal));
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
            "Inno/Editor/ImGui",
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
                    Stage(ShaderStage.Vertex, BgfxImGuiShaderSource.vertex, "Editor/ImGui.vs.sc"),
                    Stage(ShaderStage.Fragment, BgfxImGuiShaderSource.fragment, "Editor/ImGui.fs.sc")
                ],
                BgfxImGuiShaderSource.varying)]);
    }

    private static ShaderIRStageModule Stage(ShaderStage stage, string source, string path)
        => new(
            stage,
            "main",
            source,
            ShaderIRSourceKind.Handwritten,
            new ShaderSourceLocation(path, "ImGui", stage));
}
