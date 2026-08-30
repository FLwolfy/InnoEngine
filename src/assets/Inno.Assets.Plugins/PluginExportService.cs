using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Inno.Assets.Core;
using Inno.Assets.File;
using Inno.Core.Serialization;
using Inno.Core.Settings;

using IOFile = System.IO.File;

namespace Inno.Assets.Plugins;

/// <summary>Exports project assets and their complete local dependency closure as an installable Plugin source.</summary>
public static class PluginExportService
{
    private static readonly DateTimeOffset S_ARCHIVE_TIME = new(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly HashSet<string> S_FORBIDDEN_EXTENSIONS = new(StringComparer.OrdinalIgnoreCase)
    {
        ".dll", ".exe", ".dylib", ".so", ".a", ".lib", ".pdb"
    };

    /// <summary>Exports one definition to a deterministic source ZIP.</summary>
    /// <param name="definition">Project-owned Plugin definition.</param>
    /// <param name="outputPath">Destination path with a <c>.zip</c> extension.</param>
    /// <returns>Uppercase SHA-256 hash of the normalized logical Plugin content.</returns>
    /// <exception cref="InvalidOperationException">Thrown when content is missing or crosses an undeclared boundary.</exception>
    public static string ExportZip(PluginDefinitionAsset definition, string outputPath)
    {
        ExportPlan plan = BuildPlan(definition);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        string destination = Path.GetFullPath(outputPath);
        if (!string.Equals(Path.GetExtension(destination), ".zip", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Plugin ZIP destination must use the .zip extension.", nameof(outputPath));
        string siblingDirectory = Path.ChangeExtension(destination, extension: null);
        if (Directory.Exists(siblingDirectory))
        {
            throw new InvalidOperationException(
                $"Plugin source '{siblingDirectory}' already exists as a directory. Remove or rename it before exporting the ZIP.");
        }
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        string temporary = destination + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            using (FileStream stream = IOFile.Create(temporary))
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: false))
            {
                foreach (ExportEntry entry in plan.entries)
                {
                    if (entry.isDirectory)
                        WriteDirectoryEntry(archive, entry.path);
                    else
                        WriteEntry(archive, entry.path, entry.bytes);
                }
            }
            IOFile.Move(temporary, destination, overwrite: true);
        }
        catch
        {
            if (IOFile.Exists(temporary))
                IOFile.Delete(temporary);
            throw;
        }
        return plan.contentHash;
    }

    /// <summary>Exports one definition to an unpacked, directly editable Plugin directory.</summary>
    /// <param name="definition">Project-owned Plugin definition.</param>
    /// <param name="outputDirectory">Destination directory placed under a project's <c>Plugins</c> root.</param>
    /// <returns>Uppercase SHA-256 hash of the normalized logical Plugin content.</returns>
    /// <exception cref="InvalidOperationException">Thrown when content is missing or crosses an undeclared boundary.</exception>
    public static string ExportDirectory(PluginDefinitionAsset definition, string outputDirectory)
    {
        ExportPlan plan = BuildPlan(definition);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        string destination = Path.GetFullPath(outputDirectory);
        if (IOFile.Exists(destination))
            throw new IOException("Plugin directory destination is occupied by a file.");
        string siblingZip = destination + ".zip";
        if (IOFile.Exists(siblingZip))
        {
            throw new InvalidOperationException(
                $"Plugin source '{siblingZip}' already exists as a ZIP. Remove or rename it before exporting the directory.");
        }

        string parent = Path.GetDirectoryName(destination)
            ?? throw new ArgumentException("Plugin directory destination requires a parent directory.", nameof(outputDirectory));
        Directory.CreateDirectory(parent);
        string name = Path.GetFileName(destination);
        string staging = Path.Combine(parent, "." + name + ".tmp-" + Guid.NewGuid().ToString("N"));
        string backup = Path.Combine(parent, "." + name + ".backup-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(staging);
            foreach (ExportEntry entry in plan.entries)
            {
                string relative = entry.path.TrimEnd('/').Replace('/', Path.DirectorySeparatorChar);
                string target = Path.Combine(staging, relative);
                if (entry.isDirectory)
                {
                    Directory.CreateDirectory(target);
                    continue;
                }
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                IOFile.WriteAllBytes(target, entry.bytes);
            }

            bool replaced = Directory.Exists(destination);
            if (replaced)
                Directory.Move(destination, backup);
            try
            {
                Directory.Move(staging, destination);
            }
            catch
            {
                if (replaced && Directory.Exists(backup) && !Directory.Exists(destination))
                    Directory.Move(backup, destination);
                throw;
            }
            if (Directory.Exists(backup))
                Directory.Delete(backup, recursive: true);
        }
        finally
        {
            if (Directory.Exists(staging))
                Directory.Delete(staging, recursive: true);
            if (Directory.Exists(backup) && !Directory.Exists(destination))
                Directory.Move(backup, destination);
        }
        return plan.contentHash;
    }

