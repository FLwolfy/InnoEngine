using System;
using System.IO;
using Inno.Build.CImguizmo;
using Inno.Build.Global;

namespace Inno.Build.CImguizmo.Clean;

static class Program
{
    public static int Main(string[] args)
    {
        try
        {
            var repoRoot = GlobalBuildUtils.FindRepoRoot();
            var libDir = Path.Combine(repoRoot, GlobalBuildConstants.OUTPUT_ROOT_DIR_NAME);
            var cimguizmoLibDir = Path.Combine(libDir, CImguizmoBuildConstants.OUTPUT_PRODUCT_DIR_NAME);

            var externDir = Path.Combine(repoRoot, GlobalBuildConstants.EXTERN_DIR_NAME);
            var cimguizmoDir = Path.Combine(externDir, CImguizmoBuildConstants.CIMGUIZMO_DIR_NAME);

            DeleteIfExists(cimguizmoLibDir);
            DeleteIfExists(Path.Combine(cimguizmoDir, CImguizmoBuildConstants.BUILD_DIR_NAME));

            Console.WriteLine("cimguizmo outputs cleaned.");
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
