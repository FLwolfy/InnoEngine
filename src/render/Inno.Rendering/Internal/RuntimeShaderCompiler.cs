using Inno.Native.Bgfx.Tools;

namespace Inno.Rendering;

internal static class RuntimeShaderCompiler
{
    private static readonly Lock s_lock = new();
    private static readonly HashSet<string> s_compiled = new(StringComparer.Ordinal);

    public static void EnsureCompiled(string shaderRoot, string shaderProfile, string shaderName)
    {
        var key = $"{shaderProfile}:{shaderName}";
        lock (s_lock)
        {
            if (s_compiled.Contains(key))
            {
                return;
            }

            var outputDir = Path.Combine(shaderRoot, shaderProfile);
            Directory.CreateDirectory(outputDir);

            CompileStage(shaderRoot, shaderProfile, shaderName, "vs", "vertex");
            CompileStage(shaderRoot, shaderProfile, shaderName, "fs", "fragment");
            s_compiled.Add(key);
        }
    }

    private static void CompileStage(string shaderRoot, string shaderProfile, string shaderName, string stagePrefix, string shaderType)
    {
        var sourceRoot = Path.Combine(shaderRoot, "Shaders");
        var source = Path.Combine(sourceRoot, $"{stagePrefix}_{shaderName}.sc");
        var output = Path.Combine(shaderRoot, shaderProfile, $"{stagePrefix}_{shaderName}.bin");
        var varying = Path.Combine(sourceRoot, "varying.def.sc");
        var bgfxInclude = Path.Combine(FindRepoRootOrThrow(AppContext.BaseDirectory), "extern", "bgfx", "src");

        if (!File.Exists(source))
        {
            throw new FileNotFoundException($"Shader source not found: {source}");
        }

        if (!File.Exists(varying))
        {
            throw new FileNotFoundException($"Varying file not found: {varying}");
        }

        var (platform, profile) = ResolvePlatformAndProfile(shaderProfile);
        var args =
            $"--type {shaderType} " +
            $"--platform {platform} " +
            $"-p {profile} " +
            $"-f \"{source}\" " +
            $"-o \"{output}\" " +
            $"--varyingdef \"{varying}\" " +
            $"-i \"{sourceRoot}\" " +
            $"-i \"{bgfxInclude}\"";

        var exitCode = ToolRunner.Run(BgfxTool.Shaderc, args, sourceRoot);
        if (exitCode != 0)
        {
            throw new InvalidOperationException($"shaderc failed ({exitCode}) for {source}.");
        }
    }

    private static (string platform, string profile) ResolvePlatformAndProfile(string shaderProfile)
    {
        return shaderProfile switch
        {
            "metal" => ("osx", "metal"),
            "dxbc" => ("windows", "s_5_0"),
            "dxil" => ("windows", "s_6_0"),
            "spirv" => ("linux", "spirv"),
            "essl" => ("android", "300_es"),
            _ => ("linux", "120")
        };
    }

    private static string FindRepoRootOrThrow(string startDir)
    {
        var searchStarts = new[]
        {
            startDir,
            AppContext.BaseDirectory,
            Directory.GetCurrentDirectory()
        };

        foreach (var start in searchStarts)
        {
            var repoRoot = FindRepoRoot(start);
            if (repoRoot is not null)
            {
                return repoRoot;
            }
        }

        throw new DirectoryNotFoundException("Repo root not found. Runtime shader compilation requires InnoEngine.sln in ancestor directories.");
    }

    private static string? FindRepoRoot(string startDir)
    {
        var dir = new DirectoryInfo(startDir);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "InnoEngine.sln")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        return null;
    }
}
