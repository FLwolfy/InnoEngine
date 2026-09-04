using System;
using System.IO;
using Inno.Build.Toolchains;

namespace Inno.Build.Toolchains.Bgfx;

internal static class Program
{
    /// <summary>
    /// Runs the command-line entry point and returns a process exit code.
    /// </summary>
    /// <param name="args">
    /// The command-line arguments that configure this invocation.
    /// </param>
    /// <returns>
    /// The scalar result calculated from the supplied inputs.
    /// </returns>
    public static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("Usage: Inno.Build.Toolchains.Bgfx <native|tools|clean> [options]");
            return 2;
        }

        string[] commandArguments = args[1..];
        return args[0] switch
        {
            "native" => BgfxNativeBuild.Run(commandArguments),
            "tools" => BgfxToolsBuild.Run(commandArguments),
            "clean" when commandArguments.Length == 0 => Clean(),
            "clean" => InvalidCleanArguments(),
            _ => UnknownCommand(args[0])
        };
    }

    private static int Clean()
    {
        try
        {
            string repositoryRoot = ToolchainEnvironment.FindRepoRoot();
            string externalRoot = Path.Combine(repositoryRoot, ToolchainLayout.C_EXTERNAL_DIRECTORY_NAME);
            ToolchainEnvironment.DeleteDirectory(Path.Combine(
                repositoryRoot,
                ToolchainLayout.C_OUTPUT_DIRECTORY_NAME,
                BgfxBuildConstants.OUTPUT_PRODUCT_DIR_NAME));
            ToolchainEnvironment.DeleteDirectory(Path.Combine(
                externalRoot,
                BgfxBuildConstants.BGFX_DIR_NAME,
                BgfxBuildConstants.BUILD_DIR_NAME));
            ToolchainEnvironment.DeleteDirectory(Path.Combine(
                externalRoot,
                BgfxBuildConstants.BX_DIR_NAME,
                BgfxBuildConstants.BUILD_DIR_NAME));
            ToolchainEnvironment.DeleteDirectory(Path.Combine(
                externalRoot,
                BgfxBuildConstants.BIMG_DIR_NAME,
                BgfxBuildConstants.BUILD_DIR_NAME));
            Console.WriteLine("BGFX outputs cleaned.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }

    private static int InvalidCleanArguments()
    {
        Console.Error.WriteLine("The clean command does not accept additional arguments.");
        return 2;
    }

    private static int UnknownCommand(string command)
    {
        Console.Error.WriteLine($"Unknown BGFX toolchain command: {command}");
        return 2;
    }
}
