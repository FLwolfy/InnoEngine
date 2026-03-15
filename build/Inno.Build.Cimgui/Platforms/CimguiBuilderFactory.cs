using System;

namespace Inno.Build.Cimgui.Platforms;

public static class CimguiBuilderFactory
{
    private const string UNSUPPORTED_PLATFORM_MESSAGE = "Only macos-arm64 and windows-x64 are supported.";

    public static CimguiBuilder CreateForCurrentPlatform()
    {
        var builders = new CimguiBuilder[]
        {
            new OsxArm64CimguiBuilder(),
            new WindowsX64CimguiBuilder()
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
