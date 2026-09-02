using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Inno.Assets;
using Inno.Assets.Pipeline;
using Inno.Plugins.Authoring;
using Inno.Plugins;
using Inno.Core.Serialization;
using Inno.Core.Settings;

namespace Inno.Build;

internal sealed class PluginPackageBuilder
{
    private static readonly DateTimeOffset S_ARCHIVE_TIME = new(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly HashSet<string> S_FORBIDDEN_EXTENSIONS = new(StringComparer.OrdinalIgnoreCase)
    {
        ".dll", ".exe", ".dylib", ".so", ".a", ".lib", ".pdb"
    };

    private readonly AssetPipeline m_assets;
    private readonly PluginEnvironment m_plugins;
    private readonly ProjectSettingsStore m_settings;
    private readonly SerializationRegistry m_serialization;

    internal PluginPackageBuilder(
        AssetPipeline assets,
        PluginEnvironment plugins,
        ProjectSettingsStore settings,
        SerializationRegistry serialization)
    {
        m_assets = assets;
        m_plugins = plugins;
        m_settings = settings;
        m_serialization = serialization;
    }

    internal async ValueTask<BuildResult> BuildAsync(
        PluginBuildRequest request,
        IProgress<BuildProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Validate();
        if (!m_assets.isInitialized)
            throw new InvalidOperationException("Plugin build requires an active authoring asset database.");
        if (m_plugins.activePlugins.Any(plugin =>
                string.Equals(plugin.manifest.pluginId, request.pluginId, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException($"Plugin ID '{request.pluginId}' is already active in this project.");
        }
        string destination = Path.GetFullPath(request.outputPath);
        ValidateDestination(destination);
        m_assets.WaitForIdle();
        PluginPackagePlan plan = CapturePlan(request);
        progress?.Report(new BuildProgress("snapshot", 0.15d, "Captured project and Plugin dependency sources."));
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        string temporary = destination + ".staging-" + Guid.NewGuid().ToString("N");
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
                await WriteBytesAsync(archive, "Plugin.inno", plan.manifest, cancellationToken)
                    .ConfigureAwait(false);
                int completed = 0;
                foreach (SourceEntry source in plan.projectEntries)
                {
                    await WriteSourceAsync(archive, source, cancellationToken).ConfigureAwait(false);
                    completed++;
                    progress?.Report(new BuildProgress(
                        "project-content",
                        0.15d + 0.5d * completed / Math.Max(1d, plan.projectEntries.Count),
                        $"Packaging project source {completed}/{plan.projectEntries.Count}."));
                }
                for (int index = 0; index < plan.dependencies.Count; index++)
                {
                    PluginDependencyPlan dependency = plan.dependencies[index];
                    ZipArchiveEntry packageEntry = archive.CreateEntry(
                        $"Dependencies/{dependency.pluginId}.zip",
                        CompressionLevel.Optimal);
                    packageEntry.LastWriteTime = S_ARCHIVE_TIME;
                    await using Stream packageStream = packageEntry.Open();
                    using var nested = new ZipArchive(packageStream, ZipArchiveMode.Create, leaveOpen: true);
                    await WriteBytesAsync(nested, "Plugin.inno", dependency.manifest, cancellationToken)
                        .ConfigureAwait(false);
                    foreach (SourceEntry source in dependency.entries)
                        await WriteSourceAsync(nested, source, cancellationToken).ConfigureAwait(false);
                    progress?.Report(new BuildProgress(
                        "dependencies",
                        0.7d + 0.2d * (index + 1) / Math.Max(1d, plan.dependencies.Count),
                        $"Embedded dependency {index + 1}/{plan.dependencies.Count}."));
                }
            }
            EnsurePlanUnchanged(plan);
            cancellationToken.ThrowIfCancellationRequested();
            string contentHash;
            await using (FileStream stream = new(
                             temporary,
                             FileMode.Open,
                             FileAccess.Read,
                             FileShare.Read,
                             128 * 1024,
                             FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                contentHash = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false));
            }
            BuildFileSystem.InstallFileAtomically(temporary, destination);
            progress?.Report(new BuildProgress("commit", 1d, "Plugin package committed atomically."));
            return BuildResult.Success(
                destination,
                target: null,
                contentHash,
                plan.assetCount,
                artifactBundleCount: 0,
                runtimeAssemblyCount: 0,
                plan.dependencies.Count);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    private PluginPackagePlan CapturePlan(PluginBuildRequest request)
    {
        AssetSourceMount projectMount = m_assets.sourceMounts.Single(
            static mount => mount.id == AssetSourceId.project);
        PluginCandidate[] activePlugins = m_plugins.activePlugins.ToArray();
        ValidateDependencyGraph(activePlugins);
        string[] dependencyIds = activePlugins.Select(static plugin => plugin.manifest.pluginId).ToArray();
        ProjectSettingsDocument settingsDocument = m_serialization.Deserialize<ProjectSettingsDocument>(
            m_settings.CaptureDocument());
        string[] overrides = activePlugins
            .Where(plugin => plugin.manifest.settingContributions.Any(contribution =>
                settingsDocument.overrides.Any(project => project.id == contribution.id)))
            .Select(static plugin => plugin.manifest.pluginId)
            .ToArray();
        HashSet<string> dependencySet = dependencyIds.ToHashSet(StringComparer.Ordinal);
        HashSet<string> overrideSet = overrides.ToHashSet(StringComparer.Ordinal);
        var settings = new List<ProjectSettingRecord>();
        foreach (ProjectSettingRecord record in settingsDocument.overrides
                     .OrderBy(static value => value.id.value, StringComparer.Ordinal))
        {
            if (m_settings.TryCapture(
                    record.id,
                    request.pluginId,
                    dependencySet,
                    overrideSet,
                    out ProjectSettingRecord captured))
            {
                settings.Add(captured);
            }
        }

        AssetFileEntry[] projectFiles = m_assets.GetFileSystemEntries(includeDirectories: false)
            .Where(static entry => entry.source == AssetSourceId.project)
            .OrderBy(static entry => entry.assetPath.localPath, StringComparer.Ordinal)
            .ToArray();
        if (projectFiles.Length == 0)
            throw new InvalidOperationException("The project contains no source content to export.");
        string[] relativeFiles = projectFiles.Select(static entry => entry.assetPath.localPath).ToArray();
        foreach (string relative in relativeFiles)
        {
            if (S_FORBIDDEN_EXTENSIONS.Contains(Path.GetExtension(relative)))
                throw new InvalidOperationException($"Plugin content '{relative}' is a forbidden binary.");
        }
        var manifest = new PluginManifest
        {
            pluginId = request.pluginId,
            displayName = request.displayName,
            dependencies = dependencyIds,
            overrides = overrides,
            contentRoots = ["Assets"],
            assemblyDefinitions = relativeFiles
                .Where(static path => path.EndsWith(".iasmdef", StringComparison.OrdinalIgnoreCase))
                .Select(static path => "Assets/" + path)
                .ToArray(),
            settingContributions = settings.ToArray()
        };
        manifest.Validate();
        var projectEntries = new List<SourceEntry>();
        foreach (string directory in CollectDirectories(relativeFiles))
        {
            string metadata = Path.Combine(projectMount.rootPath, directory.Replace('/', Path.DirectorySeparatorChar)) + ".imeta";
            projectEntries.Add(SourceEntry.Capture(metadata, $"Assets/{directory}.imeta"));
        }
        foreach (string relative in relativeFiles)
        {
            string source = Path.Combine(projectMount.rootPath, relative.Replace('/', Path.DirectorySeparatorChar));
            projectEntries.Add(SourceEntry.Capture(source, "Assets/" + relative));
            projectEntries.Add(SourceEntry.Capture(source + ".imeta", "Assets/" + relative + ".imeta"));
        }
        projectEntries.Sort(static (left, right) => StringComparer.Ordinal.Compare(left.archivePath, right.archivePath));

        var dependencies = new List<PluginDependencyPlan>();
        if (request.includeDependencies)
        {
            foreach (PluginCandidate plugin in activePlugins)
            {
                SourceEntry[] entries = Directory.EnumerateFiles(
                        plugin.sourceMount.rootPath,
                        "*",
                        SearchOption.AllDirectories)
                    .Select(path => SourceEntry.Capture(
                        path,
                        "Assets/" + Path.GetRelativePath(plugin.sourceMount.rootPath, path).Replace('\\', '/')))
                    .OrderBy(static value => value.archivePath, StringComparer.Ordinal)
                    .ToArray();
                dependencies.Add(new PluginDependencyPlan(
                    plugin.manifest.pluginId,
                    plugin.contentHash,
                    m_serialization.Serialize(plugin.manifest),
                    entries));
            }
        }
        return new PluginPackagePlan(
            m_serialization.Serialize(manifest),
            projectEntries,
            dependencies,
            projectFiles.Length,
            activePlugins.Select(static plugin => (plugin.manifest.pluginId, plugin.contentHash)).ToArray());
    }

    private static async ValueTask WriteSourceAsync(
        ZipArchive archive,
        SourceEntry source,
        CancellationToken cancellationToken)
    {
        source.EnsureUnchanged();
        ZipArchiveEntry entry = archive.CreateEntry(source.archivePath, CompressionLevel.Optimal);
        entry.LastWriteTime = S_ARCHIVE_TIME;
        await using Stream output = entry.Open();
        await using FileStream input = new(
            source.sourcePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            128 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
        source.EnsureUnchanged();
    }

    private static async ValueTask WriteBytesAsync(
        ZipArchive archive,
        string path,
        ReadOnlyMemory<byte> bytes,
        CancellationToken cancellationToken)
    {
        ZipArchiveEntry entry = archive.CreateEntry(path, CompressionLevel.Optimal);
        entry.LastWriteTime = S_ARCHIVE_TIME;
        await using Stream output = entry.Open();
        await output.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
    }

    private void EnsurePlanUnchanged(PluginPackagePlan plan)
    {
        foreach (SourceEntry source in plan.projectEntries)
            source.EnsureUnchanged();
        foreach (PluginDependencyPlan dependency in plan.dependencies)
        {
            foreach (SourceEntry source in dependency.entries)
                source.EnsureUnchanged();
        }
        (string pluginId, string contentHash)[] current = m_plugins.activePlugins
            .Select(static plugin => (plugin.manifest.pluginId, plugin.contentHash))
            .ToArray();
        if (!plan.activePlugins.SequenceEqual(current))
            throw new InvalidOperationException("The active Plugin generation changed during package creation.");
    }

    private static void ValidateDependencyGraph(IReadOnlyList<PluginCandidate> plugins)
    {
        Dictionary<string, PluginCandidate> byId = plugins.ToDictionary(
            static value => value.manifest.pluginId,
            StringComparer.Ordinal);
        var completed = new HashSet<string>(StringComparer.Ordinal);
        var visiting = new HashSet<string>(StringComparer.Ordinal);
        foreach (PluginCandidate plugin in plugins)
            Visit(plugin.manifest.pluginId);

        void Visit(string id)
        {
            if (completed.Contains(id))
                return;
            if (!visiting.Add(id))
                throw new InvalidOperationException($"Plugin dependency graph contains a cycle at '{id}'.");
            PluginCandidate plugin = byId[id];
            foreach (string dependency in plugin.manifest.dependencies)
            {
                if (!byId.ContainsKey(dependency))
                    throw new InvalidOperationException($"Plugin '{id}' requires unavailable dependency '{dependency}'.");
                Visit(dependency);
            }
            visiting.Remove(id);
            completed.Add(id);
        }
    }

    private static IEnumerable<string> CollectDirectories(IEnumerable<string> files)
    {
        var result = new SortedSet<string>(StringComparer.Ordinal);
        foreach (string file in files)
        {
            string? directory = Path.GetDirectoryName(file)?.Replace('\\', '/');
            while (!string.IsNullOrEmpty(directory))
            {
                result.Add(directory);
                directory = Path.GetDirectoryName(directory)?.Replace('\\', '/');
            }
        }
        return result;
    }

    private void ValidateDestination(string destination)
    {
        var protectedRoots = m_assets.sourceMounts
            .Select(static mount => mount.rootPath)
            .Append(m_assets.libraryRoot)
            .ToList();
        string? projectRoot = Directory.GetParent(m_assets.assetRoot)?.FullName;
        if (projectRoot is not null)
            protectedRoots.Add(Path.Combine(projectRoot, "Plugins"));
        string? conflict = protectedRoots.FirstOrDefault(root => IsWithin(root, destination));
        if (conflict is not null)
            throw new InvalidOperationException($"Plugin output cannot be written inside '{Path.GetFullPath(conflict)}'.");
    }

    private static bool IsWithin(string root, string candidate)
    {
        string normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return string.Equals(candidate, normalizedRoot, comparison)
               || candidate.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, comparison);
    }

    private sealed record PluginPackagePlan(
        byte[] manifest,
        IReadOnlyList<SourceEntry> projectEntries,
        IReadOnlyList<PluginDependencyPlan> dependencies,
        int assetCount,
        IReadOnlyList<(string pluginId, string contentHash)> activePlugins);

    private sealed record PluginDependencyPlan(
        string pluginId,
        string contentHash,
        byte[] manifest,
        IReadOnlyList<SourceEntry> entries);

    private sealed record SourceEntry(
        string sourcePath,
        string archivePath,
        long length,
        DateTime lastWriteTimeUtc)
    {
        internal static SourceEntry Capture(string sourcePath, string archivePath)
        {
            var info = new FileInfo(sourcePath);
            if (!info.Exists)
                throw new InvalidOperationException($"Plugin source dependency '{sourcePath}' is missing.");
            return new SourceEntry(
                info.FullName,
                archivePath.Replace('\\', '/'),
                info.Length,
                info.LastWriteTimeUtc);
        }

        internal void EnsureUnchanged()
        {
            var info = new FileInfo(sourcePath);
            if (!info.Exists || info.Length != length || info.LastWriteTimeUtc != lastWriteTimeUtc)
                throw new InvalidOperationException($"Plugin source '{sourcePath}' changed during package creation.");
        }
    }
}
