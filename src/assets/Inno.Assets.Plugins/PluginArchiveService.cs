using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Inno.Assets.Core;
using Inno.Assets.File;
using Inno.Core.Serialization;

using IOFile = System.IO.File;

namespace Inno.Assets.Plugins;

/// <summary>Discovers, validates, safely extracts, and dependency-orders local ZIP Plugins.</summary>
public sealed class PluginArchiveService
{
    private static readonly HashSet<string> S_FORBIDDEN_BINARY_EXTENSIONS = new(StringComparer.OrdinalIgnoreCase)
    {
        ".dll", ".exe", ".dylib", ".so", ".a", ".lib"
    };

    private readonly string m_pluginRoot;
    private readonly string m_cacheRoot;
    private readonly PluginArchiveLimits m_limits;

    /// <summary>Creates a local Plugin archive service.</summary>
    /// <param name="pluginRoot">Project Plugins directory containing installed ZIP files.</param>
    /// <param name="libraryRoot">Project rebuildable Library directory.</param>
    /// <param name="limits">Optional bounded archive limits.</param>
    public PluginArchiveService(
        string pluginRoot,
        string libraryRoot,
        PluginArchiveLimits? limits = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(libraryRoot);
        m_pluginRoot = Path.GetFullPath(pluginRoot);
        m_cacheRoot = Path.Combine(Path.GetFullPath(libraryRoot), "Plugins");
        m_limits = limits ?? PluginArchiveLimits.defaults;
        Directory.CreateDirectory(m_pluginRoot);
        Directory.CreateDirectory(m_cacheRoot);
    }

