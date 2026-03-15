using System;

namespace Inno.Build.Bgfx.Platforms;

public static class BgfxBuilderFactory
{
    private const string UNSUPPORTED_PLATFORM_MESSAGE = "Only macos-arm64 and windows-x64 are supported.";

    public static BgfxBuilder CreateForCurrentPlatform()
    {
        var builders = new BgfxBuilder[]
        {
            new OsxArm64BgfxBuilder(),
            new WindowsX64BgfxBuilder()
        };

        foreach (var builder in builders)
        {
            if (builder.IsSupported())
            {
                return builder;
            }
        }

        throw new PlatformNotSupportedException(UNSUPPORTED_PLATFORM_MESSAGE);
    }
}
