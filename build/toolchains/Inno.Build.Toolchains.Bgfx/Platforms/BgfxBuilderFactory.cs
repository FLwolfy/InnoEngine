using System;

namespace Inno.Build.Toolchains.Bgfx.Platforms;

internal static class BgfxBuilderFactory
{
    private const string UNSUPPORTED_PLATFORM_MESSAGE = "Only macos-arm64 and windows-x64 are supported.";

    /// <summary>
    /// Creates and validates a caller-owned for current platform value.
    /// </summary>
    /// <returns>
    /// The validated bgfx builder that represents the completed operation.
    /// </returns>
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
