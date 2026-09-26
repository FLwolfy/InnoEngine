using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Inno.Assets;
using Inno.Core.Serialization;

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

    internal static void RemapIdentities(
        string stagedSource,
        AssetPath source,
        AssetPath target,
        SerializationRegistry serialization,
        Action<AssetSampleTransformContext> transform)
    {
        var identities = new Dictionary<Guid, Guid>();
        var paths = new Dictionary<string, string>(StringComparer.Ordinal);
        var sourceIdentities = new Dictionary<string, (Guid oldId, Guid newId)>(StringComparer.Ordinal);
        string[] metadata = Directory.GetFiles(stagedSource, "*.imeta", SearchOption.AllDirectories);
        if (File.Exists(stagedSource + ".imeta"))
            metadata = [.. metadata, stagedSource + ".imeta"];
        foreach (string path in metadata.Order(StringComparer.Ordinal))
        {
            AssetSourceMeta meta = serialization.Deserialize<AssetSourceMeta>(File.ReadAllBytes(path));
            if (meta.persistentId == Guid.Empty)
                throw new InvalidDataException($"Sample metadata '{path}' has no persistent identity.");
            if (!identities.TryAdd(meta.persistentId, Guid.NewGuid()))
                throw new InvalidDataException($"Sample contains duplicate persistent identity '{meta.persistentId:D}'.");
            Guid oldId = meta.persistentId;
            meta.persistentId = identities[oldId];
            File.WriteAllBytes(path, serialization.Serialize(meta));
            string relative = Path.GetRelativePath(stagedSource, path);
            string suffix = string.Equals(path, stagedSource + ".imeta", StringComparison.Ordinal)
                ? string.Empty
                : relative[..^".imeta".Length].Replace('\\', '/');
            sourceIdentities.Add(suffix, (oldId, meta.persistentId));
            string oldLocal = string.IsNullOrEmpty(suffix)
                ? source.localPath : source.localPath + "/" + suffix;
            string newLocal = string.IsNullOrEmpty(suffix)
                ? target.localPath : target.localPath + "/" + suffix;
            paths[new AssetPath(source.source, oldLocal).ToString()] = newLocal;
            paths[oldLocal] = newLocal;
        }
        transform(new AssetSampleTransformContext(stagedSource, source, target, identities, sourceIdentities));
        foreach (string path in Directory.GetFiles(stagedSource, "*", SearchOption.AllDirectories)
                     .Where(static path => !path.EndsWith(".imeta", StringComparison.OrdinalIgnoreCase)))
        {
            byte[] original = File.ReadAllBytes(path);
            byte[] rewritten = SerializedIdentityRemapper.Rewrite(original, identities, paths);
            if (!original.AsSpan().SequenceEqual(rewritten))
                File.WriteAllBytes(path, rewritten);
        }
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
