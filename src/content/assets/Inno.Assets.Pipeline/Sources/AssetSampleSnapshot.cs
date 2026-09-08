using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace Inno.Assets.Pipeline;

internal static class AssetSampleSnapshot
{
    internal static List<string> Capture(
        string source,
        string target,
        string targetLocalPath,
        AssetSourcePolicy sourcePolicy)
    {
        List<string> copied = CopyDirectory(source, target, targetLocalPath, sourcePolicy);
        string sourceMeta = source + ".imeta";
        if (File.Exists(sourceMeta))
            CopyStableFile(sourceMeta, target + ".imeta");
        return copied;
    }

    private static List<string> CopyDirectory(
        string source,
        string target,
        string targetLocalPath,
        AssetSourcePolicy sourcePolicy)
    {
        EnsureRegularDirectory(source);
        Directory.CreateDirectory(target);
        string[] directories = Directory.GetDirectories(source)
            .Where(path => !sourcePolicy.IsIgnored(Path.GetFileName(path), isDirectory: true))
            .Order(StringComparer.Ordinal)
            .ToArray();
        string[] files = Directory.GetFiles(source)
            .Where(path => ShouldCopyFile(path, sourcePolicy))
            .Order(StringComparer.Ordinal)
            .ToArray();
        var copied = new List<string>();
        for (int index = 0; index < directories.Length; index++)
        {
            string name = Path.GetFileName(directories[index]);
            string childLocalPath = targetLocalPath + "/" + name;
            copied.Add(childLocalPath);
            copied.AddRange(CopyDirectory(
                directories[index],
                Path.Combine(target, name),
                childLocalPath,
                sourcePolicy));
        }
        for (int index = 0; index < files.Length; index++)
        {
            string name = Path.GetFileName(files[index]);
            CopyStableFile(files[index], Path.Combine(target, name));
            if (!AssetSourcePolicy.IsGeneratedPath(name))
                copied.Add(targetLocalPath + "/" + name);
        }

        string[] currentDirectories = Directory.GetDirectories(source)
            .Where(path => !sourcePolicy.IsIgnored(Path.GetFileName(path), isDirectory: true))
            .Order(StringComparer.Ordinal)
            .ToArray();
        string[] currentFiles = Directory.GetFiles(source)
            .Where(path => ShouldCopyFile(path, sourcePolicy))
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (!directories.SequenceEqual(currentDirectories, StringComparer.Ordinal) ||
            !files.SequenceEqual(currentFiles, StringComparer.Ordinal))
        {
            throw new IOException($"Sample source '{source}' changed while it was being imported.");
        }
        return copied;
    }

    private static bool ShouldCopyFile(string path, AssetSourcePolicy sourcePolicy)
    {
        string name = Path.GetFileName(path);
        if (name.EndsWith(".imeta", StringComparison.OrdinalIgnoreCase))
            return true;
        if (name.EndsWith(".abin", StringComparison.OrdinalIgnoreCase))
            return false;
        return !sourcePolicy.IsIgnored(name, isDirectory: false);
    }

    private static void CopyStableFile(string source, string target)
    {
        EnsureRegularFile(source);
        FileInfo before = new(source);
        long beforeLength = before.Length;
        DateTime beforeWriteTime = before.LastWriteTimeUtc;
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Copy(source, target, overwrite: false);
        EnsureRegularFile(source);
        FileInfo after = new(source);
        if (after.Length != beforeLength || after.LastWriteTimeUtc != beforeWriteTime)
            throw new IOException($"Sample source file '{source}' changed while it was being imported.");
        using FileStream sourceStream = File.OpenRead(source);
        using FileStream targetStream = File.OpenRead(target);
        byte[] sourceHash = SHA256.HashData(sourceStream);
        byte[] targetHash = SHA256.HashData(targetStream);
        if (!sourceHash.AsSpan().SequenceEqual(targetHash))
            throw new IOException($"Sample source file '{source}' changed while it was being imported.");
    }

    private static void EnsureRegularDirectory(string path)
    {
        if (!Directory.Exists(path))
            throw new DirectoryNotFoundException($"Sample directory '{path}' does not exist.");
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new IOException($"Sample directory '{path}' cannot be a symbolic link.");
    }

    private static void EnsureRegularFile(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("Sample source file does not exist.", path);
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new IOException($"Sample source file '{path}' cannot be a symbolic link.");
    }
}
