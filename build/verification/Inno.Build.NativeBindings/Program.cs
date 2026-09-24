using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Inno.Build.NativeBindings;

internal static partial class Program
{
    private static readonly NativeBuildStep[] S_NATIVE_BUILD_STEPS =
    [
        new("build/toolchains/Inno.Build.Toolchains.Sdl3/Inno.Build.Toolchains.Sdl3.csproj", "build"),
        new("build/toolchains/Inno.Build.Toolchains.MiniAudio/Inno.Build.Toolchains.MiniAudio.csproj", "build"),
        new("build/toolchains/Inno.Build.Toolchains.ImGui/Inno.Build.Toolchains.ImGui.csproj", "build"),
        new("build/toolchains/Inno.Build.Toolchains.ImGuizmo/Inno.Build.Toolchains.ImGuizmo.csproj", "build"),
        new("build/toolchains/Inno.Build.Toolchains.Bgfx/Inno.Build.Toolchains.Bgfx.csproj", "native"),
        new("build/toolchains/Inno.Build.Toolchains.Bgfx/Inno.Build.Toolchains.Bgfx.csproj", "tools"),
        new("build/toolchains/Inno.Build.Toolchains.Text/Inno.Build.Toolchains.Text.csproj", "build"),
        new("build/toolchains/Inno.Build.Toolchains.UI/Inno.Build.Toolchains.UI.csproj", "build")
    ];

