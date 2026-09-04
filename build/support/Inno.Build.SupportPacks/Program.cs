using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Inno.Build;

namespace Inno.Build.SupportPacks;

internal static class Program
{
    private static async Task<int> Main(string[] arguments)
    {
        try
        {
            SupportPackCommand command = SupportPackCommand.Parse(arguments);
            using var cancellation = new CancellationTokenSource();
            ConsoleCancelEventHandler handler = (_, eventArguments) =>
            {
                eventArguments.Cancel = true;
                cancellation.Cancel();
            };
            Console.CancelKeyPress += handler;
            try
            {
                string installed = await SupportPackPublisher.PublishAsync(command, cancellation.Token)
                    .ConfigureAwait(false);
                Console.WriteLine(installed);
                return 0;
            }
            finally
            {
                Console.CancelKeyPress -= handler;
            }
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Support Pack generation was canceled.");
            return 2;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }
}

internal sealed record SupportPackCommand(
    string engineRoot,
    string outputRoot,
    BuildTargetId target,
    string dotnetHost)
{
    internal static SupportPackCommand Parse(IReadOnlyList<string> arguments)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int index = 0; index < arguments.Count; index += 2)
        {
            if (index + 1 >= arguments.Count || !arguments[index].StartsWith("--", StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "Usage: --engine-root <path> --output <path> --target <macos-arm64|windows-x64> [--dotnet <path>].");
            }
            if (!values.TryAdd(arguments[index], arguments[index + 1]))
                throw new ArgumentException($"Argument '{arguments[index]}' was supplied more than once.");
        }
        string engineRoot = Require(values, "--engine-root");
        string outputRoot = Require(values, "--output");
        BuildTargetId target = new(Require(values, "--target"));
        if (target != BuildTargetId.macOSArm64 && target != BuildTargetId.windowsX64)
            throw new ArgumentException($"Support Pack target '{target}' is not implemented.");
        string dotnetHost = values.GetValueOrDefault("--dotnet")
                            ?? Environment.GetEnvironmentVariable("DOTNET_HOST_PATH")
                            ?? "dotnet";
        return new SupportPackCommand(
            Path.GetFullPath(engineRoot),
            Path.GetFullPath(outputRoot),
            target,
            dotnetHost);
    }

    private static string Require(IReadOnlyDictionary<string, string> values, string name)
        => values.TryGetValue(name, out string? value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new ArgumentException($"Required argument '{name}' is missing.");
}

internal static class SupportPackPublisher
{
    private static readonly HashSet<string> S_PUBLISH_METADATA_EXTENSIONS = new(StringComparer.OrdinalIgnoreCase)
    {
        ".dbg", ".map", ".pdb", ".xml"
    };

    internal static async ValueTask<string> PublishAsync(
        SupportPackCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        string solution = Path.Combine(command.engineRoot, "InnoEngine.sln");
        if (!File.Exists(solution))
            throw new DirectoryNotFoundException($"Engine root '{command.engineRoot}' has no InnoEngine.sln.");
        string playerProject = Path.Combine(
            command.engineRoot,
            "src",
            "runtime",
            "Inno.Player",
            "Inno.Player.csproj");
        if (!File.Exists(playerProject))
            throw new FileNotFoundException("The Player composition project does not exist.", playerProject);

        Directory.CreateDirectory(command.outputRoot);
        string staging = Path.Combine(
            command.outputRoot,
            ".support-pack-" + command.target.value + "-" + Guid.NewGuid().ToString("N"));
        string publishedRuntime = staging + ".published-runtime";
        string destination = Path.Combine(command.outputRoot, command.target.value);
        string backup = destination + ".replaced-" + Guid.NewGuid().ToString("N");
        try
        {
            await RunPublishAsync(command, playerProject, publishedRuntime, cancellationToken).ConfigureAwait(false);
            ComposeRuntimeClosure(publishedRuntime, staging, cancellationToken);
            CopyNativeRuntime(command, staging);
            cancellationToken.ThrowIfCancellationRequested();
            bool replaced = Directory.Exists(destination);
            if (replaced)
                Directory.Move(destination, backup);
            try
            {
                Directory.Move(staging, destination);
                var supportPacks = new PlayerSupportPackCatalog(command.outputRoot);
                string verified = supportPacks.Resolve(command.target);
                if (Directory.Exists(backup))
                    Directory.Delete(backup, recursive: true);
                return verified;
            }
            catch
            {
                if (Directory.Exists(destination))
                    Directory.Delete(destination, recursive: true);
                if (Directory.Exists(backup))
                    Directory.Move(backup, destination);
                throw;
            }
        }
        finally
        {
            if (Directory.Exists(staging))
                Directory.Delete(staging, recursive: true);
            if (Directory.Exists(publishedRuntime))
                Directory.Delete(publishedRuntime, recursive: true);
            if (Directory.Exists(backup))
                Directory.Delete(backup, recursive: true);
        }
    }