    /// <summary>Validates one definition without touching project content.</summary>
    /// <param name="definition">Definition to validate.</param>
    /// <exception cref="InvalidDataException">Thrown when stable IDs or selections are invalid.</exception>
    public static void ValidateDefinition(PluginDefinitionAsset definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var manifest = new PluginManifest
        {
            pluginId = definition.pluginId,
            displayName = definition.displayName,
            dependencies = definition.dependencies ?? [],
            overrides = definition.overrides ?? [],
            contentRoots = ["Assets"]
        };
        manifest.Validate();
        if ((definition.assetRoots ?? []).Any(string.IsNullOrWhiteSpace))
            throw new InvalidDataException("Plugin asset roots cannot contain empty paths.");
        foreach (string root in definition.assetRoots ?? [])
            _ = AssetPath.Project(root);
        if ((definition.settingIds ?? []).Any(static id => !id.isValid))
            throw new InvalidDataException("Plugin setting IDs must be valid.");
        if ((definition.settingIds ?? []).Distinct().Count() != (definition.settingIds ?? []).Length)
            throw new InvalidDataException("Plugin setting IDs must be unique.");
    }

    private static ExportPlan BuildPlan(PluginDefinitionAsset definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (!AssetManager.isInitialized)
            throw new InvalidOperationException("Plugin export requires the common AssetManager.");
        ValidateDefinition(definition);
        AssetSourceMount projectMount = AssetManager.sourceMounts.Single(
            static mount => mount.id == AssetSourceId.project);
        HashSet<string> included = CollectContent(definition);
        if (included.Count == 0)
            throw new InvalidOperationException("A Plugin definition selected no project assets.");

        IReadOnlySet<string> dependencies = (definition.dependencies ?? []).ToHashSet(StringComparer.Ordinal);
        IReadOnlySet<string> overrides = (definition.overrides ?? []).ToHashSet(StringComparer.Ordinal);
        var settings = new List<ProjectSettingRecord>();
        foreach (ProjectSettingId id in (definition.settingIds ?? [])
                     .OrderBy(static value => value.value, StringComparer.Ordinal))
        {
            if (ProjectSettingsManager.TryCapture(
                    id,
                    definition.pluginId,
                    dependencies,
                    overrides,
                    out ProjectSettingRecord record))
                settings.Add(record);
            else if (!ProjectSettingsManager.TryClone(id, out _))
                throw new InvalidOperationException($"Project setting '{id}' is not defined.");
        }

        var manifest = new PluginManifest
        {
            pluginId = definition.pluginId,
            displayName = definition.displayName,
            dependencies = dependencies.Order(StringComparer.Ordinal).ToArray(),
            overrides = overrides.Order(StringComparer.Ordinal).ToArray(),
            contentRoots = ["Assets"],
            assemblyDefinitions = included
                .Where(static path => path.EndsWith(".iasmdef", StringComparison.OrdinalIgnoreCase))
                .Select(static path => "Assets/" + path)
                .Order(StringComparer.Ordinal)
                .ToArray(),
            settingContributions = settings.ToArray()
        };
        manifest.Validate();

        var entries = new List<ExportEntry>
        {
            new("Plugin.inno", SerializationManager.Serialize(manifest), false),
            new("Assets/", [], true)
        };
        foreach (string directory in CollectDirectories(included))
        {
            string physicalDirectory = Path.Combine(
                projectMount.rootPath,
                directory.Replace('/', Path.DirectorySeparatorChar));
            entries.Add(new ExportEntry($"Assets/{directory}.imeta", ReadPhysical(physicalDirectory + ".imeta"), false));
            entries.Add(new ExportEntry($"Assets/{directory}/", [], true));
        }
        foreach (string path in included.Order(StringComparer.Ordinal))
        {
            string physical = Path.Combine(projectMount.rootPath, path.Replace('/', Path.DirectorySeparatorChar));
            entries.Add(new ExportEntry("Assets/" + path, ReadPhysical(physical), false));
            entries.Add(new ExportEntry("Assets/" + path + ".imeta", ReadPhysical(physical + ".imeta"), false));
        }
        ExportEntry[] ordered = entries.OrderBy(static entry => entry.path, StringComparer.Ordinal).ToArray();
        return new ExportPlan(ordered, ComputeContentHash(ordered));
    }

