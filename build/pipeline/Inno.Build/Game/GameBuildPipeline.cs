using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Inno.Assets;
using Inno.Assets.Pipeline;
using Inno.Plugins.Authoring;
using Inno.Core.Serialization;
using Inno.Core.Settings;
using Inno.Extensibility.Modules;
using Inno.Runtime;
using Inno.Scene;
using Inno.Scripting.Compiler;

namespace Inno.Build;

internal sealed class GameBuildPipeline
{
    private readonly AssetPipeline m_assets;
    private readonly PluginEnvironment m_plugins;
    private readonly ProjectSettingsStore m_settings;
    private readonly SerializationRegistry m_serialization;
    private readonly ScriptCompiler m_compiler;
    private readonly IReadOnlyDictionary<BuildTargetId, IGameBuildTarget> m_targets;
    private readonly PlayerSupportPackCatalog m_supportPacks;

    internal GameBuildPipeline(
        AssetPipeline assets,
        PluginEnvironment plugins,
        ProjectSettingsStore settings,
        SerializationRegistry serialization,
        ScriptCompiler compiler,
        IReadOnlyDictionary<BuildTargetId, IGameBuildTarget> targets,
        PlayerSupportPackCatalog supportPacks)
    {
        m_assets = assets;
        m_plugins = plugins;
        m_settings = settings;
        m_serialization = serialization;
        m_compiler = compiler;
        m_targets = targets;
        m_supportPacks = supportPacks;
    }