    /// <summary>Scans every installed ZIP into one isolated candidate snapshot.</summary>
    /// <param name="trustedPluginIds">Stable Plugin IDs whose source code may execute.</param>
    /// <returns>Dependency-ordered candidates plus isolated diagnostics.</returns>
    public PluginScanResult Scan(IReadOnlySet<string> trustedPluginIds)
    {
        ArgumentNullException.ThrowIfNull(trustedPluginIds);
        var candidates = new List<PluginArchiveCandidate>();
        var diagnostics = new List<PluginArchiveDiagnostic>();
        foreach (string archivePath in Directory.GetFiles(m_pluginRoot, "*.zip", SearchOption.TopDirectoryOnly)
                     .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                candidates.Add(ValidateAndExtract(archivePath, trustedPluginIds));
            }
            catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException)
            {
                diagnostics.Add(new PluginArchiveDiagnostic(archivePath, exception.Message));
            }
        }

        IReadOnlyList<PluginArchiveCandidate> ordered = ValidateDependencyGraph(candidates, diagnostics);
        var activatable = new HashSet<string>(StringComparer.Ordinal);
        foreach (PluginArchiveCandidate candidate in ordered)
        {
            if (!candidate.canActivate)
            {
                diagnostics.Add(new PluginArchiveDiagnostic(
                    candidate.archivePath,
                    $"Plugin '{candidate.manifest.pluginId}' contains code and awaits project trust."));
                continue;
            }
            string? blockedDependency = candidate.manifest.dependencies.FirstOrDefault(
                dependency => !activatable.Contains(dependency));
            if (blockedDependency is not null)
            {
                diagnostics.Add(new PluginArchiveDiagnostic(
                    candidate.archivePath,
                    $"Plugin '{candidate.manifest.pluginId}' awaits inactive dependency '{blockedDependency}'."));
                continue;
            }
            activatable.Add(candidate.manifest.pluginId);
        }
        return new PluginScanResult(ordered, diagnostics);
    }

    /// <summary>Gets read-only mounts that passed validation, dependencies, and code trust.</summary>
    /// <param name="result">Complete scan result.</param>
    /// <returns>Dependency-ordered mount snapshot suitable for the common AssetManager.</returns>
    public static IReadOnlyList<AssetSourceMount> GetActivatableMounts(PluginScanResult result)
        => GetActivatableCandidates(result).Select(static candidate => candidate.sourceMount).ToArray();

    /// <summary>Gets candidates whose trust and complete dependency chain permit activation.</summary>
    /// <param name="result">Complete scan result.</param>
    /// <returns>Dependency-ordered candidates suitable for one activation transaction.</returns>
    public static IReadOnlyList<PluginArchiveCandidate> GetActivatableCandidates(PluginScanResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var activeIds = new HashSet<string>(StringComparer.Ordinal);
        var candidates = new List<PluginArchiveCandidate>();
        foreach (PluginArchiveCandidate candidate in result.candidates)
        {
            if (!candidate.canActivate
                || candidate.manifest.dependencies.Any(dependency => !activeIds.Contains(dependency)))
            {
                continue;
            }
            activeIds.Add(candidate.manifest.pluginId);
            candidates.Add(candidate);
        }
        return candidates;
    }

    private PluginArchiveCandidate ValidateAndExtract(
        string archivePath,
        IReadOnlySet<string> trustedPluginIds)
    {
        string contentHash = ComputeFileHash(archivePath);
        using ZipArchive archive = ZipFile.OpenRead(archivePath);
        ValidatedEntry[] entries = ValidateEntries(archive).ToArray();
        ValidatedEntry manifestEntry = entries.SingleOrDefault(static value => value.path == "Plugin.inno")
            ?? throw new InvalidDataException("A Plugin ZIP requires exactly one root Plugin.inno manifest.");
        PluginManifest manifest;
        using (Stream stream = manifestEntry.entry.Open())
        using (var memory = new MemoryStream())
        {
            stream.CopyTo(memory);
            manifest = SerializationManager.Deserialize<PluginManifest>(memory.ToArray());
        }
        manifest.Validate();
        ValidateContent(entries, manifest);

        string destinationRoot = Path.Combine(m_cacheRoot, manifest.pluginId, contentHash);
        string contentRoot = Path.Combine(destinationRoot, "Assets");
        if (!Directory.Exists(contentRoot))
            ExtractAtomically(archive, entries, destinationRoot);

        bool containsCode = entries.Any(static value =>
            value.path.StartsWith("Assets/", StringComparison.Ordinal)
            && string.Equals(Path.GetExtension(value.path), ".cs", StringComparison.OrdinalIgnoreCase));
        bool trusted = trustedPluginIds.Contains(manifest.pluginId);
        return new PluginArchiveCandidate(
            archivePath,
            contentHash,
            manifest,
            new AssetSourceMount(
                new AssetSourceId(manifest.pluginId),
                contentRoot,
                isReadOnly: true,
                manifest.dependencies.Select(static dependency => new AssetSourceId(dependency))),
            containsCode,
            trusted);
    }

    private IEnumerable<ValidatedEntry> ValidateEntries(ZipArchive archive)
    {
        if (archive.Entries.Count > m_limits.maximumEntryCount)
            throw new InvalidDataException("Plugin ZIP exceeds the configured entry-count limit.");

        var ordinalPaths = new HashSet<string>(StringComparer.Ordinal);
        var portablePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var decomposedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long totalBytes = 0;
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            string path = ValidateEntryPath(entry.FullName);
            if (!ordinalPaths.Add(path)
                || !portablePaths.Add(path)
                || !decomposedPaths.Add(path.Normalize(NormalizationForm.FormD)))
            {
                throw new InvalidDataException($"Plugin ZIP contains a duplicate or non-portable path '{path}'.");
            }
            if (IsSymbolicLink(entry))
                throw new InvalidDataException($"Plugin ZIP entry '{path}' is a symbolic link.");
            if (entry.Length > m_limits.maximumFileBytes)
                throw new InvalidDataException($"Plugin ZIP entry '{path}' exceeds the file-size limit.");
            totalBytes = checked(totalBytes + entry.Length);
            if (totalBytes > m_limits.maximumTotalBytes)
                throw new InvalidDataException("Plugin ZIP exceeds the total uncompressed-size limit.");
            if (entry.Length > 0)
            {
                double ratio = entry.CompressedLength == 0
                    ? double.PositiveInfinity
                    : (double)entry.Length / entry.CompressedLength;
                if (ratio > m_limits.maximumCompressionRatio)
                    throw new InvalidDataException($"Plugin ZIP entry '{path}' exceeds the compression-ratio limit.");
            }
            if (!path.EndsWith("/", StringComparison.Ordinal)
                && S_FORBIDDEN_BINARY_EXTENSIONS.Contains(Path.GetExtension(path)))
            {
                throw new InvalidDataException($"Plugin ZIP entry '{path}' is a forbidden prebuilt binary.");
            }
            yield return new ValidatedEntry(path, entry);
        }
    }

    private static void ValidateContent(IReadOnlyList<ValidatedEntry> entries, PluginManifest manifest)
    {
        _ = manifest;
        HashSet<string> paths = entries.Select(static value => value.path).ToHashSet(StringComparer.Ordinal);
        if (!paths.Any(static value => value.StartsWith("Assets/", StringComparison.Ordinal)))
            throw new InvalidDataException("Plugin ZIP contains no Assets content root.");

        foreach (string path in paths)
        {
            if (path is "Plugin.inno" or "Assets/" || path.EndsWith("/", StringComparison.Ordinal))
                continue;
            if (!path.StartsWith("Assets/", StringComparison.Ordinal))
                throw new InvalidDataException($"Unexpected root Plugin ZIP entry '{path}'.");
            if (path.EndsWith(".imeta", StringComparison.OrdinalIgnoreCase))
                continue;
            if (!paths.Contains(path + ".imeta"))
                throw new InvalidDataException($"Plugin source '{path}' is missing its required .imeta sidecar.");
        }

        foreach (string directory in paths.Where(static value =>
                     value.StartsWith("Assets/", StringComparison.Ordinal)
                     && value.EndsWith("/", StringComparison.Ordinal)
                     && value != "Assets/"))
        {
            string sidecar = directory.TrimEnd('/') + ".imeta";
            if (!paths.Contains(sidecar))
                throw new InvalidDataException($"Plugin directory '{directory}' is missing its required .imeta sidecar.");
        }
    }

    private static IReadOnlyList<PluginArchiveCandidate> ValidateDependencyGraph(
        IReadOnlyList<PluginArchiveCandidate> candidates,
        List<PluginArchiveDiagnostic> diagnostics)
    {
        var byId = new Dictionary<string, PluginArchiveCandidate>(StringComparer.Ordinal);
        var rejected = new HashSet<string>(StringComparer.Ordinal);
        foreach (PluginArchiveCandidate candidate in candidates)
        {
            if (!byId.TryAdd(candidate.manifest.pluginId, candidate))
            {
                rejected.Add(candidate.manifest.pluginId);
                diagnostics.Add(new PluginArchiveDiagnostic(
                    candidate.archivePath,
                    $"Plugin ID '{candidate.manifest.pluginId}' is installed more than once."));
            }
        }
        foreach (PluginArchiveCandidate candidate in candidates)
        {
            string? missing = candidate.manifest.dependencies.FirstOrDefault(id => !byId.ContainsKey(id));
            if (missing is not null)
            {
                rejected.Add(candidate.manifest.pluginId);
                diagnostics.Add(new PluginArchiveDiagnostic(
                    candidate.archivePath,
                    $"Plugin dependency '{missing}' is not installed."));
            }
        }

        var result = new List<PluginArchiveCandidate>();
        var remaining = byId.Values
            .Where(candidate => !rejected.Contains(candidate.manifest.pluginId))
            .ToDictionary(static candidate => candidate.manifest.pluginId, StringComparer.Ordinal);
        while (remaining.Count > 0)
        {
            PluginArchiveCandidate[] ready = remaining.Values
                .Where(candidate => candidate.manifest.dependencies.All(dependency =>
                    !rejected.Contains(dependency)
                    && result.Any(active => active.manifest.pluginId == dependency)))
                .OrderBy(static candidate => candidate.manifest.pluginId, StringComparer.Ordinal)
                .ToArray();
            if (ready.Length == 0)
            {
                foreach (PluginArchiveCandidate candidate in remaining.Values)
                {
                    rejected.Add(candidate.manifest.pluginId);
                    diagnostics.Add(new PluginArchiveDiagnostic(
                        candidate.archivePath,
                        $"Plugin dependency cycle includes '{candidate.manifest.pluginId}'."));
                }
                break;
            }
            foreach (PluginArchiveCandidate candidate in ready)
            {
                result.Add(candidate);
                remaining.Remove(candidate.manifest.pluginId);
            }
        }
        return result;
    }

    private static string ValidateEntryPath(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidDataException("Plugin ZIP contains an empty entry path.");
        if (value.Contains('\\') || value.StartsWith("/", StringComparison.Ordinal) || Path.IsPathRooted(value))
            throw new InvalidDataException($"Plugin ZIP entry '{value}' is not a portable relative path.");
        string path = value.Normalize(NormalizationForm.FormC);
        if (!string.Equals(path, value, StringComparison.Ordinal))
            throw new InvalidDataException($"Plugin ZIP entry '{value}' is not Unicode-normalized.");
        string[] segments = path.TrimEnd('/').Split('/');
        foreach (string segment in segments)
        {
            if (segment is "" or "." or ".."
                || segment.EndsWith(' ') || segment.EndsWith('.')
                || segment.IndexOfAny(['<', '>', ':', '"', '|', '?', '*']) >= 0
                || IsWindowsDeviceName(segment))
            {
                throw new InvalidDataException($"Plugin ZIP entry '{value}' has a non-portable segment.");
            }
        }
        return path;
    }

    private static bool IsWindowsDeviceName(string segment)
    {
        string name = Path.GetFileNameWithoutExtension(segment).ToUpperInvariant();
        return name is "CON" or "PRN" or "AUX" or "NUL"
            || name.Length == 4
            && (name.StartsWith("COM", StringComparison.Ordinal) || name.StartsWith("LPT", StringComparison.Ordinal))
            && name[3] is >= '1' and <= '9';
    }

    private static bool IsSymbolicLink(ZipArchiveEntry entry)
        => ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000;

    private void ExtractAtomically(
        ZipArchive archive,
        IReadOnlyList<ValidatedEntry> entries,
        string destinationRoot)
    {
        string stagingRoot = Path.Combine(m_cacheRoot, ".staging", Guid.NewGuid().ToString("N"));
        try
        {
            foreach (ValidatedEntry item in entries)
            {
                string target = ResolveContainedPath(stagingRoot, item.path.TrimEnd('/'));
                if (item.path.EndsWith("/", StringComparison.Ordinal))
                {
                    Directory.CreateDirectory(target);
                    continue;
                }
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                using Stream source = item.entry.Open();
                using FileStream output = new(target, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                source.CopyTo(output);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destinationRoot)!);
            if (!Directory.Exists(destinationRoot))
                Directory.Move(stagingRoot, destinationRoot);
        }
        finally
        {
            if (Directory.Exists(stagingRoot))
                Directory.Delete(stagingRoot, recursive: true);
        }
    }

    private static string ResolveContainedPath(string root, string relativePath)
    {
        string fullRoot = Path.GetFullPath(root);
        string result = Path.GetFullPath(Path.Combine(fullRoot, relativePath));
        string prefix = fullRoot.EndsWith(Path.DirectorySeparatorChar)
            ? fullRoot
            : fullRoot + Path.DirectorySeparatorChar;
        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (!result.StartsWith(prefix, comparison))
            throw new InvalidDataException("Plugin ZIP entry escaped the extraction root.");
        return result;
    }

    private static string ComputeFileHash(string path)
    {
        using FileStream stream = IOFile.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private sealed record ValidatedEntry(string path, ZipArchiveEntry entry);
}
