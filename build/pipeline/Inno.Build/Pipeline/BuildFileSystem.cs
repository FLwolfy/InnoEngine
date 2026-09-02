using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Inno.Build;

internal static class BuildFileSystem
{
    internal static async ValueTask MergeDirectoryAsync(
        string source,
        string destination,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(source))
            return;
        Directory.CreateDirectory(destination);
        foreach (string directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        }
        foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            if (File.Exists(target))
                throw new IOException($"Target artifact '{Path.GetRelativePath(destination, target)}' collides with runtime content.");
            await CopyFileAsync(file, target, cancellationToken).ConfigureAwait(false);
        }
    }

    internal static async ValueTask CopyDirectoryAsync(
        string source,
        string destination,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(source))
            throw new DirectoryNotFoundException($"Build input directory '{source}' does not exist.");
        Directory.CreateDirectory(destination);
        foreach (string directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        }
        foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await CopyFileAsync(file, target, cancellationToken).ConfigureAwait(false);
        }
    }

    internal static async ValueTask CopyFileAsync(
        string source,
        string destination,
        CancellationToken cancellationToken)
    {
        await using FileStream input = new(
            source,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            128 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using FileStream output = new(
            destination,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            128 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
        await output.FlushAsync(cancellationToken).ConfigureAwait(false);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(destination, File.GetUnixFileMode(source));
    }

    internal static void InstallDirectoryAtomically(string source, string destination)
    {
        string backup = destination + ".backup-" + Guid.NewGuid().ToString("N");
        bool replaced = Directory.Exists(destination);
        if (replaced)
            Directory.Move(destination, backup);
        try
        {
            Directory.Move(source, destination);
            if (Directory.Exists(backup))
                Directory.Delete(backup, recursive: true);
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

    internal static void InstallFileAtomically(string source, string destination)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        string backup = destination + ".backup-" + Guid.NewGuid().ToString("N");
        bool replaced = File.Exists(destination);
        if (replaced)
            File.Move(destination, backup);
        try
        {
            File.Move(source, destination);
            if (File.Exists(backup))
                File.Delete(backup);
        }
        catch
        {
            if (File.Exists(destination))
                File.Delete(destination);
            if (File.Exists(backup))
                File.Move(backup, destination);
            throw;
        }
    }
}
