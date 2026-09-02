using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

using Inno.Assets;
using Inno.Core.Serialization;

using IOFile = System.IO.File;

namespace Inno.Assets.Pipeline;

internal sealed class AssetArtifactStore
{
    private const int C_SHA256_HEX_LENGTH = 64;

    private readonly string m_root;
    private readonly string m_stagingRoot;
    private readonly SerializationRegistry m_serialization;

    internal AssetArtifactStore(string libraryRoot, SerializationRegistry serialization)
    {
        ArgumentNullException.ThrowIfNull(serialization);
        m_serialization = serialization;
        m_root = Path.Combine(libraryRoot, "Artifacts");
        m_stagingRoot = Path.Combine(m_root, ".staging");
        Directory.CreateDirectory(m_root);
        Directory.CreateDirectory(m_stagingRoot);
    }

    internal string root => m_root;

    internal AssetArtifactKey Commit(
        string inputFingerprint,
        IReadOnlyDictionary<string, ReadOnlyMemory<byte>> outputs)
    {
        if (outputs.Count == 0)
            throw new InvalidOperationException("An artifact bundle requires at least one output.");

        AssetArtifactKey key = ComputeKey(inputFingerprint, outputs);
        string finalPath = GetBundlePath(key);
        if (Directory.Exists(finalPath))
            return key;

        string stagingPath = Path.Combine(m_stagingRoot, Guid.NewGuid().ToString("N"));
        string outputRoot = Path.Combine(stagingPath, "outputs");
        Directory.CreateDirectory(outputRoot);
        try
        {
            AssetArtifactOutputData[] entries = outputs
                .OrderBy(static pair => pair.Key, StringComparer.Ordinal)
                .Select((pair, index) => WriteOutput(outputRoot, pair.Key, pair.Value, index))
                .ToArray();
            var manifest = new AssetArtifactManifest
            {
                key = key.value,
                outputs = entries
            };
            AssetFileTransaction.WriteAtomic(
                Path.Combine(stagingPath, "manifest"),
                m_serialization.Serialize(manifest));

            Directory.CreateDirectory(Path.GetDirectoryName(finalPath)!);
            try
            {
                Directory.Move(stagingPath, finalPath);
            }
            catch (IOException) when (Directory.Exists(finalPath))
            {
                Directory.Delete(stagingPath, recursive: true);
            }
            return key;
        }
        catch
        {
            if (Directory.Exists(stagingPath))
                Directory.Delete(stagingPath, recursive: true);
            throw;
        }
    }

    internal bool TryGet(
        AssetArtifactKey key,
        string outputName,
        out AssetArtifactInfo? artifact)
        => TryGet(key, outputName, serialization: null, out artifact);

    internal bool TryGet(
        AssetArtifactKey key,
        string outputName,
        SerializationGeneration? serialization,
        out AssetArtifactInfo? artifact)
    {
        artifact = null;
        if (key.isEmpty || string.IsNullOrWhiteSpace(outputName))
            return false;
        AssetArtifactManifest? manifest = ReadManifest(key, serialization);
        AssetArtifactOutputData output = manifest?.outputs.FirstOrDefault(value =>
            string.Equals(value.name, outputName, StringComparison.Ordinal)) ?? default;
        if (string.IsNullOrEmpty(output.fileName))
            return false;
        string path = Path.Combine(GetBundlePath(key), "outputs", output.fileName);
        if (!IOFile.Exists(path))
            return false;
        artifact = new AssetArtifactInfo(key, output.name, path, output.contentHash, output.length);
        return true;
    }

    internal byte[] Read(AssetArtifactKey key, string outputName)
    {
        return TryGet(key, outputName, out AssetArtifactInfo? artifact) && artifact is not null
            ? IOFile.ReadAllBytes(artifact.absolutePath)
            : [];
    }

    internal int Collect(
        IReadOnlySet<string> reachableKeys,
        TimeSpan gracePeriod,
        long maximumSizeBytes)
    {
        DateTime cutoff = DateTime.UtcNow - (gracePeriod < TimeSpan.Zero ? TimeSpan.Zero : gracePeriod);
        ArtifactDirectory[] bundles = EnumerateBundles();
        long totalSize = bundles.Sum(static bundle => bundle.size);
        int removed = 0;
        foreach (ArtifactDirectory bundle in bundles
                     .Where(bundle => !reachableKeys.Contains(bundle.key))
                     .OrderBy(static bundle => bundle.lastWriteUtc))
        {
            bool exceededLimit = maximumSizeBytes > 0 && totalSize > maximumSizeBytes;
            if (!exceededLimit && bundle.lastWriteUtc > cutoff)
                continue;
            try
            {
                Directory.Delete(bundle.path, recursive: true);
                totalSize -= bundle.size;
                removed++;
            }
            catch (IOException)
            {
                // A concurrent reader may temporarily retain a platform file handle.
            }
            catch (UnauthorizedAccessException)
            {
                // Read-only cache entries are retried by a later idle collection.
            }
        }
        return removed;
    }

