using System;
using System.IO;
using Inno.Build.Bgfx;
using Inno.Build.Global;

namespace Inno.Build.Bgfx.Clean;

static class Program
{
    public static int Main(string[] args)
    {
        try
        {
            var repoRoot = GlobalBuildUtils.FindRepoRoot();
            var libDir = Path.Combine(repoRoot, GlobalBuildConstants.OUTPUT_ROOT_DIR_NAME);
            var bgfxLibDir = Path.Combine(libDir, BgfxBuildConstants.OUTPUT_PRODUCT_DIR_NAME);

            var externDir = Path.Combine(repoRoot, GlobalBuildConstants.EXTERN_DIR_NAME);
            var bgfxDir = Path.Combine(externDir, BgfxBuildConstants.BGFX_DIR_NAME);
            var bxDir = Path.Combine(externDir, BgfxBuildConstants.BX_DIR_NAME);
            var bimgDir = Path.Combine(externDir, BgfxBuildConstants.BIMG_DIR_NAME);

            DeleteIfExists(bgfxLibDir);
            DeleteIfExists(Path.Combine(bgfxDir, BgfxBuildConstants.BUILD_DIR_NAME));
            DeleteIfExists(Path.Combine(bxDir, BgfxBuildConstants.BUILD_DIR_NAME));
            DeleteIfExists(Path.Combine(bimgDir, BgfxBuildConstants.BUILD_DIR_NAME));

            Console.WriteLine("bgfx outputs cleaned.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static void DeleteIfExists(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        Directory.Delete(path, recursive: true);
    }
}
