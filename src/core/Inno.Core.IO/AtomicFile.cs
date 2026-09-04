using System;
using System.IO;

namespace Inno.Core.IO;

/// <summary>
/// Provides durable same-directory file replacement without exposing partial destination content.
/// </summary>
public static class AtomicFile
{
    /// <summary>
    /// Writes a complete byte payload and atomically installs it at the destination.
    /// </summary>
    /// <param name="path">
    /// The destination file path.
    /// </param>
    /// <param name="data">
    /// The complete file payload.
    /// </param>
    public static void WriteAllBytes(string path, ReadOnlySpan<byte> data)
    {
        string destination = NormalizeDestination(path);
        string candidate = destination + ".staging-" + Guid.NewGuid().ToString("N");
        try
        {
            using (var stream = new FileStream(
                       candidate,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       32 * 1024,
                       FileOptions.WriteThrough))
            {
                stream.Write(data);
                stream.Flush(flushToDisk: true);
            }
            Install(candidate, destination);
        }
        finally
        {
            if (File.Exists(candidate))
                File.Delete(candidate);
        }
    }

    /// <summary>
    /// Atomically installs an existing same-directory candidate file.
    /// </summary>
    /// <param name="source">
    /// The complete candidate file beside the destination.
    /// </param>
    /// <param name="destination">
    /// The destination file path beside the candidate.
    /// </param>
    public static void Install(string source, string destination)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        string candidate = Path.GetFullPath(source);
        string target = NormalizeDestination(destination);
        if (!File.Exists(candidate))
            throw new FileNotFoundException("The atomic file candidate does not exist.", candidate);
        if (string.Equals(candidate, target, PathComparison()))
            throw new ArgumentException("The atomic file candidate and destination must be different paths.", nameof(source));

        string candidateDirectory = Path.GetDirectoryName(candidate)!;
        string targetDirectory = Path.GetDirectoryName(target)!;
        if (!string.Equals(candidateDirectory, targetDirectory, PathComparison()))
            throw new ArgumentException(
                "Atomic file installation requires the candidate and destination to share a directory.",
                nameof(source));

        File.Move(candidate, target, overwrite: true);
    }

    private static string NormalizeDestination(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string destination = Path.GetFullPath(path);
        string? directory = Path.GetDirectoryName(destination);
        if (string.IsNullOrEmpty(directory))
            throw new IOException($"File path '{destination}' has no owning directory.");
        Directory.CreateDirectory(directory);
        return destination;
    }

    private static StringComparison PathComparison()
        => OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
}
