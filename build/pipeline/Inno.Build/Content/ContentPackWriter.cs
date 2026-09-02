using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace Inno.Build;

internal static class ContentPackWriter
{
    private static readonly DateTimeOffset S_ARCHIVE_TIME = new(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);

    internal static async ValueTask<(string packPath, string contentHash)> WriteAsync(
        string sourceRoot,
        string contentRoot,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(contentRoot);
        string temporary = Path.Combine(contentRoot, ".content.pack.staging");
        if (File.Exists(temporary))
            File.Delete(temporary);
        try
        {
            await using (FileStream stream = new(
                             temporary,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             128 * 1024,
                             FileOptions.Asynchronous | FileOptions.SequentialScan))
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach (string file in Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories)
                             .OrderBy(static value => value, StringComparer.Ordinal))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    string relative = Path.GetRelativePath(sourceRoot, file).Replace('\\', '/');
                    ZipArchiveEntry entry = archive.CreateEntry(relative, CompressionLevel.Optimal);
                    entry.LastWriteTime = S_ARCHIVE_TIME;
                    await using Stream output = entry.Open();
                    await using FileStream input = new(
                        file,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.Read,
                        128 * 1024,
                        FileOptions.Asynchronous | FileOptions.SequentialScan);
                    await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
                }
            }
            cancellationToken.ThrowIfCancellationRequested();
            string contentHash;
            await using (FileStream input = new(
                             temporary,
                             FileMode.Open,
                             FileAccess.Read,
                             FileShare.Read,
                             128 * 1024,
                             FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                contentHash = Convert.ToHexString(await SHA256.HashDataAsync(input, cancellationToken).ConfigureAwait(false));
            }
            string packPath = Path.Combine(contentRoot, $"content-{contentHash}.pack");
            File.Move(temporary, packPath);
            return (packPath, contentHash);
        }
        catch
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
            throw;
        }
    }
}
