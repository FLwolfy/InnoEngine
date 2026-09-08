using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;

namespace Inno.Runtime;

/// <summary>
/// Verifies and materializes one immutable content-addressed Player content pack.
/// </summary>
public static class RuntimeContentDeployment
{
    /// <summary>
    /// Materializes the single packaged content pack into an application-owned persistent cache.
    /// </summary>
    /// <param name="packagedContentRoot">
    /// The read-only packaged Content directory beside the Player.
    /// </param>
    /// <param name="persistentRoot">
    /// The application-ID-scoped writable persistent directory.
    /// </param>
    /// <returns>
    /// The verified runtime asset root consumed by the engine session.
    /// </returns>
    /// <exception cref="DirectoryNotFoundException">
    /// Thrown when packaged content is unavailable.
    /// </exception>
    /// <exception cref="InvalidDataException">
    /// Thrown when the pack set, hash, or archive entries are invalid.
    /// </exception>
    /// <exception cref="IOException">
    /// Thrown when the persistent cache cannot be created atomically.
    /// </exception>
    public static string Materialize(string packagedContentRoot, string persistentRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packagedContentRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(persistentRoot);
        string packaged = Path.GetFullPath(packagedContentRoot);
        if (!Directory.Exists(packaged))
            throw new DirectoryNotFoundException($"Packaged content root '{packaged}' does not exist.");
        string[] packs = Directory.EnumerateFiles(packaged, "content-*.pack", SearchOption.TopDirectoryOnly).ToArray();
        if (packs.Length != 1)
            throw new InvalidDataException("A Player deployment must contain exactly one content pack.");
        string expectedHash = Path.GetFileNameWithoutExtension(packs[0])["content-".Length..];
        if (expectedHash.Length != 64 || expectedHash.Any(static value => !Uri.IsHexDigit(value)))
            throw new InvalidDataException("The content pack file name does not contain a SHA-256 identity.");
        using (FileStream stream = File.OpenRead(packs[0]))
        {
            string actualHash = Convert.ToHexString(SHA256.HashData(stream));
            if (!string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The content pack hash does not match its content identity.");
        }

        string cacheRoot = Path.Combine(Path.GetFullPath(persistentRoot), "Content", expectedHash.ToUpperInvariant());
        string completion = Path.Combine(cacheRoot, ".complete");
        if (File.Exists(completion))
            return cacheRoot;

        Directory.CreateDirectory(Path.GetDirectoryName(cacheRoot)!);
        string staging = cacheRoot + ".staging-" + Guid.NewGuid().ToString("N");
        try
        {
            Directory.CreateDirectory(staging);
            ExtractVerified(packs[0], staging);
            File.WriteAllText(Path.Combine(staging, ".complete"), expectedHash.ToUpperInvariant());
            if (Directory.Exists(cacheRoot))
                Directory.Delete(cacheRoot, recursive: true);
            Directory.Move(staging, cacheRoot);
            return cacheRoot;
        }
        finally
        {
            if (Directory.Exists(staging))
                Directory.Delete(staging, recursive: true);
        }
    }

    private static void ExtractVerified(string packPath, string destinationRoot)
    {
        string normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(destinationRoot));
        using ZipArchive archive = ZipFile.OpenRead(packPath);
        foreach (ZipArchiveEntry entry in archive.Entries.OrderBy(static value => value.FullName, StringComparer.Ordinal))
        {
            string portable = entry.FullName.Replace('\\', '/');
            if (portable.Length == 0
                || portable.StartsWith("/", StringComparison.Ordinal)
                || portable.Split('/').Any(static segment => segment is "." or ".."))
            {
                throw new InvalidDataException($"Content pack entry '{entry.FullName}' is not portable.");
            }
            string destination = Path.GetFullPath(Path.Combine(
                normalizedRoot,
                portable.Replace('/', Path.DirectorySeparatorChar)));
            StringComparison comparison = OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            if (!destination.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, comparison))
                throw new InvalidDataException($"Content pack entry '{entry.FullName}' escapes the cache root.");
            if (entry.FullName.EndsWith("/", StringComparison.Ordinal))
            {
                Directory.CreateDirectory(destination);
                continue;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            using Stream source = entry.Open();
            using FileStream target = new(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            source.CopyTo(target);
        }
    }
}
