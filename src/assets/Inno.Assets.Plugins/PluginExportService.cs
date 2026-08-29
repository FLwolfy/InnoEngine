using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using Inno.Assets.Core;
using Inno.Assets.File;
using Inno.Core.Settings;
using Inno.Core.Serialization;

using IOFile = System.IO.File;

namespace Inno.Assets.Plugins;

/// <summary>Exports project assets and their complete local dependency closure as a deterministic ZIP Plugin.</summary>
public static class PluginExportService
{
    private static readonly DateTimeOffset S_ARCHIVE_TIME = new(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly HashSet<string> S_FORBIDDEN_EXTENSIONS = new(StringComparer.OrdinalIgnoreCase)
    {
        ".dll", ".exe", ".dylib", ".so", ".a", ".lib", ".pdb"
    };

    /// <summary>Exports one definition to a deterministic ZIP.</summary>
    /// <param name="definition">Project-owned Plugin definition.</param>
    /// <param name="outputPath">Destination ZIP path.</param>
    /// <returns>Lowercase SHA-256 content hash of the committed ZIP.</returns>
    /// <exception cref="InvalidOperationException">Thrown when content is missing or crosses an undeclared boundary.</exception>
    public static string Export(PluginDefinitionAsset definition, string outputPath)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        if (!AssetManager.isInitialized)
            throw new InvalidOperationException("Plugin export requires the common AssetManager.");
        ValidateDefinition(definition);
        AssetSourceMount projectMount = AssetManager.sourceMounts.Single(
            static mount => mount.id == AssetSourceId.project);
        HashSet<string> included = CollectContent(definition);
        if (included.Count == 0)
            throw new InvalidOperationException("A Plugin definition selected no project assets.");

        IReadOnlySet<string> dependencies = (definition.dependencies ?? [])
            .ToHashSet(StringComparer.Ordinal);
        IReadOnlySet<string> overrides = (definition.overrides ?? [])
            .ToHashSet(StringComparer.Ordinal);
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

        string destination = Path.GetFullPath(outputPath);
        if (!string.Equals(Path.GetExtension(destination), ".zip", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Plugin export destination must use the .zip extension.", nameof(outputPath));
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        string temporary = destination + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            using (FileStream stream = IOFile.Create(temporary))
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: false))
            {
                WriteEntry(archive, "Plugin.inno", SerializationManager.Serialize(manifest));
                WriteDirectoryEntry(archive, "Assets/");
                foreach (string directory in CollectDirectories(included))
                {
                    WriteDirectoryEntry(archive, $"Assets/{directory}/");
                    WritePhysicalEntry(
                        archive,
                        $"Assets/{directory}.imeta",
                        Path.Combine(projectMount.rootPath, directory.Replace('/', Path.DirectorySeparatorChar)) + ".imeta");
                }
                foreach (string path in included.Order(StringComparer.Ordinal))
                {
                    string physical = Path.Combine(
                        projectMount.rootPath,
                        path.Replace('/', Path.DirectorySeparatorChar));
                    WritePhysicalEntry(archive, "Assets/" + path, physical);
                    WritePhysicalEntry(archive, "Assets/" + path + ".imeta", physical + ".imeta");
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
        return Convert.ToHexString(SHA256.HashData(IOFile.ReadAllBytes(destination))).ToLowerInvariant();
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
            {
                AddProjectPath(included, entry.assetPath.localPath);
            }
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
                int before = included.Count;
                AddDependencyPath(definition, included, AssetPath.Parse(dependency.lastKnownPath));
                if (included.Count > before)
                    pending.Enqueue(AssetPath.Parse(dependency.lastKnownPath).localPath);
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

    private static void AddDependencyPath(
        PluginDefinitionAsset definition,
        ISet<string> included,
        AssetPath path)
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

    private static void WriteDirectoryEntry(ZipArchive archive, string path)
    {
        ZipArchiveEntry entry = archive.CreateEntry(path, CompressionLevel.NoCompression);
        entry.LastWriteTime = S_ARCHIVE_TIME;
    }

    private static void WritePhysicalEntry(ZipArchive archive, string path, string physicalPath)
    {
        if (!IOFile.Exists(physicalPath))
            throw new InvalidOperationException($"Plugin source dependency '{physicalPath}' is missing.");
        WriteEntry(archive, path, IOFile.ReadAllBytes(physicalPath));
    }

    private static void WriteEntry(ZipArchive archive, string path, ReadOnlySpan<byte> bytes)
    {
        ZipArchiveEntry entry = archive.CreateEntry(path, CompressionLevel.Optimal);
        entry.LastWriteTime = S_ARCHIVE_TIME;
        using Stream target = entry.Open();
        target.Write(bytes);
    }
}
