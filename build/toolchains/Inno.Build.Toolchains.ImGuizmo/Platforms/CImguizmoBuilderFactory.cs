using System;

namespace Inno.Build.Toolchains.ImGuizmo.Platforms;

internal static class CImguizmoBuilderFactory
{
    private const string UNSUPPORTED_PLATFORM_MESSAGE = "Only macos-arm64 and windows-x64 are supported.";

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
