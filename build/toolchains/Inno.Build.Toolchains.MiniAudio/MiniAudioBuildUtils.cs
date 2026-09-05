using System.IO;

namespace Inno.Build.Toolchains.MiniAudio;

internal static class MiniAudioBuildUtils
{
    /// <summary>
    /// Validates that the pinned miniaudio checkout contains every native build input.
    /// </summary>
    /// <param name="miniAudioDirectory">
    /// The absolute path of the checked-out miniaudio source directory.
    /// </param>
    public static void ValidateSource(string miniAudioDirectory)
    {
        if (!Directory.Exists(miniAudioDirectory))
        {
            throw new DirectoryNotFoundException(
                $"miniaudio source not found at {miniAudioDirectory}. Initialize repository submodules before running this tool.");
        }

        ValidateFile(miniAudioDirectory, MiniAudioBuildConstants.CMAKE_LISTS_FILE);
        ValidateFile(miniAudioDirectory, MiniAudioBuildConstants.HEADER_FILE);
        ValidateFile(miniAudioDirectory, MiniAudioBuildConstants.SOURCE_FILE);
    }

    private static void ValidateFile(string miniAudioDirectory, string fileName)
    {
        string path = Path.Combine(miniAudioDirectory, fileName);
        if (!File.Exists(path))
            throw new FileNotFoundException($"miniaudio source is missing '{fileName}'.", path);
    }
}
