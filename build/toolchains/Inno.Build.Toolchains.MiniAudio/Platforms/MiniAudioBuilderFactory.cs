using System;

namespace Inno.Build.Toolchains.MiniAudio.Platforms;

internal static class MiniAudioBuilderFactory
{
    private const string UNSUPPORTED_PLATFORM_MESSAGE = "Supported hosts are macos-arm64, windows-x64, linux-x64, and linux-arm64.";

    /// <summary>
    /// Creates the miniaudio builder matching the current host platform.
    /// </summary>
    /// <returns>
    /// The validated platform builder that can execute on the current host.
    /// </returns>
    /// <exception cref="PlatformNotSupportedException">
    /// Thrown when the current operating system or process architecture is unsupported.
    /// </exception>
    public static MiniAudioBuilder CreateForCurrentPlatform()
    {
        MiniAudioBuilder[] builders =
        [
            new OsxArm64MiniAudioBuilder(),
            new WindowsX64MiniAudioBuilder(),
            new LinuxMiniAudioBuilder()
        ];

        foreach (MiniAudioBuilder builder in builders)
        {
            if (builder.IsSupported())
                return builder;
        }

        throw new PlatformNotSupportedException(UNSUPPORTED_PLATFORM_MESSAGE);
    }
}
