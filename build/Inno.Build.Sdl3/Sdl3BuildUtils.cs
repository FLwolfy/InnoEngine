using System;
using System.IO;

namespace Inno.Build.Sdl3;

public static class Sdl3BuildUtils
{
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