    private static int Main(string[] args)
    {
        if (args.Any(static argument => argument is "help" or "--help" or "-h"))
        {
            Console.WriteLine(AcceptanceOptions.usage);
            return 0;
        }

        try
        {
            AcceptanceOptions options = AcceptanceOptions.Parse(args);
            string bindGenRoot = Path.GetFullPath(options.bindGenRoot);
            string repositoryRoot = FindRepositoryRoot();
            IReadOnlyList<string> bindingProjects = DiscoverBindingProjects(repositoryRoot);
            string uiBridgeConfig = Path.Combine(
                repositoryRoot,
                "native",
                "Inno.Native.UI",
                "Bindings",
                "rmlui.bridge.json");
            string bindGenProject = Path.Combine(bindGenRoot, "src", "BGCS.Tool", "BGCS.Tool.csproj");
            string bindGenRuntimeProject = Path.Combine(
                bindGenRoot, "src", "BGCS.Runtime", "BGCS.Runtime.csproj");
            ValidateInputs(bindingProjects, uiBridgeConfig, bindGenProject, bindGenRuntimeProject);

            string target = DetectTarget();
            string nativeConfiguration = options.configuration.ToLowerInvariant();
            try
            {
                Console.WriteLine($"[inno-bindings] Verify deterministic generation for {target}.");
                VerifyCppBridge(
                    repositoryRoot,
                    options,
                    bindGenProject,
                    uiBridgeConfig);
                foreach (string project in bindingProjects)
                {
                    ProcessRunner.Run(options.dotnet,
                        ["build", project,
                            "--configuration", options.configuration, "-t:CheckBindings",
                            $"-p:BindGenRoot={bindGenRoot}", $"-p:BindGenDotNetHost={options.dotnet}", "--nologo"],
                        repositoryRoot);
                }

                AuditNativeImports(repositoryRoot);

                Console.WriteLine("[inno-bindings] Restore the complete InnoEngine solution.");
                ProcessRunner.Run(
                    options.dotnet,
                    [
                        "restore", Path.Combine(repositoryRoot, "InnoEngine.sln"),
                        $"-p:BGCSRuntimeProject={bindGenRuntimeProject}",
                        "-p:BuildInParallel=false",
                        "/m:1",
                        "/nodeReuse:false",
                        "--nologo"
                    ],
                    repositoryRoot);

                BuildNativeDependencies(repositoryRoot, options, nativeConfiguration);

                Console.WriteLine("[inno-bindings] Build the complete InnoEngine solution.");
                ProcessRunner.Run(
                    options.dotnet,
                    [
                        "build", Path.Combine(repositoryRoot, "InnoEngine.sln"),
                        "--configuration", options.configuration,
                        "--no-restore",
                        $"-p:BGCSRuntimeProject={bindGenRuntimeProject}",
                        "-p:TreatWarningsAsErrors=true",
                        "--nologo"
                    ],
                    repositoryRoot);

                IReadOnlyList<string> testProjects = DiscoverTestProjects(repositoryRoot);
                RunTests(repositoryRoot, options, testProjects);
                string reportPath = WriteReport(
                    repositoryRoot,
                    bindGenRoot,
                    target,
                    bindingProjects,
                    testProjects.Count);
                Console.WriteLine(
                    $"[inno-bindings] Project binding diffs, native builds, solution build, import audit, "
                    + $"and {testProjects.Count} test projects passed.");
                Console.WriteLine($"[inno-bindings] Acceptance report: {reportPath}");
                return 0;
            }
            finally
            {
                DeleteTransientCimguizmoBuild(repositoryRoot);
            }
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"[inno-bindings] {exception.Message}");
            return 1;
        }
    }

    private static void ValidateInputs(
        IReadOnlyList<string> bindingProjects,
        string uiBridgeConfig,
        string bindGenProject,
        string bindGenRuntimeProject)
    {
        foreach (string project in bindingProjects)
        {
            string config = Path.Combine(Path.GetDirectoryName(project)!, "Bindings", "bindgen.json");
            if (!File.Exists(config))
                throw new FileNotFoundException("Native binding configuration was not found.", config);
        }
        if (!File.Exists(uiBridgeConfig))
            throw new FileNotFoundException("RmlUi Cpp2C bridge configuration was not found.", uiBridgeConfig);
        if (!File.Exists(bindGenProject))
            throw new FileNotFoundException("The BindGen-CS CLI project was not found.", bindGenProject);
        if (!File.Exists(bindGenRuntimeProject))
            throw new FileNotFoundException("The BindGen-CS runtime project was not found.", bindGenRuntimeProject);

    }

    private static IReadOnlyList<string> DiscoverBindingProjects(string repositoryRoot)
    {
        string nativeRoot = Path.Combine(repositoryRoot, "native");
        string[] projects = Directory.EnumerateDirectories(nativeRoot, "Inno.Native.*", SearchOption.TopDirectoryOnly)
            .Select(directory => Path.Combine(directory, Path.GetFileName(directory) + ".csproj"))
            .Where(File.Exists)
            .Where(project => XDocument.Load(project).Descendants("BindGenGeneratedBindings")
                .Any(property => string.Equals(property.Value.Trim(), "true", StringComparison.OrdinalIgnoreCase)))
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (projects.Length == 0)
            throw new InvalidOperationException("No Native projects declare BindGenGeneratedBindings=true.");
        return projects;
    }

    private static void VerifyCppBridge(
        string repositoryRoot,
        AcceptanceOptions options,
        string bindGenProject,
        string uiBridgeConfig)
    {
        using JsonDocument bridgeConfig = JsonDocument.Parse(File.ReadAllText(uiBridgeConfig));
        string outputPath = bridgeConfig.RootElement.GetProperty("OutputPath").GetString()
            ?? throw new InvalidOperationException("RmlUi Cpp2C OutputPath must be a directory.");
        string outputRoot = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(uiBridgeConfig)!, outputPath));
        IReadOnlyDictionary<string, string> before = SnapshotDirectory(outputRoot);
        ProcessRunner.Run(
            options.dotnet,
            [
                "run",
                "--project", bindGenProject,
                "--configuration", options.configuration,
                "--",
                "bridge", uiBridgeConfig
            ],
            repositoryRoot);
        IReadOnlyDictionary<string, string> after = SnapshotDirectory(outputRoot);
        if (!before.OrderBy(static pair => pair.Key, StringComparer.Ordinal)
            .SequenceEqual(after.OrderBy(static pair => pair.Key, StringComparer.Ordinal)))
        {
            throw new InvalidOperationException(
                "The checked-in RmlUi Cpp2C bridge is stale. Regenerate it with the current BindGen-CS tool.");
        }
        Console.WriteLine("[inno-bindings] RmlUi Cpp2C bridge is deterministic and current.");
    }

    private static IReadOnlyDictionary<string, string> SnapshotDirectory(string root)
    {
        if (!Directory.Exists(root))
            return new Dictionary<string, string>(StringComparer.Ordinal);
        return Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal)
            .ToDictionary(
                path => Path.GetRelativePath(root, path).Replace('\\', '/'),
                path => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))),
                StringComparer.Ordinal);
    }

    private static void AuditNativeImports(string repositoryRoot)
    {
        const int C_MAX_REPORTED_VIOLATIONS = 50;
        string nativeRoot = Path.Combine(repositoryRoot, "native");
        var violations = new List<string>();
        foreach (string path in Directory.EnumerateFiles(nativeRoot, "*.cs", SearchOption.AllDirectories))
        {
            string relativePath = Path.GetRelativePath(repositoryRoot, path).Replace('\\', '/');
            if (relativePath.Contains("/Generated/", StringComparison.OrdinalIgnoreCase)
                || relativePath.Contains("/generated/", StringComparison.OrdinalIgnoreCase)
                || relativePath.Contains("/.bindgen-cache/", StringComparison.Ordinal)
                || relativePath.Contains("/bin/", StringComparison.OrdinalIgnoreCase)
                || relativePath.Contains("/obj/", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            int lineNumber = 0;
            foreach (string line in File.ReadLines(path))
            {
                lineNumber++;
                if (NativeImportPattern().IsMatch(line))
                    violations.Add($"{relativePath}:{lineNumber}:{line.Trim()}");
            }
        }

        if (violations.Count != 0)
        {
            string[] reported = violations.Take(C_MAX_REPORTED_VIOLATIONS).ToArray();
            string remainder = violations.Count > reported.Length
                ? $"{Environment.NewLine}... and {violations.Count - reported.Length} more violation(s)."
                : string.Empty;
            throw new InvalidOperationException(
                "Hand-authored native imports remain outside generated binding roots:"
                + Environment.NewLine
                + string.Join(Environment.NewLine, reported)
                + remainder);
        }

        Console.WriteLine("[inno-bindings] No hand-authored native imports exist outside generated binding roots.");
    }

    private static void BuildNativeDependencies(
        string repositoryRoot,
        AcceptanceOptions options,
        string nativeConfiguration)
    {
        Console.WriteLine("[inno-bindings] Build every native dependency required by generated bindings.");
        foreach (NativeBuildStep step in S_NATIVE_BUILD_STEPS)
        {
            string project = Path.Combine(
                repositoryRoot,
                step.project.Replace('/', Path.DirectorySeparatorChar));
            ProcessRunner.Run(
                options.dotnet,
                [
                    "run",
                    "--project", project,
                    "--configuration", options.configuration,
                    "--no-restore",
                    "-p:TreatWarningsAsErrors=true",
                    "--",
                    step.command,
                    "--config", nativeConfiguration
                ],
                repositoryRoot);
        }
    }

    private static IReadOnlyList<string> DiscoverTestProjects(string repositoryRoot)
    {
        string nativeTestsRoot = Path.Combine(repositoryRoot, "tests", "native");
        var projects = Directory.EnumerateFiles(nativeTestsRoot, "*.csproj", SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal)
            .ToList();
        projects.Add(Path.Combine(repositoryRoot, "tests", "text", "Inno.Text.Tests", "Inno.Text.Tests.csproj"));
        projects.Add(Path.Combine(repositoryRoot, "tests", "ui", "Inno.UI.Tests", "Inno.UI.Tests.csproj"));
        if (projects.Count == 0)
            throw new InvalidOperationException("No native binding test projects were discovered.");
        if (projects.Any(static project => !File.Exists(project)))
        {
            throw new FileNotFoundException(
                "A required native binding test project is missing.",
                projects.First(static project => !File.Exists(project)));
        }
        return projects;
    }

    private static void RunTests(
        string repositoryRoot,
        AcceptanceOptions options,
        IReadOnlyList<string> testProjects)
    {
        Console.WriteLine("[inno-bindings] Run every native binding, Text, and UI test project.");
        foreach (string project in testProjects)
        {
            ProcessRunner.Run(
                options.dotnet,
                [
                    "test", project,
                    "--configuration", options.configuration,
                    "--no-build",
                    "--no-restore",
                    "--nologo"
                ],
                repositoryRoot);
        }
    }

    private static string WriteReport(
        string repositoryRoot,
        string bindGenRoot,
        string target,
        IReadOnlyList<string> bindingProjects,
        int testProjectCount)
    {
        string innoRevision = ProcessRunner.Capture(
            "git", ["-C", repositoryRoot, "rev-parse", "HEAD"], repositoryRoot).Trim();
        string bindGenRevision = ProcessRunner.Capture(
            "git", ["-C", bindGenRoot, "rev-parse", "HEAD"], bindGenRoot).Trim();
        bool innoDirty = !string.IsNullOrWhiteSpace(ProcessRunner.Capture(
            "git", ["-C", repositoryRoot, "status", "--porcelain", "--untracked-files=all"], repositoryRoot));
        bool bindGenDirty = !string.IsNullOrWhiteSpace(ProcessRunner.Capture(
            "git", ["-C", bindGenRoot, "status", "--porcelain", "--untracked-files=all"], bindGenRoot));
        DateTimeOffset generatedAt = DateTimeOffset.UtcNow;
        string reportDirectory = Path.Combine(
            repositoryRoot, "artifacts", "bindings", "acceptance", target);
        Directory.CreateDirectory(reportDirectory);

        var report = new
        {
            target,
            status = "passed",
            generatedAtUtc = generatedAt,
            source = new
            {
                innoEngine = new { revision = innoRevision, workingTreeDirty = innoDirty },
                bindGenCs = new { revision = bindGenRevision, workingTreeDirty = bindGenDirty }
            },
            bindingProjects = bindingProjects.Select(Path.GetFileNameWithoutExtension).ToArray(),
            nativeBuilds = new[] { "SDL3", "MiniAudio", "cimgui", "cimguizmo", "bgfx", "bgfx-tools", "inno-text", "inno-ui" },
            nativeTestProjects = testProjectCount,
            gates = new[]
            {
                "deterministic-project-binding-diff",
                "deterministic-cpp2c-bridge",
                "no-handwritten-native-imports",
                "native-dependency-builds",
                "full-solution-build",
                "all-native-binding-tests"
            }
        };
        string jsonPath = Path.Combine(reportDirectory, "report.json");
        File.WriteAllText(
            jsonPath,
            JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        string markdown = $"""
            # InnoEngine generated-native-binding acceptance

            - Target: `{target}`
            - Status: **passed**
            - Generated at: `{generatedAt:O}`
            - InnoEngine: `{innoRevision}` (working tree dirty: `{innoDirty.ToString().ToLowerInvariant()}`)
            - BindGen-CS: `{bindGenRevision}` (working tree dirty: `{bindGenDirty.ToString().ToLowerInvariant()}`)
            - Binding projects: {string.Join(", ", bindingProjects.Select(Path.GetFileNameWithoutExtension))}
            - Native builds: SDL3, MiniAudio, cimgui, cimguizmo, bgfx, bgfx tools, inno-text, inno-ui
            - Native test projects: {testProjectCount}

            Deterministic Cpp2C bridge and project binding diffs, handwritten-import audit, all native dependency builds, the complete
            InnoEngine solution build, and every native-binding test project passed.
            """;
        File.WriteAllText(
            Path.Combine(reportDirectory, "report.md"),
            markdown + Environment.NewLine,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return jsonPath;
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "InnoEngine.sln")))
                return directory.FullName;
            directory = directory.Parent;
        }
        throw new InvalidOperationException("Could not locate the InnoEngine repository root.");
    }

    private static string DetectTarget()
    {
        string operatingSystem = OperatingSystem.IsWindows()
            ? "windows"
            : OperatingSystem.IsMacOS()
                ? "macos"
                : OperatingSystem.IsLinux()
                    ? "linux"
                    : throw new PlatformNotSupportedException("The current operating system is not supported.");
        string architecture = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.Arm64 => "arm64",
            _ => throw new PlatformNotSupportedException(
                $"Architecture '{RuntimeInformation.ProcessArchitecture}' is not supported.")
        };
        string abi = operatingSystem switch
        {
            "windows" => "msvc",
            "macos" => "darwin",
            "linux" => "gnu",
            _ => throw new UnreachableException()
        };
        return $"{operatingSystem}-{architecture}-{abi}";
    }

    private static void DeleteTransientCimguizmoBuild(string repositoryRoot)
    {
        string path = Path.Combine(repositoryRoot, "extern", "cimguizmo", "build");
        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);
    }

    [GeneratedRegex(@"\[(DllImport|LibraryImport)\b|partial\s+.*\bextern\b|static\s+extern\b")]
    private static partial Regex NativeImportPattern();

    private readonly record struct NativeBuildStep(string project, string command);
}

