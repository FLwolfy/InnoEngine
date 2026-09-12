using System;
using System.IO;
using Inno.Adapter.Rendering.Bgfx;
using Inno.Adapter.Presentation.ImGui;
using Inno.Rendering;
using Xunit;

namespace Inno.Adapter.Rendering.Bgfx.Tests;

public sealed class BgfxImGuiColorContractTests
{
    [Fact]
    public void DefaultBackbufferAndImGuiShaderApplyExactlyOneSrgbEncoding()
    {
        var options = new BgfxDeviceOptions();
        string source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "ShaderFixtures", "ImGuiFragment.ishadersource"));

        Assert.True(options.sRgbBackbuffer);
        Assert.Contains("SrgbToLinear(tint.rgb)", source);
        Assert.Contains("tint.a", source);
        Assert.DoesNotContain("SrgbToLinear(texture2D", source);
        Assert.DoesNotContain("void main", source);
    }

    [Fact]
    public void DistributedImGuiGraphLoadsCompiledStagesAndOneTextureBinding()
    {
        GraphicsApi api = OperatingSystem.IsMacOS() ? GraphicsApi.Metal : GraphicsApi.Direct3D11;
        GraphicsPipelineDescriptor pipeline = BgfxImGuiShaderArtifacts.Load(api);

        Assert.False(pipeline.vertexShader.IsEmpty);
        Assert.False(pipeline.fragmentShader.IsEmpty);
        RenderShaderBindingDescriptor binding = Assert.Single(pipeline.bindings);
        Assert.Equal("s_tex", binding.id.value);
        Assert.Equal(RenderShaderBindingKind.Texture, binding.kind);
        Assert.Equal(0, binding.slot);
        Assert.InRange(binding.nativeName.Length, 1, 63);
    }
}
