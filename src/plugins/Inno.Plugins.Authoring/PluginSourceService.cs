using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Inno.Assets;
using Inno.Plugins;
using Inno.Assets.Pipeline;
using Inno.Core.Serialization;
using Inno.Core.Storage;

using IOFile = System.IO.File;

namespace Inno.Plugins.Authoring;

/// <summary>
/// Discovers, validates, mounts, and dependency-orders local Plugin ZIPs and source directories.
/// </summary>
public sealed class PluginSourceService
{
    private static readonly HashSet<string> S_FORBIDDEN_BINARY_EXTENSIONS = new(StringComparer.OrdinalIgnoreCase)
    {
        ".dll", ".exe", ".dylib", ".so", ".a", ".lib"
    };
    private static readonly HashSet<string> S_IGNORED_SYSTEM_FILE_NAMES = new(StringComparer.OrdinalIgnoreCase)
    {
        ".DS_Store", "Thumbs.db", "desktop.ini"
    };

    private readonly string m_pluginRoot;
    private readonly string m_cacheRoot;
    private readonly PluginSourceLimits m_limits;
    private readonly SerializationRegistry m_serialization;

    /// <summary>
    /// Creates a local Plugin source service.
    /// </summary>
    /// <param name="serialization">
    /// The serialization registry used to read the current Plugin manifest contract.
    /// </param>
    /// <param name="pluginRoot">
    /// Project Plugins directory containing ZIPs and unpacked Plugin directories.
    /// </param>
    /// <param name="libraryRoot">
    /// Project rebuildable Library directory.
    /// </param>
    /// <param name="limits">
    /// Optional bounded source limits.
    /// </param>
    public PluginSourceService(
        SerializationRegistry serialization,
        string pluginRoot,
        string libraryRoot,
        PluginSourceLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(serialization);
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(libraryRoot);
        m_serialization = serialization;
        m_pluginRoot = Path.GetFullPath(pluginRoot);
        m_cacheRoot = Path.Combine(Path.GetFullPath(libraryRoot), "Plugins");
        m_limits = limits ?? PluginSourceLimits.defaults;
        Directory.CreateDirectory(m_pluginRoot);
        Directory.CreateDirectory(m_cacheRoot);
    }

    /// <summary>
    /// Scans every installed ZIP and directory into one isolated candidate snapshot.
    /// </summary>
    /// <returns>
    /// Dependency-ordered candidates plus isolated diagnostics.
    /// </returns>
    public PluginScanResult Scan()
    {
        var candidates = new List<PluginCandidate>();
        var diagnostics = new List<PluginDiagnostic>();
        foreach (PluginSource source in DiscoverSources())
        {
            try
            {
                candidates.AddRange(source.kind switch
                {
                    PluginSourceKind.Zip => ValidateZip(source.path),
                    PluginSourceKind.Directory => [ValidateDirectory(source.path)],
                    _ => throw new InvalidDataException("Unknown Plugin source kind.")
                });
            }
            catch (Exception exception) when (
                exception is InvalidDataException or IOException or UnauthorizedAccessException)
            {
                diagnostics.Add(new PluginDiagnostic(source.path, exception.Message));
            }
        }

        IReadOnlyList<PluginCandidate> ordered = ValidateDependencyGraph(candidates, diagnostics);
        return new PluginScanResult(ordered, diagnostics);
    }

    /// <summary>
    /// Gets read-only mounts that passed validation and dependency ordering.
    /// </summary>
    /// <param name="result">
    /// Complete scan result.
    /// </param>
    /// <returns>
    /// Dependency-ordered mount snapshot suitable for the common AssetPipeline.
    /// </returns>
    public static IReadOnlyList<AssetSourceMount> GetActivatableMounts(PluginScanResult result)
        => GetActivatableCandidates(result).Select(static candidate => candidate.sourceMount).ToArray();

    /// <summary>
    /// Gets validated candidates in complete dependency order.
    /// </summary>
    /// <param name="result">
    /// Complete scan result.
    /// </param>
    /// <returns>
    /// Dependency-ordered candidates suitable for one activation transaction.
    /// </returns>
    public static IReadOnlyList<PluginCandidate> GetActivatableCandidates(PluginScanResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return result.candidates;
    }

