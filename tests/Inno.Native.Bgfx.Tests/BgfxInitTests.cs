using Xunit;

namespace Inno.Native.Bgfx.Tests;

public sealed class BgfxInitTests
{
    [Fact]
    public unsafe void InitAndShutdown_ShouldSucceed()
    {
        bgfx.Init init;
        bgfx.init_ctor(&init);
        init.type = bgfx.RendererType.Noop;
        init.resolution.width = 1;
        init.resolution.height = 1;

        var ok = bgfx.init(&init);
        Assert.True(ok);
        bgfx.shutdown();
    }
}
