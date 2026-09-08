using System;
using System.IO;

namespace Inno.Build.Toolchains.ImGui;

internal static class CimguiBuildUtils
{
    /// <summary>
    /// Validates the supplied input and rejects state that cannot satisfy this contract.
    /// </summary>
    /// <param name="cimguiDir">
    /// The cimgui dir text validated by the validate source operation.
    /// </param>
    public static void ValidateSource(string cimguiDir)
    {
        if (!Directory.Exists(cimguiDir))
        {
            throw new DirectoryNotFoundException(
                $"cimgui source not found at {cimguiDir}. Place the cimgui repository in extern/cimgui before running this tool.");
        }

        var cmakeList = Path.Combine(cimguiDir, CimguiBuildConstants.CMAKE_LISTS_FILE);
        if (!File.Exists(cmakeList))
        {
            throw new DirectoryNotFoundException(
                $"cimgui source missing {CimguiBuildConstants.CMAKE_LISTS_FILE}: {cmakeList}");
        }
    }
}