    internal async ValueTask<BuildResult> BuildAsync(
        GameBuildRequest request,
        IProgress<BuildProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Validate();
        if (!m_targets.TryGetValue(request.profile.target, out IGameBuildTarget? target))
            throw new InvalidOperationException($"No game packager is registered for '{request.profile.target}'.");
        if (!m_assets.isInitialized)
            throw new InvalidOperationException("Game build requires an active authoring asset database.");
        string outputRoot = Path.GetFullPath(request.outputDirectory);
        ValidateOutputRoot(outputRoot);
        string supportPack = m_supportPacks.Resolve(request.profile.target);
        m_assets.WaitForIdle();
        AssetPath startupPath = AssetPath.Parse(request.profile.startupScene);
        if (!m_assets.TryGetAssetType(startupPath, out Type? sceneType) || sceneType != typeof(SceneAsset))
            throw new InvalidOperationException($"Startup scene '{startupPath}' is not an imported Scene asset.");

        long assetRevision = m_assets.revision;
        long pluginRevision = m_plugins.revision;
        long settingsRevision = m_settings.revision;
        PluginCandidate[] plugins = m_plugins.activePlugins.ToArray();
        byte[] settings = m_settings.CaptureDocument();
        using SerializationGeneration serialization = m_serialization.CaptureGeneration();
        progress?.Report(new BuildProgress("snapshot", 0.05d, "Captured the combined authoring generation."));

        Directory.CreateDirectory(outputRoot);
        string staging = Path.Combine(outputRoot, ".inno-build-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        using var stagingCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task<AssetRuntimeContentInfo>? assetExportTask = null;
        Task? targetContentTask = null;
        try
        {
            CancellationToken stagingToken = stagingCancellation.Token;
            stagingToken.ThrowIfCancellationRequested();
            string rawContent = Path.Combine(staging, "RawContent");
            string targetContent = Path.Combine(staging, "TargetContent");
            progress?.Report(new BuildProgress("content", 0.12d, "Exporting the runtime artifact closure."));
            assetExportTask = m_assets.ExportRuntimeArtifactsAsync(
                rawContent,
                stagingToken);
            targetContentTask = target.BuildContentAsync(
                    new GameBuildContentContext(request.profile, targetContent, serialization),
                    stagingToken)
                .AsTask();

            ScriptCompilationResult compilation = await m_compiler.CompileRuntimeDeploymentAsync(
                    new ScriptBuildProgress(progress),
                    stagingToken)
                .ConfigureAwait(false);
            if (!compilation.success)
            {
                stagingCancellation.Cancel();
                BuildDiagnostic[] diagnostics = compilation.diagnostics.Select(ToBuildDiagnostic).ToArray();
                return BuildResult.Failure(request.profile.target, diagnostics);
            }

            string[] assemblies = compilation.runtimeAssemblyPaths
                .Select(Path.GetFullPath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (assemblies.Length == 0 || assemblies.Any(static path => !File.Exists(path)))
            {
                stagingCancellation.Cancel();
                return BuildResult.Failure(
                    request.profile.target,
                    [new BuildDiagnostic(
                        BuildDiagnosticSeverity.Error,
                        "INNOBUILD1001",
                        "Runtime script compilation did not produce a complete assembly generation.")]);
            }
            GameRuntimeModule[] runtimeModules = CreateRuntimeModules(compilation.activationRequests);
            byte[] runtimeManifest = RuntimeManifestEnvelope.Encode(
                CreateManifest(request.profile, plugins, runtimeModules),
                serialization);
            EnsureGenerationUnchanged(assetRevision, pluginRevision, settingsRevision);
            progress?.Report(new BuildProgress("prepare", 0.28d, "Prepared the runtime script and manifest generation."));

            AssetRuntimeContentInfo assetInfo = await assetExportTask.ConfigureAwait(false);
            await targetContentTask.ConfigureAwait(false);
            await BuildFileSystem.MergeDirectoryAsync(targetContent, rawContent, stagingToken)
                .ConfigureAwait(false);
            EnsureGenerationUnchanged(assetRevision, pluginRevision, settingsRevision);

            await File.WriteAllBytesAsync(
                    Path.Combine(rawContent, "ProjectSettings.inno"),
                    settings,
                    stagingToken)
                .ConfigureAwait(false);
            string managed = Path.Combine(rawContent, "Managed");
            Directory.CreateDirectory(managed);
            for (int index = 0; index < assemblies.Length; index++)
            {
                stagingToken.ThrowIfCancellationRequested();
                string destination = Path.Combine(managed, Path.GetFileName(assemblies[index]));
                if (File.Exists(destination))
                    throw new InvalidOperationException($"Runtime assembly '{Path.GetFileName(destination)}' is duplicated.");
                await BuildFileSystem.CopyFileAsync(assemblies[index], destination, stagingToken)
                    .ConfigureAwait(false);
            }

            string snapshotFingerprint = await BuildSnapshotFingerprint.ComputeAsync(
                    assetRevision,
                    assemblies,
                    plugins,
                    settings,
                    stagingToken)
                .ConfigureAwait(false);
            string packagedContent = Path.Combine(staging, "PackagedContent");
            Directory.CreateDirectory(packagedContent);
            await File.WriteAllBytesAsync(
                    Path.Combine(packagedContent, "runtime.manifest"),
                    runtimeManifest,
                    stagingToken)
                .ConfigureAwait(false);

            progress?.Report(new BuildProgress("pack", 0.55d, "Writing the deterministic content pack."));
            (_, string contentHash) = await ContentPackWriter.WriteAsync(
                    rawContent,
                    packagedContent,
                    stagingToken)
                .ConfigureAwait(false);
            var catalog = new RuntimeContentCatalog
            {
                contentHash = contentHash,
                packFileName = $"content-{contentHash}.pack",
                snapshotFingerprint = snapshotFingerprint,
                assetCount = assetInfo.assetCount,
                artifactBundleCount = assetInfo.artifactBundleCount,
                runtimeAssemblyCount = assemblies.Length
            };
            catalog.Validate();
            await File.WriteAllBytesAsync(
                    Path.Combine(packagedContent, "catalog.inno"),
                    serialization.Serialize(catalog),
                    stagingToken)
                .ConfigureAwait(false);
            EnsureGenerationUnchanged(assetRevision, pluginRevision, settingsRevision);

            string platformStaging = Path.Combine(staging, "Platform");
            Directory.CreateDirectory(platformStaging);
            progress?.Report(new BuildProgress("package", 0.78d, $"Composing {request.profile.target} Player output."));
            string composed = await target.PackageAsync(
                    new GameBuildPackageContext(
                        request.profile,
                        supportPack,
                        packagedContent,
                        platformStaging),
                    stagingToken)
                .ConfigureAwait(false);
            string normalizedComposed = Path.GetFullPath(composed);
            if (!IsWithin(platformStaging, normalizedComposed))
                throw new InvalidOperationException("A game target returned output outside its staging directory.");
            if (!Directory.Exists(normalizedComposed))
                throw new DirectoryNotFoundException("The game target did not create its declared output directory.");
            stagingToken.ThrowIfCancellationRequested();
            string final = Path.Combine(outputRoot, Path.GetFileName(normalizedComposed));
            BuildFileSystem.InstallDirectoryAtomically(normalizedComposed, final);
            progress?.Report(new BuildProgress("commit", 1d, "Game build committed atomically."));
            return BuildResult.Success(
                final,
                request.profile.target,
                contentHash,
                assetInfo.assetCount,
                assetInfo.artifactBundleCount,
                assemblies.Length,
                embeddedPluginCount: 0);
        }
        finally
        {
            stagingCancellation.Cancel();
            await ObserveCompletionAsync(assetExportTask).ConfigureAwait(false);
            await ObserveCompletionAsync(targetContentTask).ConfigureAwait(false);
            if (Directory.Exists(staging))
                Directory.Delete(staging, recursive: true);
        }
    }

    private static async ValueTask ObserveCompletionAsync(Task? task)
    {
        if (task is null)
            return;
        try
        {
            await task.ConfigureAwait(false);
        }
        catch
        {
            // The primary build path reports the stage failure; cleanup only observes task completion.
        }
    }

    private static BuildDiagnostic ToBuildDiagnostic(ScriptDiagnostic diagnostic)
    {
        BuildDiagnosticSeverity severity = diagnostic.severity switch
        {
            ScriptDiagnosticSeverity.Error => BuildDiagnosticSeverity.Error,
            ScriptDiagnosticSeverity.Warning => BuildDiagnosticSeverity.Warning,
            _ => BuildDiagnosticSeverity.Information
        };
        string location = diagnostic.filePath is null
            ? string.Empty
            : $" ({diagnostic.filePath}:{diagnostic.line}:{diagnostic.column})";
        return new BuildDiagnostic(severity, diagnostic.id, diagnostic.message + location);
    }

    private static GameRuntimeManifest CreateManifest(
        BuildProfile profile,
        IReadOnlyList<PluginCandidate> plugins,
        GameRuntimeModule[] modules)
    {
        var manifest = new GameRuntimeManifest
        {
            applicationId = profile.applicationId,
            productName = profile.productName,
            startupScene = AssetPath.Parse(profile.startupScene).ToString(),
            windowWidth = profile.windowWidth,
            windowHeight = profile.windowHeight,
            modules = modules,
            plugins = plugins.Select(static plugin => new GameRuntimePlugin
            {
                id = plugin.manifest.pluginId,
                dependencies = plugin.manifest.dependencies.ToArray(),
                overrides = plugin.manifest.overrides.ToArray(),
                settings = plugin.manifest.settingContributions.ToArray()
            }).ToArray()
        };
        manifest.Validate();
        return manifest;
    }

    private static GameRuntimeModule[] CreateRuntimeModules(
        IReadOnlyList<AssemblyLoadRequest> requests)
    {
        var selected = requests
            .Select(request => new
            {
                request,
                assemblies = new[] { request.mainAssemblyPath }
                    .Concat(request.preloadAssemblyPaths)
                    .Where(path => request.assemblyScopes.TryGetValue(
                            Path.GetFileNameWithoutExtension(path),
                            out AssemblyScope scope)
                        ? scope == AssemblyScope.Runtime
                        : request.scope == AssemblyScope.Runtime)
                    .ToArray()
            })
            .Where(static candidate => candidate.assemblies.Length > 0)
            .ToArray();
        var includedNames = selected
            .Select(static candidate => candidate.request.moduleName)
            .ToHashSet(StringComparer.Ordinal);
        GameRuntimeModule[] candidates = selected.Select(candidate => new GameRuntimeModule
        {
            name = candidate.request.moduleName,
            domain = candidate.request.domain,
            mainAssembly = Path.GetFileName(candidate.assemblies[0]),
            preloadAssemblies = candidate.assemblies.Skip(1).Select(Path.GetFileName).ToArray()!,
            dependencies = candidate.request.upstreamModuleNames
                .Where(includedNames.Contains)
                .ToArray()
        }).ToArray();
        var byName = candidates.ToDictionary(static module => module.name, StringComparer.Ordinal);
        var visiting = new HashSet<string>(StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var ordered = new List<GameRuntimeModule>(candidates.Length);
        foreach (GameRuntimeModule module in candidates.OrderBy(static module => module.name, StringComparer.Ordinal))
            Visit(module);
        return ordered.ToArray();

        void Visit(GameRuntimeModule module)
        {
            if (visited.Contains(module.name))
                return;
            if (!visiting.Add(module.name))
                throw new InvalidOperationException($"Runtime module dependency cycle contains '{module.name}'.");
            foreach (string dependencyName in module.dependencies.Order(StringComparer.Ordinal))
            {
                if (!byName.TryGetValue(dependencyName, out GameRuntimeModule? dependency))
                {
                    throw new InvalidOperationException(
                        $"Runtime module '{module.name}' depends on missing module '{dependencyName}'.");
                }
                Visit(dependency);
            }
            visiting.Remove(module.name);
            visited.Add(module.name);
            ordered.Add(module);
        }
    }

    private void EnsureGenerationUnchanged(
        long expectedAssetRevision,
        long expectedPluginRevision,
        long expectedSettingsRevision)
    {
        if (m_assets.revision != expectedAssetRevision
            || m_plugins.revision != expectedPluginRevision
            || m_settings.revision != expectedSettingsRevision)
        {
            throw new InvalidOperationException(
                "The active Asset, Plugin, or Project Settings generation changed during the build. " +
                "No output was committed; start a new build from the latest generation.");
        }
    }

    private void ValidateOutputRoot(string outputRoot)
    {
        var protectedRoots = m_assets.sourceMounts
            .Select(static mount => mount.rootPath)
            .Append(m_assets.libraryRoot)
            .ToList();
        string? projectRoot = Directory.GetParent(m_assets.assetRoot)?.FullName;
        if (projectRoot is not null)
            protectedRoots.Add(Path.Combine(projectRoot, "Plugins"));
        string? conflict = protectedRoots.FirstOrDefault(root => IsWithin(root, outputRoot));
        if (conflict is not null)
        {
            throw new InvalidOperationException(
                $"Build output cannot be written inside managed project content '{Path.GetFullPath(conflict)}'.");
        }
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

    private sealed class ScriptBuildProgress(IProgress<BuildProgress>? progress)
        : IProgress<ScriptCompilationProgress>
    {
        /// <summary>
        /// Publishes one progress update to the receiving workflow.
        /// </summary>
        /// <param name="value">
        /// The concrete value read or transformed by this operation.
        /// </param>
        public void Report(ScriptCompilationProgress value)
            => progress?.Report(new BuildProgress(
                "scripting",
                0.05d + value.fraction * 0.2d,
                value.stage));
    }
}
