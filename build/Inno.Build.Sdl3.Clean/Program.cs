using System;
using System.IO;
using Inno.Build.Global;
using Inno.Build.Sdl3;

namespace Inno.Build.Sdl3.Clean;

static class Program
{
    public static int Main(string[] args)
    {
        try
        {
            var repoRoot = GlobalBuildUtils.FindRepoRoot();
            var libDir = Path.Combine(repoRoot, GlobalBuildConstants.OUTPUT_ROOT_DIR_NAME);
            var sdlLibDir = Path.Combine(libDir, Sdl3BuildConstants.OUTPUT_PRODUCT_DIR_NAME);

            var externDir = Path.Combine(repoRoot, GlobalBuildConstants.EXTERN_DIR_NAME);
            var sdlDir = Path.Combine(externDir, Sdl3BuildConstants.SDL_DIR_NAME);

            DeleteIfExists(sdlLibDir);
            DeleteIfExists(Path.Combine(sdlDir, Sdl3BuildConstants.BUILD_DIR_NAME));

            Console.WriteLine("SDL3 outputs cleaned.");
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
