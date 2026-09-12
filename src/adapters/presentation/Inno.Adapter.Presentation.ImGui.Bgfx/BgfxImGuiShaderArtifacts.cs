using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Inno.Rendering;

namespace Inno.Adapter.Presentation.ImGui;

/// <summary>Loads the graph-built, source-free ImGui program distributed with this presentation adapter.</summary>
public static class BgfxImGuiShaderArtifacts
{
    /// <summary>Reads the precompiled program for the current host and requested graphics API without invoking a compiler.</summary>
    /// <param name="api">The active device API.</param>
    /// <returns>The validated texture binding, stages and render state required by ImGui.</returns>
    /// <exception cref="PlatformNotSupportedException">The host architecture has no supported distribution profile.</exception>
    /// <exception cref="InvalidDataException">The installed adapter lacks a required artifact or its binding contract is invalid.</exception>
    public static GraphicsPipelineDescriptor Load(GraphicsApi api)
    {
        string platform = OperatingSystem.IsMacOS() && RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "MacOSArm64"
            : OperatingSystem.IsWindows() && RuntimeInformation.ProcessArchitecture == Architecture.X64 ? "WindowsX64"
            : throw new PlatformNotSupportedException("ImGui artifacts are distributed for macOS arm64 and Windows x64.");
        string resource = $"Inno.BuiltInShaders.ImGui.{platform}.{api}";
        using Stream source = typeof(BgfxImGuiShaderArtifacts).Assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidDataException($"Missing precompiled ImGui shader '{resource}'. Rebuild or repair the adapter installation; runtime source compilation is not supported.");
        using var bytes = new MemoryStream();
        source.CopyTo(bytes);
        RenderShaderArtifact artifact = RenderShaderArtifactCodec.Decode(bytes.ToArray(), "Inno/Host/ImGui", RenderShaderVariant.empty);
        RenderShaderPassArtifact pass = artifact.passes.Single();
        ShaderInterfaceBinding binding = pass.shaderInterface.bindings.Single();
        if (binding.id.value != "s_tex" || binding.bindingKind != ShaderPropertyBindingKind.SampledTexture || binding.location != 0)
            throw new InvalidDataException("The built-in ImGui graph must expose exactly the s_tex sampled texture at slot zero.");
        return new GraphicsPipelineDescriptor(
            pass.stages.Single(static stage => stage.stage == ShaderStage.Vertex).bytes.Span,
            pass.stages.Single(static stage => stage.stage == ShaderStage.Fragment).bytes.Span,
            [new(new("s_tex"), RenderShaderBindingKind.Texture, slot: 0, nativeName: binding.nativeName)],
            BgfxImGuiRenderer.vertexLayout, pass.rasterState);
    }
}
