using Inno.Rendering.Bgfx;
using Inno.Rendering.Bgfx.ImGui;
using Xunit;

namespace Inno.Rendering.Bgfx.Tests;

public sealed class BgfxImGuiColorContractTests
{
    [Fact]
    public void DefaultBackbufferAndImGuiShaderApplyExactlyOneSrgbEncoding()
    {
        var options = new BgfxDeviceOptions();

        Assert.True(options.sRgbBackbuffer);
        Assert.Contains("InnoSrgbToLinear(v_color0.rgb)", BgfxImGuiShaderSource.fragment);
        Assert.Contains("v_color0.a", BgfxImGuiShaderSource.fragment);
        Assert.DoesNotContain(
            "InnoSrgbToLinear(texture2D",
            BgfxImGuiShaderSource.fragment);
    }
}
