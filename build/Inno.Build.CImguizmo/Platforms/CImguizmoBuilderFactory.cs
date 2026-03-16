using System;

namespace Inno.Build.CImguizmo.Platforms;

public static class CImguizmoBuilderFactory
{
    private const string UNSUPPORTED_PLATFORM_MESSAGE = "Only macos-arm64 and windows-x64 are supported.";

    public static CImguizmoBuilder CreateForCurrentPlatform()
    {
        var builders = new CImguizmoBuilder[]
        {
            new OsxArm64CImguizmoBuilder(),
            new WindowsX64CImguizmoBuilder()
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