    private IEnumerable<PluginSource> DiscoverSources()
    {
        IEnumerable<PluginSource> zipSources = Directory
            .EnumerateFiles(m_pluginRoot, "*.zip", SearchOption.TopDirectoryOnly)
            .Select(static path => new PluginSource(path, PluginSourceKind.Zip));
        IEnumerable<PluginSource> directorySources = Directory
            .EnumerateDirectories(m_pluginRoot, "*", SearchOption.TopDirectoryOnly)
            .Where(static path => !Path.GetFileName(path).StartsWith(".", StringComparison.Ordinal))
            .Select(static path => new PluginSource(path, PluginSourceKind.Directory));
        return zipSources.Concat(directorySources)
            .OrderBy(static source => source.path, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static source => source.kind);
    }

    private IReadOnlyList<PluginCandidate> ValidateZip(string zipPath)
    {
        int embeddedCount = 0;
        return ValidateZip(zipPath, PluginSourceKind.Zip, depth: 0, ref embeddedCount);
    }

    private IReadOnlyList<PluginCandidate> ValidateZip(
        string zipPath,
        PluginSourceKind sourceKind,
        int depth,
        ref int embeddedCount)
    {
        if (depth > m_limits.maximumEmbeddedDepth)
            throw new InvalidDataException("Plugin dependency packages exceed the configured nesting limit.");
        using ZipArchive archive = ZipFile.OpenRead(zipPath);
        ValidatedZipEntry[] entries = ValidateZipEntries(archive).ToArray();
        ValidatedZipEntry manifestEntry = entries.SingleOrDefault(static value => value.path == "Plugin.inno")
            ?? throw new InvalidDataException("A Plugin ZIP requires exactly one root Plugin.inno manifest.");
        PluginManifest manifest = ReadManifest(manifestEntry.entry);
        manifest.Validate();
        ValidateContent(entries.Select(static entry => entry.path).ToArray(), "ZIP", allowDependencies: true);

        string contentHash = ComputeZipContentHash(entries);
        string destinationRoot = Path.Combine(m_cacheRoot, manifest.pluginId, contentHash);
        string contentRoot = Path.Combine(destinationRoot, "Assets");
        if (!Directory.Exists(contentRoot))
            ExtractAtomically(archive, entries, destinationRoot);

        bool containsCode = ContainsCode(entries.Select(static entry => entry.path));
        var candidates = new List<PluginCandidate>
        {
            CreateCandidate(
                zipPath,
                sourceKind,
                contentHash,
                manifest,
                contentRoot,
                containsCode)
        };
        foreach (ValidatedZipEntry dependency in entries.Where(static entry =>
                     entry.path.StartsWith("Dependencies/", StringComparison.Ordinal)
                     && entry.path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)))
        {
            embeddedCount++;
            if (embeddedCount > m_limits.maximumEmbeddedPluginCount)
                throw new InvalidDataException("Plugin installation exceeds the embedded package-count limit.");
            string extractedPath = Path.Combine(
                destinationRoot,
                dependency.path.Replace('/', Path.DirectorySeparatorChar));
            IReadOnlyList<PluginCandidate> embedded = ValidateZip(
                extractedPath,
                PluginSourceKind.EmbeddedZip,
                depth + 1,
                ref embeddedCount);
            PluginCandidate direct = embedded[0];
            if (!manifest.dependencies.Contains(direct.manifest.pluginId, StringComparer.Ordinal))
            {
                throw new InvalidDataException(
                    $"Embedded Plugin '{direct.manifest.pluginId}' is not declared by '{manifest.pluginId}'.");
            }
            candidates.AddRange(embedded);
        }
        return candidates;
    }

    private PluginCandidate ValidateDirectory(string directoryPath)
    {
        FileAttributes rootAttributes = IOFile.GetAttributes(directoryPath);
        if ((rootAttributes & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("A Plugin directory cannot be a symbolic link or reparse point.");

        string stagingRoot = Path.Combine(m_cacheRoot, ".staging", Guid.NewGuid().ToString("N"));
        try
        {
            CopyDirectorySnapshot(directoryPath, stagingRoot);
            ValidatedDirectoryEntry[] entries = ValidateDirectoryEntries(stagingRoot).ToArray();
            ValidatedDirectoryEntry manifestEntry = entries.SingleOrDefault(
                static value => value.path == "Plugin.inno")
                ?? throw new InvalidDataException(
                    "A Plugin directory requires exactly one root Plugin.inno manifest.");
            PluginManifest manifest = ReadManifest(manifestEntry.physicalPath);
            manifest.Validate();
            string[] paths = entries.Select(static entry => entry.path).ToArray();
            ValidateContent(paths, "directory", allowDependencies: false);

            string contentHash = ComputeDirectoryContentHash(entries);
            string destinationRoot = Path.Combine(m_cacheRoot, manifest.pluginId, contentHash);
            CommitStagedDirectory(stagingRoot, destinationRoot);
            return CreateCandidate(
                directoryPath,
                PluginSourceKind.Directory,
                contentHash,
                manifest,
                Path.Combine(destinationRoot, "Assets"),
                ContainsCode(paths));
        }
        finally
        {
            if (Directory.Exists(stagingRoot))
                Directory.Delete(stagingRoot, recursive: true);
        }
    }

    private PluginCandidate CreateCandidate(
        string sourcePath,
        PluginSourceKind sourceKind,
        string contentHash,
        PluginManifest manifest,
        string contentRoot,
        bool containsCode)
        => new(
            Path.GetFullPath(sourcePath),
            sourceKind,
            contentHash,
            manifest,
            new AssetSourceMount(
                new AssetSourceId(manifest.pluginId),
                contentRoot,
                isReadOnly: true,
                manifest.dependencies.Select(static dependency => new AssetSourceId(dependency))),
            containsCode);

    private IEnumerable<ValidatedZipEntry> ValidateZipEntries(ZipArchive archive)
    {
        if (archive.Entries.Count > m_limits.maximumEntryCount)
            throw new InvalidDataException("Plugin ZIP exceeds the configured entry-count limit.");

        var paths = new PortablePathSet("ZIP");
        long totalBytes = 0;
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            string path = ValidatePortablePath(entry.FullName);
            if (IsSymbolicLink(entry))
                throw new InvalidDataException($"Plugin ZIP entry '{path}' is a symbolic link.");
            if (IsIgnoredSystemFile(path))
                continue;
            paths.Add(path);
            ValidateFileSize(path, entry.Length, ref totalBytes);
            if (entry.Length > 0)
            {
                double ratio = entry.CompressedLength == 0
                    ? double.PositiveInfinity
                    : (double)entry.Length / entry.CompressedLength;
                if (ratio > m_limits.maximumCompressionRatio)
                    throw new InvalidDataException($"Plugin ZIP entry '{path}' exceeds the compression-ratio limit.");
            }
            ValidateSourceExtension(path);
            yield return new ValidatedZipEntry(path, entry);
        }
    }

    private IEnumerable<ValidatedDirectoryEntry> ValidateDirectoryEntries(string directoryPath)
    {
        var paths = new PortablePathSet("directory");
        var pending = new Stack<string>();
        pending.Push(directoryPath);
        int entryCount = 0;
        long totalBytes = 0;
        while (pending.Count > 0)
        {
            string current = pending.Pop();
            foreach (string child in Directory.EnumerateFileSystemEntries(current)
                         .OrderBy(static value => value, StringComparer.OrdinalIgnoreCase))
            {
                FileAttributes attributes = IOFile.GetAttributes(child);
                string relative = Path.GetRelativePath(directoryPath, child).Replace('\\', '/');
                bool isDirectory = (attributes & FileAttributes.Directory) != 0;
                string path = ValidatePortablePath(isDirectory ? relative + "/" : relative);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException($"Plugin directory entry '{path}' is a symbolic link or reparse point.");
                if (!isDirectory && IsIgnoredSystemFile(path))
                    continue;
                entryCount++;
                if (entryCount > m_limits.maximumEntryCount)
                    throw new InvalidDataException("Plugin directory exceeds the configured entry-count limit.");
                paths.Add(path);
                if (isDirectory)
                {
                    pending.Push(child);
                    yield return new ValidatedDirectoryEntry(path, child);
                    continue;
                }

                long length = new FileInfo(child).Length;
                ValidateFileSize(path, length, ref totalBytes);
                ValidateSourceExtension(path);
                yield return new ValidatedDirectoryEntry(path, child);
            }
        }
    }

    private void ValidateFileSize(string path, long length, ref long totalBytes)
    {
        if (length > m_limits.maximumFileBytes)
            throw new InvalidDataException($"Plugin source '{path}' exceeds the file-size limit.");
        totalBytes = checked(totalBytes + length);
        if (totalBytes > m_limits.maximumTotalBytes)
            throw new InvalidDataException("Plugin source exceeds the total uncompressed-size limit.");
    }

    private static void ValidateSourceExtension(string path)
    {
        if (!path.EndsWith("/", StringComparison.Ordinal)
            && S_FORBIDDEN_BINARY_EXTENSIONS.Contains(Path.GetExtension(path)))
        {
            throw new InvalidDataException($"Plugin source '{path}' is a forbidden prebuilt binary.");
        }
    }

    private static void ValidateContent(
        IReadOnlyList<string> entries,
        string sourceDescription,
        bool allowDependencies)
    {
        HashSet<string> paths = entries.ToHashSet(StringComparer.Ordinal);
        if (!paths.Any(static value => value.StartsWith("Assets/", StringComparison.Ordinal)))
        {
            throw new InvalidDataException($"Plugin {sourceDescription} contains no Assets content root.");
        }

        foreach (string path in paths)
        {
            if (path is "Plugin.inno" or "Assets/" or "Dependencies/"
                || path.EndsWith("/", StringComparison.Ordinal))
                continue;
            if (path.StartsWith("Dependencies/", StringComparison.Ordinal))
            {
                if (!allowDependencies)
                {
                    throw new InvalidDataException(
                        "Embedded dependency packages are supported only by ZIP installations.");
                }
                string local = path["Dependencies/".Length..];
                if (local.Contains('/')
                    || !local.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException(
                        $"Plugin {sourceDescription} dependency entry '{path}' must be one direct ZIP.");
                }
                continue;
            }
            if (!path.StartsWith("Assets/", StringComparison.Ordinal))
                throw new InvalidDataException($"Unexpected root Plugin source entry '{path}'.");
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

    private static IReadOnlyList<PluginCandidate> ValidateDependencyGraph(
        IReadOnlyList<PluginCandidate> candidates,
        List<PluginDiagnostic> diagnostics)
    {
        var byId = new Dictionary<string, PluginCandidate>(StringComparer.Ordinal);
        var rejected = new HashSet<string>(StringComparer.Ordinal);
        foreach (IGrouping<string, PluginCandidate> group in candidates.GroupBy(
                     static candidate => candidate.manifest.pluginId,
                     StringComparer.Ordinal))
        {
            PluginCandidate[] installed = group.ToArray();
            if (installed.Length == 1)
            {
                byId.Add(group.Key, installed[0]);
                continue;
            }
            rejected.Add(group.Key);
            foreach (PluginCandidate duplicate in installed)
            {
                diagnostics.Add(new PluginDiagnostic(
                    duplicate.sourcePath,
                    $"Plugin ID '{group.Key}' is installed more than once."));
            }
        }
        foreach (PluginCandidate candidate in candidates.Where(candidate =>
                     !rejected.Contains(candidate.manifest.pluginId)))
        {
            string? missing = candidate.manifest.dependencies.FirstOrDefault(id => !byId.ContainsKey(id));
            if (missing is not null)
            {
                rejected.Add(candidate.manifest.pluginId);
                diagnostics.Add(new PluginDiagnostic(
                    candidate.sourcePath,
                    $"Plugin dependency '{missing}' is not installed."));
            }
        }

        bool rejectionChanged;
        do
        {
            rejectionChanged = false;
            foreach (PluginCandidate candidate in byId.Values)
            {
                if (rejected.Contains(candidate.manifest.pluginId))
                    continue;
                string? blocked = candidate.manifest.dependencies.FirstOrDefault(rejected.Contains);
                if (blocked is null)
                    continue;
                rejected.Add(candidate.manifest.pluginId);
                rejectionChanged = true;
                diagnostics.Add(new PluginDiagnostic(
                    candidate.sourcePath,
                    $"Plugin dependency '{blocked}' is unavailable."));
            }
        }
        while (rejectionChanged);

        var graph = new DependencyGraph<string>(StringComparer.Ordinal, StringComparer.Ordinal);
        foreach (PluginCandidate candidate in byId.Values.Where(candidate =>
                     !rejected.Contains(candidate.manifest.pluginId)))
        {
            graph.AddNode(candidate.manifest.pluginId);
            foreach (string dependency in candidate.manifest.dependencies)
                graph.AddDependency(candidate.manifest.pluginId, dependency);
        }
        string[] cyclic = graph.GetStronglyConnectedComponents()
            .Where(component => component.Count > 1 || graph.DependsOn(component[0], component[0]))
            .SelectMany(static component => component)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var cycleBlocked = new HashSet<string>(cyclic, StringComparer.Ordinal);
        foreach (string node in cyclic)
            cycleBlocked.UnionWith(graph.GetDependents(node, recursive: true));
        foreach (string pluginId in cycleBlocked.OrderBy(static value => value, StringComparer.Ordinal))
        {
            PluginCandidate candidate = byId[pluginId];
            diagnostics.Add(new PluginDiagnostic(
                candidate.sourcePath,
                $"Plugin dependency cycle prevents '{pluginId}' from loading."));
            graph.RemoveNode(pluginId);
        }
        return graph.TopologicalSort().Select(pluginId => byId[pluginId]).ToArray();
    }

    private PluginManifest ReadManifest(ZipArchiveEntry entry)
    {
        try
        {
            using Stream stream = entry.Open();
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            return m_serialization.Deserialize<PluginManifest>(memory.ToArray());
        }
        catch (Exception exception) when (IsManifestDataFailure(exception))
        {
            throw new InvalidDataException("Plugin.inno is malformed or incompatible with the current contract.", exception);
        }
    }

    private PluginManifest ReadManifest(string path)
    {
        try
        {
            return m_serialization.Deserialize<PluginManifest>(IOFile.ReadAllBytes(path));
        }
        catch (Exception exception) when (IsManifestDataFailure(exception))
        {
            throw new InvalidDataException("Plugin.inno is malformed or incompatible with the current contract.", exception);
        }
    }

    private static bool IsManifestDataFailure(Exception exception)
        => exception is InvalidDataException
            or InvalidOperationException
            or ArgumentException
            or NotSupportedException
            or OverflowException;

    private static bool ContainsCode(IEnumerable<string> paths)
        => paths.Any(static path =>
            path.StartsWith("Assets/", StringComparison.Ordinal)
            && string.Equals(Path.GetExtension(path), ".cs", StringComparison.OrdinalIgnoreCase));

    private static bool IsIgnoredSystemFile(string path)
    {
        if (path.EndsWith("/", StringComparison.Ordinal))
            return false;
        int separator = path.LastIndexOf('/');
        string fileName = separator < 0 ? path : path[(separator + 1)..];
        return S_IGNORED_SYSTEM_FILE_NAMES.Contains(fileName);
    }

    private static string ValidatePortablePath(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidDataException("Plugin source contains an empty entry path.");
        if (value.Contains('\\') || value.StartsWith("/", StringComparison.Ordinal) || Path.IsPathRooted(value))
            throw new InvalidDataException($"Plugin source entry '{value}' is not a portable relative path.");
        string path = value.Normalize(NormalizationForm.FormC);
        if (!string.Equals(path, value, StringComparison.Ordinal))
            throw new InvalidDataException($"Plugin source entry '{value}' is not Unicode-normalized.");
        string[] segments = path.TrimEnd('/').Split('/');
        foreach (string segment in segments)
        {
            if (segment is "" or "." or ".."
                || segment.EndsWith(' ') || segment.EndsWith('.')
                || segment.IndexOfAny(['<', '>', ':', '"', '|', '?', '*']) >= 0
                || IsWindowsDeviceName(segment))
            {
                throw new InvalidDataException($"Plugin source entry '{value}' has a non-portable segment.");
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
        IReadOnlyList<ValidatedZipEntry> entries,
        string destinationRoot)
    {
        string stagingRoot = Path.Combine(m_cacheRoot, ".staging", Guid.NewGuid().ToString("N"));
        try
        {
            foreach (ValidatedZipEntry item in entries)
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

    private void CopyDirectorySnapshot(string sourceRoot, string stagingRoot)
    {
        ValidatedDirectoryEntry[] entries = ValidateDirectoryEntries(sourceRoot).ToArray();
        Directory.CreateDirectory(stagingRoot);
        foreach (ValidatedDirectoryEntry entry in entries)
        {
            string target = ResolveContainedPath(stagingRoot, entry.path.TrimEnd('/'));
            if (entry.path.EndsWith("/", StringComparison.Ordinal))
            {
                Directory.CreateDirectory(target);
                continue;
            }

            FileAttributes attributes = IOFile.GetAttributes(entry.physicalPath);
            if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
            {
                throw new InvalidDataException(
                    $"Plugin directory entry '{entry.path}' changed kind while its generation snapshot was captured.");
            }
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            using FileStream input = new(
                entry.physicalPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read);
            using FileStream output = new(target, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            input.CopyTo(output);
        }
    }

    private static void CommitStagedDirectory(string stagingRoot, string destinationRoot)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destinationRoot)!);
        if (Directory.Exists(destinationRoot))
            return;
        try
        {
            Directory.Move(stagingRoot, destinationRoot);
        }
        catch (IOException) when (Directory.Exists(destinationRoot))
        {
            // Another complete scan committed the same content-addressed snapshot first.
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
            throw new InvalidDataException("Plugin source entry escaped its generation snapshot root.");
        return result;
    }

    private static string ComputeZipContentHash(IReadOnlyList<ValidatedZipEntry> entries)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (ValidatedZipEntry item in entries.OrderBy(static entry => entry.path, StringComparer.Ordinal))
        {
            AppendPath(hash, item.path);
            if (item.path.EndsWith("/", StringComparison.Ordinal))
                continue;
            using Stream stream = item.entry.Open();
            AppendStream(hash, stream);
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static string ComputeDirectoryContentHash(IReadOnlyList<ValidatedDirectoryEntry> entries)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (ValidatedDirectoryEntry item in entries.OrderBy(static entry => entry.path, StringComparer.Ordinal))
        {
            AppendPath(hash, item.path);
            if (item.path.EndsWith("/", StringComparison.Ordinal))
                continue;
            using FileStream stream = IOFile.OpenRead(item.physicalPath);
            AppendStream(hash, stream);
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static void AppendPath(IncrementalHash hash, string path)
    {
        byte[] pathBytes = Encoding.UTF8.GetBytes(path);
        Span<byte> lengthBytes = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(lengthBytes, pathBytes.Length);
        hash.AppendData(lengthBytes);
        hash.AppendData(pathBytes);
    }

    private static void AppendStream(IncrementalHash hash, Stream stream)
    {
        byte[] buffer = new byte[64 * 1024];
        int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
            hash.AppendData(buffer.AsSpan(0, read));
    }

    private sealed class PortablePathSet(string sourceDescription)
    {
        private readonly HashSet<string> m_ordinal = new(StringComparer.Ordinal);
        private readonly HashSet<string> m_portable = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> m_decomposed = new(StringComparer.OrdinalIgnoreCase);

        internal void Add(string path)
        {
            if (!m_ordinal.Add(path)
                || !m_portable.Add(path)
                || !m_decomposed.Add(path.Normalize(NormalizationForm.FormD)))
            {
                throw new InvalidDataException(
                    $"Plugin {sourceDescription} contains a duplicate or non-portable path '{path}'.");
            }
        }
    }

    private readonly record struct PluginSource(string path, PluginSourceKind kind);
    private sealed record ValidatedZipEntry(string path, ZipArchiveEntry entry);
    private sealed record ValidatedDirectoryEntry(string path, string physicalPath);
}
