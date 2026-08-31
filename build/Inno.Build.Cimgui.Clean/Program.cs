using System;
using System.IO;
using Inno.Build.Cimgui;
using Inno.Build.Global;

namespace Inno.Build.Cimgui.Clean;

static class Program
{
    public static int Main(string[] args)
    {
        try
        {
            var repoRoot = GlobalBuildUtils.FindRepoRoot();
            var libDir = Path.Combine(repoRoot, GlobalBuildConstants.OUTPUT_ROOT_DIR_NAME);
            var cimguiLibDir = Path.Combine(libDir, CimguiBuildConstants.OUTPUT_PRODUCT_DIR_NAME);

            var externDir = Path.Combine(repoRoot, GlobalBuildConstants.EXTERN_DIR_NAME);
            var cimguiDir = Path.Combine(externDir, CimguiBuildConstants.CIMGUI_DIR_NAME);

            DeleteIfExists(cimguiLibDir);
            DeleteIfExists(Path.Combine(cimguiDir, CimguiBuildConstants.BUILD_DIR_NAME));

            Console.WriteLine("cimgui outputs cleaned.");
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