internal sealed class AcceptanceOptions
{
    private const string C_DEFAULT_CONFIGURATION = "Release";

    private AcceptanceOptions(string bindGenRoot, string configuration, string dotnet)
    {
        this.bindGenRoot = bindGenRoot;
        this.configuration = configuration;
        this.dotnet = dotnet;
    }

    internal static string usage
        => "Usage: dotnet run --project build/verification/Inno.Build.NativeBindings -- "
           + "[--bindgen-root <dir>] [--configuration <Debug|Release>] [--dotnet <path>]";

    internal string bindGenRoot { get; }

    internal string configuration { get; }

    internal string dotnet { get; }

    internal static AcceptanceOptions Parse(IReadOnlyList<string> args)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int index = 0; index < args.Count; index++)
        {
            string argument = args[index];
            if (!argument.StartsWith("--", StringComparison.Ordinal))
                throw new ArgumentException($"Unexpected argument '{argument}'.{Environment.NewLine}{usage}");
            string key = argument[2..];
            if (++index >= args.Count)
                throw new ArgumentException($"Argument '{argument}' requires a value.");
            if (!values.TryAdd(key, args[index]))
                throw new ArgumentException($"Argument '{argument}' was specified more than once.");
        }

        string bindGenRoot = Path.GetFullPath(values.GetValueOrDefault(
            "bindgen-root",
            Path.Combine(FindRepositoryParent(), "BindGen-CS")));
        string configuration = values.GetValueOrDefault("configuration", C_DEFAULT_CONFIGURATION);
        if (configuration is not ("Debug" or "Release"))
            throw new ArgumentException("--configuration must be either Debug or Release.");
        string dotnet = values.GetValueOrDefault("dotnet", ResolveDotnet());

        string[] knownKeys = ["bindgen-root", "configuration", "dotnet"];
        string? unknownKey = values.Keys.FirstOrDefault(key => !knownKeys.Contains(key, StringComparer.Ordinal));
        if (unknownKey != null)
            throw new ArgumentException($"Unknown argument '--{unknownKey}'.{Environment.NewLine}{usage}");
        return new AcceptanceOptions(bindGenRoot, configuration, dotnet);
    }

    private static string FindRepositoryParent()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "InnoEngine.sln")))
                return directory.Parent?.FullName
                    ?? throw new InvalidOperationException("The InnoEngine repository has no parent directory.");
            directory = directory.Parent;
        }
        throw new InvalidOperationException("Could not locate the InnoEngine repository root.");
    }

    private static string ResolveDotnet()
    {
        string? hostPath = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
        if (!string.IsNullOrWhiteSpace(hostPath) && File.Exists(hostPath))
            return hostPath;
        string? dotnetRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        if (!string.IsNullOrWhiteSpace(dotnetRoot))
        {
            string rootedHost = Path.Combine(dotnetRoot, OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");
            if (File.Exists(rootedHost))
                return rootedHost;
        }
        string userHost = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".dotnet",
            OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");
        return File.Exists(userHost) ? userHost : "dotnet";
    }
}

