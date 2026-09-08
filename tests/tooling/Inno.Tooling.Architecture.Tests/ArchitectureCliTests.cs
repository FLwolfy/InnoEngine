using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Xunit;

namespace Inno.Tooling.Architecture.Tests;

public sealed class ArchitectureCliTests
{
    [Theory]
    [InlineData("src/services/audio/Probe.cs", "global using System;", "global using", true)]
    [InlineData("src/composition/player/Inno.Player/Probe.cs", "internal class Probe { private BgfxDevice device; }", "backend-neutral", true)]
    [InlineData("src/content/assets/Probe.cs", "internal class Probe { void Read() { context.With<IAssetReferenceResolver>(resolver); } }", "owner-complete", true)]
    [InlineData("src/services/audio/Probe.cs", "public class Probe { public MaEngine engine; }", "native implementation", true)]
    [InlineData("src/services/audio/Probe.cs", "internal class Probe { void Release() { OnCleanupFailed(phase, failure); } }", "diagnostic-only", true)]
    [InlineData("src/services/audio/Probe.cs", "internal class Probe { void Release() { try { } catch (RetirementPendingException) { throw; } } }", "wrapped pending ownership", true)]
    [InlineData("src/services/audio/Probe.cs", "internal class Probe { void Release() { try { } catch (Inno.Core.Execution.RetirementTimeoutException failure) { throw; } } }", "wrapped pending ownership", true)]
    [InlineData("src/services/audio/Probe.cs", "internal class Probe { void Release() { try { } catch (Exception error) when (RetirementPendingException.Find(error) is not null) { throw; } } }", "wrapped pending ownership", false)]
    public async Task CliValidatesSourceContracts(string relative, string source, string expected, bool rejected)
    {
        string root = Path.Combine(Path.GetTempPath(), "InnoArchitectureTests", Guid.NewGuid().ToString("N"));
        try
        {
            foreach (string folder in new[] { "src", "native", "build", "tools", "tests" })
                Directory.CreateDirectory(Path.Combine(root, folder));
            File.WriteAllText(Path.Combine(root, "InnoEngine.sln"), """
                Microsoft Visual Studio Solution File, Format Version 12.00
                Project("{2150E333-8FDC-42A3-9474-1A3956D46DE8}") = "src", "src", "{BBD55508-6095-4D53-B0E4-59B04BFB9376}"
                EndProject
                Project("{2150E333-8FDC-42A3-9474-1A3956D46DE8}") = "tests", "tests", "{A98FE84D-9721-4B99-950D-05CF68C303F2}"
                EndProject
                Global
                EndGlobal
                """);
            (int baselineCode, string baseline) = await Run(root);
            // This isolated source fixture intentionally has no product projects or compiled assemblies.
            Assert.Equal(1, baselineCode);
            Assert.Contains("failed with 3 violation(s)", baseline);
            Assert.DoesNotContain(expected, baseline, StringComparison.OrdinalIgnoreCase);
            string path = Path.Combine(root, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, source);
            (int code, string output) = await Run(root);
            Assert.Equal(1, code);
            if (rejected)
                Assert.Contains(expected, output, StringComparison.OrdinalIgnoreCase);
            else
            {
                Assert.DoesNotContain(expected, output, StringComparison.OrdinalIgnoreCase);
                Assert.Contains("failed with 3 violation(s)", output);
            }
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    private static async Task<(int, string)> Run(string root)
    {
        DirectoryInfo? repository = new(AppContext.BaseDirectory);
        while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "InnoEngine.sln")))
            repository = repository.Parent;
        Assert.NotNull(repository);
        string dotnet = Path.GetFullPath("../../../dotnet", RuntimeEnvironment.GetRuntimeDirectory());
        if (OperatingSystem.IsWindows())
            dotnet += ".exe";
        var start = new ProcessStartInfo(dotnet)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        start.ArgumentList.Add(Path.Combine(repository!.FullName, "tools/Inno.Tooling.Architecture/bin/Debug/net9.0/Inno.Tooling.Architecture.dll"));
        start.ArgumentList.Add(root);
        using Process process = Process.Start(start)!;
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return (process.ExitCode, await output + await error);
    }
}
