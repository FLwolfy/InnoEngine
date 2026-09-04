using System;
using System.IO;

namespace Inno.Build.Toolchains.Sdl3;

internal static class Sdl3BuildUtils
{
    /// <summary>
    /// Validates the supplied input and rejects state that cannot satisfy this contract.
    /// </summary>
    /// <param name="sdlDir">
    /// The sdl dir text validated by the validate source operation.
    /// </param>
    public static void ValidateSource(string sdlDir)
    {
        if (!Directory.Exists(sdlDir))
        {
            throw new DirectoryNotFoundException(
                $"SDL3 source not found at {sdlDir}. Please initialize submodules before running this tool.");
        }

        var marker = Path.Combine(sdlDir, Sdl3BuildConstants.SOURCE_DIR_NAME);
        if (!Directory.Exists(marker))
        {
            throw new DirectoryNotFoundException(
                $"SDL3 source missing expected folder: {marker}");
        }
    }
}
