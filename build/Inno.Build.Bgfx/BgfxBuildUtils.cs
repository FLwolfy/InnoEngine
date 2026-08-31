using System.IO;

namespace Inno.Build.Bgfx;

public static class BgfxBuildUtils
{
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
