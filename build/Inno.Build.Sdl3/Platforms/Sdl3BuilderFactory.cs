using System;

namespace Inno.Build.Sdl3.Platforms;

public static class Sdl3BuilderFactory
{
    private const string UNSUPPORTED_PLATFORM_MESSAGE = "Only macos-arm64 and windows-x64 are supported.";

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