    private static HashSet<string> CollectContent(PluginDefinitionAsset definition)
    {
        var included = new HashSet<string>(StringComparer.Ordinal);
        IReadOnlyList<AssetFileEntry> entries = AssetManager.GetFileSystemEntries(includeDirectories: false);
        foreach (string rootValue in definition.assetRoots ?? [])
        {
            string root = new AssetPath(AssetSourceId.project, rootValue).localPath;
            foreach (AssetFileEntry entry in entries.Where(entry =>
                         entry.source == AssetSourceId.project
                         && IsWithin(entry.assetPath.localPath, root)))
                AddProjectPath(included, entry.assetPath.localPath);
        }
        foreach (AssetObject asset in definition.assets ?? [])
        {
            if (asset is null)
                throw new InvalidDataException("Plugin explicit assets cannot contain null.");
            AddDependencyPath(definition, included, asset.assetPath);
        }

        var pending = new Queue<string>(included.Order(StringComparer.Ordinal));
        var visited = new HashSet<string>(StringComparer.Ordinal);
        while (pending.Count > 0)
        {
            string path = pending.Dequeue();
            if (!visited.Add(path)
                || !AssetManager.TryLoad<AssetObject>(AssetPath.Project(path), out AssetObject? asset)
                || asset is null)
                continue;
            foreach (AssetDependency dependency in AssetManager.GetDependencies(asset))
            {
                AssetPath dependencyPath = AssetPath.Parse(dependency.lastKnownPath);
                int before = included.Count;
                AddDependencyPath(definition, included, dependencyPath);
                if (included.Count > before && dependencyPath.source == AssetSourceId.project)
                    pending.Enqueue(dependencyPath.localPath);
            }
            foreach (AssetPath dependency in AssetManager.GetImportDependencies(asset))
            {
                int before = included.Count;
                AddDependencyPath(definition, included, dependency);
                if (included.Count > before && dependency.source == AssetSourceId.project)
                    pending.Enqueue(dependency.localPath);
            }
        }
        included.RemoveWhere(static path => path.EndsWith(".iplugin", StringComparison.OrdinalIgnoreCase));
        return included;
    }

    private static void AddDependencyPath(PluginDefinitionAsset definition, ISet<string> included, AssetPath path)
    {
        if (path.source == AssetSourceId.project)
        {
            AddProjectPath(included, path.localPath);
            return;
        }
        if (!(definition.dependencies ?? []).Contains(path.source.value, StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                $"Asset '{path}' belongs to undeclared Plugin dependency '{path.source}'.");
        }
    }

    private static void AddProjectPath(ISet<string> included, string path)
    {
        string normalized = new AssetPath(AssetSourceId.project, path).localPath;
        if (S_FORBIDDEN_EXTENSIONS.Contains(Path.GetExtension(normalized)))
            throw new InvalidOperationException($"Plugin content '{normalized}' is a forbidden binary.");
        included.Add(normalized);
    }

    private static IEnumerable<string> CollectDirectories(IEnumerable<string> files)
    {
        var directories = new SortedSet<string>(StringComparer.Ordinal);
        foreach (string file in files)
        {
            string? directory = Path.GetDirectoryName(file)?.Replace('\\', '/');
            while (!string.IsNullOrWhiteSpace(directory))
            {
                directories.Add(directory);
                directory = Path.GetDirectoryName(directory)?.Replace('\\', '/');
            }
        }
        return directories;
    }

    private static bool IsWithin(string path, string root)
        => string.IsNullOrEmpty(root)
            || string.Equals(path, root, StringComparison.Ordinal)
            || path.StartsWith(root + "/", StringComparison.Ordinal);

    private static byte[] ReadPhysical(string physicalPath)
    {
        if (!IOFile.Exists(physicalPath))
            throw new InvalidOperationException($"Plugin source dependency '{physicalPath}' is missing.");
        return IOFile.ReadAllBytes(physicalPath);
    }

    private static void WriteDirectoryEntry(ZipArchive archive, string path)
    {
        ZipArchiveEntry entry = archive.CreateEntry(path, CompressionLevel.NoCompression);
        entry.LastWriteTime = S_ARCHIVE_TIME;
    }

    private static void WriteEntry(ZipArchive archive, string path, ReadOnlySpan<byte> bytes)
    {
        ZipArchiveEntry entry = archive.CreateEntry(path, CompressionLevel.Optimal);
        entry.LastWriteTime = S_ARCHIVE_TIME;
        using Stream target = entry.Open();
        target.Write(bytes);
    }

    private static string ComputeContentHash(IEnumerable<ExportEntry> entries)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] lengthBytes = new byte[sizeof(int)];
        foreach (ExportEntry entry in entries.OrderBy(static value => value.path, StringComparer.Ordinal))
        {
            byte[] pathBytes = Encoding.UTF8.GetBytes(entry.path);
            BinaryPrimitives.WriteInt32LittleEndian(lengthBytes, pathBytes.Length);
            hash.AppendData(lengthBytes);
            hash.AppendData(pathBytes);
            if (!entry.isDirectory)
                hash.AppendData(entry.bytes);
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private sealed record ExportPlan(IReadOnlyList<ExportEntry> entries, string contentHash);
    private sealed record ExportEntry(string path, byte[] bytes, bool isDirectory);
}
