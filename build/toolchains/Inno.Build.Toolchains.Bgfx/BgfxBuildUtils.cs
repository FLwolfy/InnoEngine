using System.IO;

namespace Inno.Build.Toolchains.Bgfx;

internal static class BgfxBuildUtils
{
    /// <summary>
    /// Validates the supplied input and rejects state that cannot satisfy this contract.
    /// </summary>
    /// <param name="bgfxDir">
    /// The bgfx dir text validated by the validate submodules operation.
    /// </param>
    /// <param name="bxDir">
    /// The bx dir text validated by the validate submodules operation.
    /// </param>
    /// <param name="bimgDir">
    /// The bimg dir text validated by the validate submodules operation.
    /// </param>
    public static void ValidateSubmodules(string bgfxDir, string bxDir, string bimgDir)
    {
        if (!Directory.Exists(bgfxDir))
        {
            throw new DirectoryNotFoundException(
                $"bgfx submodule not found at {bgfxDir}. Please initialize submodules before running this tool.");
        }

        if (!Directory.Exists(bxDir) || !Directory.Exists(bimgDir))
        {
            throw new DirectoryNotFoundException(
                $"bx/bimg submodules not found next to bgfx. Expected {bxDir} and {bimgDir}.");
        }
    }
}
