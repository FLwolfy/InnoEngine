using System;
using System.IO;

namespace Inno.Core.IO;

/// <summary>
/// Provides rollback-safe installation of complete directory trees.
/// </summary>
public static class AtomicDirectory
{
    /// <summary>
    /// Installs an existing directory tree and restores the previous destination on failure.
    /// </summary>
    /// <param name="source">
    /// The complete candidate directory.
    /// </param>
    /// <param name="destination">
    /// The destination directory.
    /// </param>
    public static void Install(string source, string destination)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);
        string candidate = Path.GetFullPath(source);
        string target = Path.GetFullPath(destination);
        if (!Directory.Exists(candidate))
            throw new DirectoryNotFoundException($"Atomic directory candidate '{candidate}' does not exist.");
        string? parent = Path.GetDirectoryName(target);
        if (string.IsNullOrEmpty(parent))
            throw new IOException($"Directory path '{target}' has no owning directory.");
        Directory.CreateDirectory(parent);

        string backup = target + ".backup-" + Guid.NewGuid().ToString("N");
        if (Directory.Exists(target))
            Directory.Move(target, backup);
        try
        {
            Directory.Move(candidate, target);
            if (Directory.Exists(backup))
                Directory.Delete(backup, recursive: true);
        }
        catch
        {
            if (Directory.Exists(target))
                Directory.Delete(target, recursive: true);
            if (Directory.Exists(backup))
                Directory.Move(backup, target);
            throw;
        }
    }
}
