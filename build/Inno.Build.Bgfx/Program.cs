using System.Diagnostics;
using System.Runtime.InteropServices;

static class Program
{

    public static int Main(string[] args)
    {
        try
        {
            var options = Options.Parse(args);
            var repoRoot = FindRepoRoot();
            var externDir = Path.Combine(repoRoot, "extern");
            var bgfxDir = Path.Combine(externDir, "bgfx");
            var bxDir = Path.Combine(externDir, "bx");
            var bimgDir = Path.Combine(externDir, "bimg");
            var outputDir = Path.Combine(repoRoot, "native", "bgfx", options.OutputPlatform);

            Directory.CreateDirectory(externDir);
            Directory.CreateDirectory(outputDir);

            ValidateSubmodules(bgfxDir, bxDir, bimgDir);

            Run("make", options.MakeTarget, bgfxDir);

            CopyArtifacts(bgfxDir, outputDir, options.IncludeStatic);
            Console.WriteLine($"bgfx build complete. Output: {outputDir}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "InnoEngine.sln")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("Could not locate repo root (InnoEngine.sln not found).");
    }

    private static void CopyArtifacts(string bgfxDir, string outputDir, bool includeStatic)
    {
        var buildDir = Path.Combine(bgfxDir, ".build");
        if (!Directory.Exists(buildDir))
        {
            Console.WriteLine("No .build directory found. Nothing to copy.");
            return;
        }

        var exts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".dll",
            ".dylib",
            ".so",
        };

        if (includeStatic)
        {
            exts.Add(".a");
            exts.Add(".lib");
        }

        var candidates = Directory.EnumerateFiles(buildDir, "*", SearchOption.AllDirectories)
            .Where(path =>
            {
                var ext = Path.GetExtension(path);
                if (!exts.Contains(ext))
                {
                    return false;
                }

                var fileName = Path.GetFileName(path);
                if (!ContainsAny(fileName, "bgfx", "bx", "bimg"))
                {
                    return false;
                }

                var normalized = path.Replace('\\', '/');
                return normalized.Contains("/bin/") || normalized.Contains("/lib/");
            })
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (candidates.Count == 0)
        {
            Console.WriteLine("No matching artifacts found under .build.");
            return;
        }

        Directory.CreateDirectory(outputDir);
        foreach (var src in candidates)
        {
            var dest = Path.Combine(outputDir, Path.GetFileName(src));
            File.Copy(src, dest, overwrite: true);
        }
    }

    private static void ValidateSubmodules(string bgfxDir, string bxDir, string bimgDir)
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


    private static bool ContainsAny(string value, params string[] needles)
    {
        foreach (var needle in needles)
        {
            if (value.Contains(needle, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static void Run(string fileName, string arguments, string workingDir)
    {
        Console.WriteLine($"> {fileName} {arguments}");
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            WorkingDirectory = workingDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        using var process = Process.Start(psi);
        if (process == null)
        {
            throw new InvalidOperationException($"Failed to start process: {fileName}");
        }

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data != null)
            {
                Console.WriteLine(e.Data);
            }
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null)
            {
                Console.Error.WriteLine(e.Data);
            }
        };

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"{fileName} exited with code {process.ExitCode}.");
        }
    }
}

sealed record Options(
    string OutputPlatform,
    string MakeTarget,
    bool IncludeStatic,
    string Config)
{
    public static Options Parse(string[] args)
    {
        var config = "release";
        var makeTargetOverride = "";
        var includeStatic = false;
        var outputPlatformOverride = "";

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            switch (arg)
            {
                case "--repo":
                    _ = GetNext(args, ref i);
                    break;
                case "--platform":
                    outputPlatformOverride = GetNext(args, ref i);
                    break;
                case "--config":
                    config = GetNext(args, ref i).ToLowerInvariant();
                    break;
                case "--make-target":
                    makeTargetOverride = GetNext(args, ref i);
                    break;
                case "--include-static":
                    includeStatic = true;
                    break;
                case "--help":
                case "-h":
                    PrintHelp();
                    Environment.Exit(0);
                    break;
                default:
                    throw new ArgumentException($"Unknown argument: {arg}");
            }
        }

        if (config is not ("debug" or "release"))
        {
            throw new ArgumentException("--config must be 'debug' or 'release'.");
        }

        var defaults = DetectDefaults(config);
        var outputPlatform = string.IsNullOrWhiteSpace(outputPlatformOverride)
            ? defaults.OutputPlatform
            : outputPlatformOverride;
        var makeTarget = string.IsNullOrWhiteSpace(makeTargetOverride)
            ? defaults.MakeTarget
            : makeTargetOverride;

        return new Options(outputPlatform, makeTarget, includeStatic, config);
    }

    private static (string OutputPlatform, string MakeTarget) DetectDefaults(string config)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            if (RuntimeInformation.ProcessArchitecture != Architecture.Arm64)
            {
                throw new PlatformNotSupportedException("Only macos-arm64 is supported. Use --make-target to override.");
            }

            var outputPlatform = "macos-arm64";
            var makeTarget = config == "debug" ? "osx-debug" : "osx-release";
            return (outputPlatform, makeTarget);
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var outputPlatform = RuntimeInformation.ProcessArchitecture == Architecture.Arm64
                ? "windows-arm64"
                : "windows-x64";
            var makeTarget = config == "debug" ? "vs2022-debug64" : "vs2022-release64";
            return (outputPlatform, makeTarget);
        }

        throw new PlatformNotSupportedException("Only macOS and Windows are supported by default. Use --make-target to override.");
    }

    private static string GetNext(string[] args, ref int index)
    {
        if (index + 1 >= args.Length)
        {
            throw new ArgumentException($"Missing value for {args[index]}.");
        }

        index++;
        return args[index];
    }

    private static void PrintHelp()
    {
        Console.WriteLine("Inno.Build.Bgfx");
        Console.WriteLine("Options:");
        Console.WriteLine("  --platform <name>      output directory name under native/bgfx");
        Console.WriteLine("  --config <debug|release>");
        Console.WriteLine("  --make-target <target> override full make target");
        Console.WriteLine("  --include-static       also copy static libs (.a/.lib)");
    }
}
