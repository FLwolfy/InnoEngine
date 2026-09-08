using System;
using System.IO;
using System.IO.Compression;

using Inno.Assets;
using Inno.Assets.Pipeline;

namespace Inno.Editor.Panel.FileBrowser;

internal static class AssetSourceArchive
{
    private const string C_SOURCE_ENTRY = "source";
    private const string C_META_ENTRY = "source.imeta";

    internal static byte[] Capture(
        AssetPipeline assets,
        string relativePath,
        out bool isDirectory)
    {
        ArgumentNullException.ThrowIfNull(assets);
        string source = GetSourcePath(assets, relativePath);
        isDirectory = Directory.Exists(source);
        if (!isDirectory && !File.Exists(source))
            throw new FileNotFoundException($"Asset source '{relativePath}' does not exist.", source);

        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            if (isDirectory)
            {
                _ = archive.CreateEntry(C_SOURCE_ENTRY + "/");
                string[] directories = Directory.GetDirectories(source, "*", SearchOption.AllDirectories);
                Array.Sort(directories, StringComparer.Ordinal);
                for (int i = 0; i < directories.Length; i++)
                {
                    string local = Path.GetRelativePath(source, directories[i]).Replace('\\', '/');
                    _ = archive.CreateEntry($"{C_SOURCE_ENTRY}/{local}/");
                }
                string[] files = Directory.GetFiles(source, "*", SearchOption.AllDirectories);
                Array.Sort(files, StringComparer.Ordinal);
                for (int i = 0; i < files.Length; i++)
                {
                    string local = Path.GetRelativePath(source, files[i]).Replace('\\', '/');
                    archive.CreateEntryFromFile(
                        files[i],
                        $"{C_SOURCE_ENTRY}/{local}",
                        CompressionLevel.Fastest);
                }
            }
            else
            {
                archive.CreateEntryFromFile(source, C_SOURCE_ENTRY, CompressionLevel.Fastest);
            }

            string meta = source + ".imeta";
            if (File.Exists(meta))
                archive.CreateEntryFromFile(meta, C_META_ENTRY, CompressionLevel.Fastest);
        }
        return stream.ToArray();
    }

    internal static void Restore(
        AssetPipeline assets,
        string relativePath,
        bool isDirectory,
        ReadOnlySpan<byte> data)
    {
        ArgumentNullException.ThrowIfNull(assets);
        string target = GetSourcePath(assets, relativePath);
        if (File.Exists(target) || Directory.Exists(target))
            throw new IOException($"Asset source '{relativePath}' already exists.");
        string? parent = Path.GetDirectoryName(target);
        if (!string.IsNullOrEmpty(parent))
            Directory.CreateDirectory(parent);

        string stagingDirectory = Path.Combine(
            Path.GetTempPath(),
            "InnoAssetHistory",
            Guid.NewGuid().ToString("N"));
        string stagingSource = Path.Combine(stagingDirectory, C_SOURCE_ENTRY);
        string stagingMeta = Path.Combine(stagingDirectory, C_META_ENTRY);
        bool sourceCommitted = false;
        bool metaCommitted = false;
        try
        {
            Directory.CreateDirectory(stagingDirectory);
            using (var stream = new MemoryStream(data.ToArray(), writable: false))
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false))
            {
                string stagingRoot = Path.GetFullPath(stagingDirectory) + Path.DirectorySeparatorChar;
                for (int i = 0; i < archive.Entries.Count; i++)
                {
                    ZipArchiveEntry entry = archive.Entries[i];
                    string destination = ResolveDestination(
                        stagingSource,
                        stagingMeta,
                        isDirectory,
                        entry.FullName);
                    string fullDestination = Path.GetFullPath(destination);
                    if (!fullDestination.StartsWith(stagingRoot, StringComparison.Ordinal))
                        throw new InvalidDataException("Asset history archive escapes its staging directory.");
                    if (entry.FullName.EndsWith("/", StringComparison.Ordinal))
                    {
                        Directory.CreateDirectory(fullDestination);
                        continue;
                    }
                    string? destinationParent = Path.GetDirectoryName(fullDestination);
                    if (!string.IsNullOrEmpty(destinationParent))
                        Directory.CreateDirectory(destinationParent);
                    entry.ExtractToFile(fullDestination, overwrite: false);
                }
            }

            if (isDirectory ? !Directory.Exists(stagingSource) : !File.Exists(stagingSource))
                throw new InvalidDataException("Asset history archive does not contain its source payload.");
            MoveSource(stagingSource, target, isDirectory);
            sourceCommitted = true;
            if (File.Exists(stagingMeta))
            {
                File.Move(stagingMeta, target + ".imeta");
                metaCommitted = true;
            }
            assets.Rescan();
            assets.WaitForIdle();
        }
        catch
        {
            if (metaCommitted && File.Exists(target + ".imeta"))
                File.Move(target + ".imeta", stagingMeta);
            if (sourceCommitted && (isDirectory ? Directory.Exists(target) : File.Exists(target)))
                MoveSource(target, stagingSource, isDirectory);
            if (sourceCommitted)
            {
                try
                {
                    assets.Rescan();
                    assets.WaitForIdle();
                }
                catch
                {
                }
            }
            throw;
        }
        finally
        {
            if (Directory.Exists(stagingDirectory))
                Directory.Delete(stagingDirectory, recursive: true);
        }
    }

    private static string ResolveDestination(
        string stagingSource,
        string stagingMeta,
        bool isDirectory,
        string entryName)
    {
        if (string.Equals(entryName, C_META_ENTRY, StringComparison.Ordinal))
            return stagingMeta;
        if (!isDirectory && string.Equals(entryName, C_SOURCE_ENTRY, StringComparison.Ordinal))
            return stagingSource;
        string prefix = C_SOURCE_ENTRY + "/";
        if (isDirectory && entryName.StartsWith(prefix, StringComparison.Ordinal))
        {
            return Path.Combine(
                stagingSource,
                entryName[prefix.Length..].Replace('/', Path.DirectorySeparatorChar));
        }
        throw new InvalidDataException($"Unknown asset history archive entry '{entryName}'.");
    }

    private static string GetSourcePath(AssetPipeline assets, string relativePath)
    {
        string root = Path.GetFullPath(assets.assetRoot) + Path.DirectorySeparatorChar;
        string result = Path.GetFullPath(Path.Combine(
            assets.assetRoot,
            relativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!result.StartsWith(root, StringComparison.Ordinal))
            throw new InvalidDataException($"Asset history path '{relativePath}' escapes the asset root.");
        return result;
    }

    private static void MoveSource(string source, string target, bool isDirectory)
    {
        if (isDirectory)
            Directory.Move(source, target);
        else
            File.Move(source, target);
    }
}
