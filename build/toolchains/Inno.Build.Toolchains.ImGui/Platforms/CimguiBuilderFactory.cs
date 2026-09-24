using System;

namespace Inno.Build.Toolchains.ImGui.Platforms;

internal static class CimguiBuilderFactory
{
    private const string UNSUPPORTED_PLATFORM_MESSAGE = "Supported hosts are macos-arm64, windows-x64, linux-x64, and linux-arm64.";

    /// <summary>
    /// Creates and validates a caller-owned for current platform value.
    /// </summary>
    /// <returns>
    /// The validated cimgui builder that represents the completed operation.
    /// </returns>
    public static CimguiBuilder CreateForCurrentPlatform()
    {
        var builders = new CimguiBuilder[]
        {
            new OsxArm64CimguiBuilder(),
            new WindowsX64CimguiBuilder(),
            new LinuxCimguiBuilder()
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