internal static class ProcessRunner
{
    internal static void Run(string fileName, IReadOnlyList<string> arguments, string workingDirectory)
    {
        using Process process = Start(fileName, arguments, workingDirectory, captureOutput: false);
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Command '{FormatCommand(fileName, arguments)}' exited with code {process.ExitCode}.");
        }
    }

    internal static string Capture(string fileName, IReadOnlyList<string> arguments, string workingDirectory)
    {
        using Process process = Start(fileName, arguments, workingDirectory, captureOutput: true);
        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Command '{FormatCommand(fileName, arguments)}' exited with code {process.ExitCode}: "
                + error.Trim());
        }
        return output;
    }

    private static Process Start(
        string fileName,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        bool captureOutput)
    {
        Console.WriteLine($"> {FormatCommand(fileName, arguments)}");
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = captureOutput,
            RedirectStandardError = captureOutput
        };
        foreach (string argument in arguments)
            startInfo.ArgumentList.Add(argument);
        return Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Failed to start process '{fileName}'.");
    }

    private static string FormatCommand(string fileName, IReadOnlyList<string> arguments)
        => string.Join(' ', new[] { fileName }.Concat(arguments).Select(QuoteArgument));

    private static string QuoteArgument(string value)
        => value.Any(char.IsWhiteSpace) ? $"\"{value.Replace("\"", "\\\"")}\"" : value;
}