    private AssetArtifactManifest? ReadManifest(
        AssetArtifactKey key,
        SerializationGeneration? serialization)
    {
        string path = Path.Combine(GetBundlePath(key), "manifest");
        if (!IOFile.Exists(path))
            return null;
        try
        {
            byte[] bytes = IOFile.ReadAllBytes(path);
            AssetArtifactManifest manifest = serialization is null
                ? m_serialization.Deserialize<AssetArtifactManifest>(bytes)
                : serialization.Deserialize<AssetArtifactManifest>(bytes);
            if (!string.Equals(manifest.key, key.value, StringComparison.Ordinal)
                || manifest.outputs.Length == 0
                || manifest.outputs.Any(static output =>
                    string.IsNullOrWhiteSpace(output.name)
                    || string.IsNullOrWhiteSpace(output.fileName)
                    || string.IsNullOrWhiteSpace(output.contentHash)
                    || output.length < 0))
            {
                throw new InvalidDataException($"Artifact bundle '{key}' has an invalid manifest contract.");
            }
            return manifest;
        }
        catch (Exception exception) when (exception is IOException
            or InvalidDataException
            or InvalidOperationException
            or ArgumentException)
        {
            throw new InvalidDataException(
                $"Artifact bundle '{key}' has a corrupt current-format manifest.",
                exception);
        }
    }

    private ArtifactDirectory[] EnumerateBundles()
    {
        var result = new List<ArtifactDirectory>();
        foreach (string firstLevel in Directory.EnumerateDirectories(m_root))
        {
            if (string.Equals(Path.GetFileName(firstLevel), ".staging", StringComparison.Ordinal))
                continue;
            foreach (string secondLevel in Directory.EnumerateDirectories(firstLevel))
            {
                foreach (string bundlePath in Directory.EnumerateDirectories(secondLevel))
                {
                    string key = Path.GetFileName(bundlePath).ToUpperInvariant();
                    if (key.Length != C_SHA256_HEX_LENGTH)
                        continue;
                    long size = Directory.EnumerateFiles(bundlePath, "*", SearchOption.AllDirectories)
                        .Sum(static path => new FileInfo(path).Length);
                    result.Add(new ArtifactDirectory(
                        key,
                        bundlePath,
                        Directory.GetLastWriteTimeUtc(bundlePath),
                        size));
                }
            }
        }
        return result.ToArray();
    }

    private string GetBundlePath(AssetArtifactKey key)
    {
        string value = key.value;
        if (value.Length < 4)
            throw new ArgumentException("Artifact keys must contain a SHA-256 value.", nameof(key));
        return Path.Combine(m_root, value[..2].ToLowerInvariant(), value[2..4].ToLowerInvariant(), value);
    }

    private static AssetArtifactOutputData WriteOutput(
        string outputRoot,
        string outputName,
        ReadOnlyMemory<byte> bytes,
        int index)
    {
        string fileName = index.ToString("D4") + ".bin";
        string path = Path.Combine(outputRoot, fileName);
        IOFile.WriteAllBytes(path, bytes.Span);
        return new AssetArtifactOutputData
        {
            name = outputName,
            fileName = fileName,
            contentHash = Convert.ToHexString(SHA256.HashData(bytes.Span)),
            length = bytes.Length
        };
    }

    private static AssetArtifactKey ComputeKey(
        string inputFingerprint,
        IReadOnlyDictionary<string, ReadOnlyMemory<byte>> outputs)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, "Inno.AssetArtifact");
        Append(hash, inputFingerprint);
        foreach (KeyValuePair<string, ReadOnlyMemory<byte>> output in outputs
                     .OrderBy(static pair => pair.Key, StringComparer.Ordinal))
        {
            Append(hash, output.Key);
            hash.AppendData(output.Value.Span);
        }
        return new AssetArtifactKey(Convert.ToHexString(hash.GetHashAndReset()));
    }

    private static void Append(IncrementalHash hash, string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
        hash.AppendData(BitConverter.GetBytes(bytes.Length));
        hash.AppendData(bytes);
    }

    private readonly record struct ArtifactDirectory(
        string key,
        string path,
        DateTime lastWriteUtc,
        long size);
}
