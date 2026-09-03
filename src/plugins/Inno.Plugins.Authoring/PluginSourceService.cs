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

namespace Inno.Plugins.Authoring;

/// <summary>
/// Discovers, validates, mounts, and dependency-orders local <c>.iplugin</c> packages.
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
    /// Project Plugins directory containing <c>.iplugin</c> packages.
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
    /// Scans every installed <c>.iplugin</c> package into one isolated candidate snapshot.
    /// </summary>
    /// <returns>
    /// Dependency-ordered candidates plus isolated diagnostics.
    /// </returns>
    public PluginScanResult Scan()
    {
        var candidates = new List<PluginCandidate>();
        var diagnostics = new List<PluginDiagnostic>();
        foreach (string unsupported in DiscoverUnsupportedSources())
        {
            diagnostics.Add(new PluginDiagnostic(
                unsupported,
                "Plugin installations must be .iplugin package files; directories and other file extensions are not supported."));
        }
        foreach (string packagePath in DiscoverPackages())
        {
            try
            {
                candidates.AddRange(ValidatePackage(packagePath));
            }
            catch (Exception exception) when (
                exception is InvalidDataException or IOException or UnauthorizedAccessException)
            {
                diagnostics.Add(new PluginDiagnostic(packagePath, exception.Message));
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

    private IEnumerable<string> DiscoverPackages()
    {
        return Directory
            .EnumerateFiles(m_pluginRoot, "*", SearchOption.TopDirectoryOnly)
            .Where(static path => string.Equals(
                Path.GetExtension(path),
                ".iplugin",
                StringComparison.OrdinalIgnoreCase))
            .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase);
    }

    private IEnumerable<string> DiscoverUnsupportedSources()
    {
        return Directory
            .EnumerateFileSystemEntries(m_pluginRoot, "*", SearchOption.TopDirectoryOnly)
            .Where(path =>
            {
                string name = Path.GetFileName(path);
                if (name.StartsWith(".", StringComparison.Ordinal) || S_IGNORED_SYSTEM_FILE_NAMES.Contains(name))
                    return false;
                return Directory.Exists(path)
                    || !string.Equals(Path.GetExtension(path), ".iplugin", StringComparison.OrdinalIgnoreCase);
            })
            .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase);
    }

    private IReadOnlyList<PluginCandidate> ValidatePackage(string packagePath)
    {
        int embeddedCount = 0;
        return ValidatePackage(packagePath, PluginSourceKind.Package, depth: 0, ref embeddedCount);
    }

    private IReadOnlyList<PluginCandidate> ValidatePackage(
        string packagePath,
        PluginSourceKind sourceKind,
        int depth,
        ref int embeddedCount)
    {
        if (depth > m_limits.maximumEmbeddedDepth)
            throw new InvalidDataException("Plugin dependency packages exceed the configured nesting limit.");
        using ZipArchive archive = ZipFile.OpenRead(packagePath);
        ValidatedPackageEntry[] entries = ValidatePackageEntries(archive).ToArray();
        ValidatedPackageEntry manifestEntry = entries.SingleOrDefault(static value => value.path == "Plugin.inno")
            ?? throw new InvalidDataException("An .iplugin package requires exactly one root Plugin.inno manifest.");
        PluginManifest manifest = ReadManifest(manifestEntry.entry);
        manifest.Validate();
        ValidateContent(entries.Select(static entry => entry.path).ToArray());

        string contentHash = ComputePackageContentHash(entries);
        string destinationRoot = Path.Combine(m_cacheRoot, manifest.pluginId, contentHash);
        string contentRoot = Path.Combine(destinationRoot, "Assets");
        if (!Directory.Exists(contentRoot))
            ExtractAtomically(archive, entries, destinationRoot);

        bool containsCode = ContainsCode(entries.Select(static entry => entry.path));
        var candidates = new List<PluginCandidate>
        {
            CreateCandidate(
                packagePath,
                sourceKind,
                contentHash,
                manifest,
                contentRoot,
                containsCode)
        };
        foreach (ValidatedPackageEntry dependency in entries.Where(static entry =>
                     entry.path.StartsWith("Dependencies/", StringComparison.Ordinal)
                     && entry.path.EndsWith(".iplugin", StringComparison.OrdinalIgnoreCase)))
        {
            embeddedCount++;
            if (embeddedCount > m_limits.maximumEmbeddedPluginCount)
                throw new InvalidDataException("Plugin installation exceeds the embedded package-count limit.");
            string extractedPath = Path.Combine(
                destinationRoot,
                dependency.path.Replace('/', Path.DirectorySeparatorChar));
            IReadOnlyList<PluginCandidate> embedded = ValidatePackage(
                extractedPath,
                PluginSourceKind.EmbeddedPackage,
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

    private IEnumerable<ValidatedPackageEntry> ValidatePackageEntries(ZipArchive archive)
    {
        if (archive.Entries.Count > m_limits.maximumEntryCount)
            throw new InvalidDataException("The .iplugin package exceeds the configured entry-count limit.");

        var paths = new PortablePathSet(".iplugin package");
        long totalBytes = 0;
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            string path = ValidatePortablePath(entry.FullName);
            if (IsSymbolicLink(entry))
                throw new InvalidDataException($"Plugin package entry '{path}' is a symbolic link.");
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
                    throw new InvalidDataException($"Plugin package entry '{path}' exceeds the compression-ratio limit.");
            }
            ValidateSourceExtension(path);
            yield return new ValidatedPackageEntry(path, entry);
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

    private static void ValidateContent(IReadOnlyList<string> entries)
    {
        HashSet<string> paths = entries.ToHashSet(StringComparer.Ordinal);
        if (!paths.Any(static value => value.StartsWith("Assets/", StringComparison.Ordinal)))
        {
            throw new InvalidDataException("The .iplugin package contains no Assets content root.");
        }

        foreach (string path in paths)
        {
            if (path is "Plugin.inno" or "Assets/" or "Dependencies/"
                || path.EndsWith("/", StringComparison.Ordinal))
                continue;
            if (path.StartsWith("Dependencies/", StringComparison.Ordinal))
            {
                string local = path["Dependencies/".Length..];
                if (local.Contains('/')
                    || !local.EndsWith(".iplugin", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException(
                        $"Plugin dependency entry '{path}' must be one direct .iplugin package.");
                }
                continue;
            }
            if (!path.StartsWith("Assets/", StringComparison.Ordinal))
                throw new InvalidDataException($"Unexpected root .iplugin package entry '{path}'.");
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
        IReadOnlyList<ValidatedPackageEntry> entries,
        string destinationRoot)
    {
        string stagingRoot = Path.Combine(m_cacheRoot, ".staging", Guid.NewGuid().ToString("N"));
        try
        {
            foreach (ValidatedPackageEntry item in entries)
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
            throw new InvalidDataException("Plugin source entry escaped its generation snapshot root.");
        return result;
    }

    private static string ComputePackageContentHash(IReadOnlyList<ValidatedPackageEntry> entries)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (ValidatedPackageEntry item in entries.OrderBy(static entry => entry.path, StringComparer.Ordinal))
        {
            AppendPath(hash, item.path);
            if (item.path.EndsWith("/", StringComparison.Ordinal))
                continue;
            using Stream stream = item.entry.Open();
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

    private sealed record ValidatedPackageEntry(string path, ZipArchiveEntry entry);
}
