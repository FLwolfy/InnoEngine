using System;

namespace Inno.Build.Toolchains.Sdl3.Platforms;

internal static class Sdl3BuilderFactory
{
    private const string UNSUPPORTED_PLATFORM_MESSAGE = "Only macos-arm64 and windows-x64 are supported.";

    /// <summary>
    /// Creates and validates a caller-owned for current platform value.
    /// </summary>
    /// <returns>
    /// The validated sdl3builder that represents the completed operation.
    /// </returns>
    public static Sdl3Builder CreateForCurrentPlatform()
    {
        var builders = new Sdl3Builder[]
        {
            new OsxArm64Sdl3Builder(),
            new WindowsX64Sdl3Builder()
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
