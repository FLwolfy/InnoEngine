using System;

namespace Inno.Build.Toolchains.ImGuizmo.Platforms;

internal static class CImguizmoBuilderFactory
{
    private const string UNSUPPORTED_PLATFORM_MESSAGE = "Supported hosts are macos-arm64, windows-x64, linux-x64, and linux-arm64.";

    /// <summary>
    /// Creates and validates a caller-owned for current platform value.
    /// </summary>
    /// <returns>
    /// The validated cimguizmo builder that represents the completed operation.
    /// </returns>
    public static CImguizmoBuilder CreateForCurrentPlatform()
    {
        var builders = new CImguizmoBuilder[]
        {
            new OsxArm64CImguizmoBuilder(),
            new WindowsX64CImguizmoBuilder(),
            new LinuxCImguizmoBuilder()
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