    private static async ValueTask RunPublishAsync(
        SupportPackCommand command,
        string playerProject,
        string staging,
        CancellationToken cancellationToken)
    {
        string runtimeIdentifier = command.target == BuildTargetId.macOSArm64
            ? "osx-arm64"
            : "win-x64";
        var startInfo = new ProcessStartInfo
        {
            FileName = command.dotnetHost,
            WorkingDirectory = command.engineRoot,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("publish");
        startInfo.ArgumentList.Add(playerProject);
        startInfo.ArgumentList.Add("--disable-build-servers");
        startInfo.ArgumentList.Add("-m:1");
        startInfo.ArgumentList.Add("-nodeReuse:false");
        startInfo.ArgumentList.Add("--configuration");
        startInfo.ArgumentList.Add("Release");
        startInfo.ArgumentList.Add("--runtime");
        startInfo.ArgumentList.Add(runtimeIdentifier);
        startInfo.ArgumentList.Add("--self-contained");
        startInfo.ArgumentList.Add("true");
        startInfo.ArgumentList.Add("--output");
        startInfo.ArgumentList.Add(staging);
        startInfo.ArgumentList.Add("--nologo");
        startInfo.ArgumentList.Add("-p:DebugType=None");
        startInfo.ArgumentList.Add("-p:DebugSymbols=false");
        startInfo.ArgumentList.Add("-p:CopyOutputSymbolsToPublishDirectory=false");
        startInfo.ArgumentList.Add("-p:CopyDebugSymbolFilesFromPackages=false");
        startInfo.ArgumentList.Add("-p:AllowedReferenceRelatedFileExtensions=");

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
            throw new InvalidOperationException("The .NET publish process could not be started.");
        Task<string> output = process.StandardOutput.ReadToEndAsync(cancellationToken);
        Task<string> error = process.StandardError.ReadToEndAsync(cancellationToken);
        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
            throw;
        }
        string standardOutput = await output.ConfigureAwait(false);
        string standardError = await error.ConfigureAwait(false);
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Player Support Pack publish failed with exit code {process.ExitCode}." +
                Environment.NewLine + standardOutput + Environment.NewLine + standardError);
        }
    }

    private static void CopyNativeRuntime(SupportPackCommand command, string staging)
    {
        string nativeProducts = Path.Combine(command.engineRoot, ".lib");
        string nativePlatform = command.target == BuildTargetId.macOSArm64
            ? "osx-arm64"
            : "windows-x64";
        string[] components = ["bgfx", "sdl3"];
        string destination = Path.Combine(staging, "native");
        foreach (string component in components)
        {
            string source = Path.Combine(nativeProducts, component, nativePlatform);
            if (!Directory.Exists(source))
            {
                throw new DirectoryNotFoundException(
                    $"Release native runtime output for '{component}' and '{command.target}' does not exist at '{source}'.");
            }
            string[] files = Directory.EnumerateFiles(source, "*release*", SearchOption.TopDirectoryOnly).ToArray();
            if (files.Length == 0)
            {
                throw new InvalidDataException(
                    $"Release native runtime output for '{component}' and '{command.target}' is empty.");
            }
            string componentDestination = Path.Combine(destination, component, command.target.value);
            Directory.CreateDirectory(componentDestination);
            foreach (string file in files.Order(StringComparer.Ordinal))
                File.Copy(file, Path.Combine(componentDestination, Path.GetFileName(file)));
        }
    }

    private static void ComposeRuntimeClosure(
        string publishedRuntime,
        string staging,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(publishedRuntime))
            throw new DirectoryNotFoundException("The Player publish stage produced no runtime directory.");

        Directory.CreateDirectory(staging);
        foreach (string source in Directory.EnumerateFiles(publishedRuntime, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (S_PUBLISH_METADATA_EXTENSIONS.Contains(Path.GetExtension(source)))
                continue;
            string relativePath = Path.GetRelativePath(publishedRuntime, source);
            string destination = Path.Combine(staging, relativePath);
            string? destinationDirectory = Path.GetDirectoryName(destination);
            if (!string.IsNullOrEmpty(destinationDirectory))
                Directory.CreateDirectory(destinationDirectory);
            File.Copy(source, destination);
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(destination, File.GetUnixFileMode(source));
        }
    }
}
