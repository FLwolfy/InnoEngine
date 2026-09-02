using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Inno.Assets;
using Inno.Assets.Pipeline;
using Inno.Build.Platform.MacOS;
using Inno.Build.Platform.Windows;
using Inno.Core.Identity;
using Inno.Core.Serialization;
using Inno.Core.Settings;
using Inno.Plugins.Authoring;
using Inno.Runtime;
using Inno.Scene;
using Inno.Scripting.Compiler;

using Xunit;

namespace Inno.Build.Tests;

[Collection("Build pipeline serialization")]
public sealed class BuildPipelineTests : IDisposable
{
    private readonly AssetPipeline m_assets;
    private readonly EngineHost m_engine;
    private readonly IdentityAllocator m_identities = new();
    private readonly BuildPipeline m_pipeline;
    private readonly PluginEnvironment m_plugins;
    private readonly ScriptCompiler m_compiler;
    private readonly string m_projectRoot;
    private readonly string m_root;
    private readonly ProjectSettingsStore m_settings;
    private readonly SceneWorld m_sceneWorld;
    private readonly IDisposable m_sceneWorldScope;
    private readonly string m_supportPackRoot;

    public BuildPipelineTests()
    {
        m_root = Path.Combine(Path.GetTempPath(), "InnoBuildTests", Guid.NewGuid().ToString("N"));
        m_projectRoot = Path.Combine(m_root, "Project");
        string assetRoot = Path.Combine(m_projectRoot, "Assets");
        string pluginRoot = Path.Combine(m_projectRoot, "Plugins");
        string libraryRoot = Path.Combine(m_projectRoot, "Library");
        m_supportPackRoot = Path.Combine(m_root, "SupportPacks");
        Directory.CreateDirectory(assetRoot);
        Directory.CreateDirectory(pluginRoot);
        CreateSupportPack(BuildTargetId.macOSArm64, "Inno.Player");
        CreateSupportPack(BuildTargetId.windowsX64, "Inno.Player.exe");

        _ = typeof(SceneAsset);
        _ = typeof(ScriptCompiler);
        _ = typeof(BuildProfile);
        m_engine = new EngineHostBuilder()
            .UseMetadataCache(Path.Combine(libraryRoot, "Build", "Metadata"))
            .Build();
        m_sceneWorld = new SceneWorld(m_identities, m_engine.types);
        m_sceneWorldScope = m_sceneWorld.EnterScope();
        m_settings = new ProjectSettingsStore(
            Path.Combine(m_projectRoot, "ProjectSettings.inno"),
            m_engine.types,
            m_engine.serialization);
        var sources = new PluginSourceService(m_engine.serialization, pluginRoot, libraryRoot);
        PluginScanResult scan = sources.Scan();
        m_assets = new AssetPipeline(
            m_engine.modules,
            m_engine.types,
            m_engine.serialization,
            m_identities,
            m_engine.diagnostics,
            m_engine.logs,
            AssetPipelineOptions.Create(assetRoot, libraryRoot) with
            {
                enableFileSystemWatcher = false
            });
        m_plugins = new PluginEnvironment(
            m_assets,
            m_settings,
            m_engine.serialization,
            pluginRoot,
            libraryRoot,
            scan);
        m_compiler = new ScriptCompiler(
            new ScriptCompilerOptions
            {
                projectRootDirectory = m_projectRoot
            },
            m_assets,
            m_plugins);
        m_pipeline = new BuildPipeline(
            m_assets,
            m_plugins,
            m_settings,
            m_engine.serialization,
            m_compiler,
            m_supportPackRoot,
            [
                new MacOSArm64GameBuildTarget(m_assets, m_engine.serialization),
                new WindowsX64GameBuildTarget(m_assets, m_engine.serialization)
            ]);
    }

    public void Dispose()
    {
        m_plugins.Dispose();
        m_assets.Dispose();
        m_settings.Dispose();
        m_sceneWorldScope.Dispose();
        m_sceneWorld.Dispose();
        m_engine.Dispose();
        if (Directory.Exists(m_root))
            Directory.Delete(m_root, recursive: true);
    }

    [Theory]
    [InlineData("macos-arm64")]
    [InlineData("windows-x64")]
    public async Task GameBuildPublishesOnlyVerifiedContentPacksAndRuntimeAssemblies(string targetValue)
    {
        SaveStartupScene();
        BuildTargetId target = new(targetValue);
        using SerializationGeneration serialization = m_engine.serialization.CaptureGeneration();

        BuildResult result = await m_pipeline.BuildGameAsync(new GameBuildRequest
        {
            profile = CreateProfile(target),
            outputDirectory = Path.Combine(m_root, "Builds", targetValue)
        });

        Assert.True(result.succeeded);
        string outputPath = Assert.IsType<string>(result.outputPath);
        string contentHash = Assert.IsType<string>(result.contentHash);
        string packagedContent = target == BuildTargetId.macOSArm64
            ? Path.Combine(outputPath, "Contents", "Resources", "Content")
            : Path.Combine(outputPath, "Content");
        string executable = target == BuildTargetId.macOSArm64
            ? Path.Combine(outputPath, "Contents", "MacOS", "Test Game")
            : Path.Combine(outputPath, "Test Game.exe");
        Assert.True(File.Exists(executable));
        Assert.Equal(
            ["catalog.inno", $"content-{contentHash}.pack", "runtime.manifest"],
            Directory.EnumerateFiles(packagedContent)
                .Select(Path.GetFileName)
                .Order(StringComparer.Ordinal));

        byte[] envelope = File.ReadAllBytes(Path.Combine(packagedContent, "runtime.manifest"));
        Assert.Equal("tests.game", RuntimeManifestEnvelope.ReadApplicationId(envelope));
        GameRuntimeManifest manifest = RuntimeManifestEnvelope.Decode(envelope, serialization);
        Assert.Equal("Scenes/Startup.iscene", manifest.startupScene);
        GameRuntimeModule runtimeModule = Assert.Single(manifest.modules);
        Assert.Equal("RuntimeScripts", runtimeModule.name);
        Assert.Equal("Inno.GameScripts.dll", runtimeModule.mainAssembly);
        Assert.Equal(Inno.Extensibility.Modules.AssemblyDomain.InnoScripting, runtimeModule.domain);

        string persistentRoot = Path.Combine(m_root, "Persistent", manifest.applicationId);
        string materialized = RuntimeContentDeployment.Materialize(packagedContent, persistentRoot);
        using var runtimeAssets = new AssetDatabase(
            materialized,
            serialization,
            m_engine.types,
            new IdentityAllocator());
        Assert.True(File.Exists(Path.Combine(materialized, "AssetDatabase", "Catalog.snapshot")));
        Assert.True(File.Exists(Path.Combine(materialized, "Managed", "Inno.GameScripts.dll")));
        Assert.StartsWith(Path.GetFullPath(persistentRoot), materialized, StringComparison.Ordinal);
        Assert.DoesNotContain(
            Directory.EnumerateFiles(materialized, "*", SearchOption.AllDirectories),
            static path => path.EndsWith(".iscene", StringComparison.OrdinalIgnoreCase)
                           || path.EndsWith(".imeta", StringComparison.OrdinalIgnoreCase)
                           || path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(1, result.assetCount);
        Assert.Equal(1, result.runtimeAssemblyCount);
    }

    [Fact]
    public async Task GameBuildWaitsForPendingStartupSceneImportBeforeValidation()
    {
        AssetPath stagedPath = AssetPath.Project("Staged/Startup.iscene");
        var scene = new GameScene("Startup");
        scene.CreateObject("Player");
        Assert.True(m_assets.Save(
            stagedPath,
            SceneAsset.Capture(scene, m_engine.serialization, m_assets)));
        string stagedSource = Path.Combine(m_projectRoot, "Assets", "Staged", "Startup.iscene");
        string destinationDirectory = Path.Combine(m_projectRoot, "Assets", "Scenes");
        string destinationSource = Path.Combine(destinationDirectory, "Startup.iscene");
        Directory.CreateDirectory(destinationDirectory);
        File.Move(stagedSource, destinationSource);
        File.Move(stagedSource + ".imeta", destinationSource + ".imeta");
        m_assets.Rescan();

        BuildResult result = await m_pipeline.BuildGameAsync(new GameBuildRequest
        {
            profile = CreateProfile(BuildTargetId.macOSArm64),
            outputDirectory = Path.Combine(m_root, "Builds", "PendingImport")
        });

        Assert.True(result.succeeded);
        Assert.NotNull(result.outputPath);
    }

    [Fact]
    public async Task BackgroundArtifactExportUsesAnOwnerThreadSerializationSnapshot()
    {
        SaveStartupScene();
        string destination = Path.Combine(m_root, "RuntimeArtifacts");

        AssetRuntimeContentInfo result = await m_assets.ExportRuntimeArtifactsAsync(destination);

        Assert.Equal(1, result.assetCount);
        Assert.True(File.Exists(Path.Combine(destination, "AssetDatabase", "Catalog.snapshot")));
    }

    [Fact]
    public async Task BackgroundArtifactExportRejectsACorruptCurrentFormatManifest()
    {
        SaveStartupScene();
        string manifest = Assert.Single(Directory.EnumerateFiles(
            m_assets.artifactRoot,
            "manifest",
            SearchOption.AllDirectories));
        File.WriteAllBytes(manifest, [0x42, 0x41, 0x44]);

        InvalidDataException failure = await Assert.ThrowsAsync<InvalidDataException>(() =>
            m_assets.ExportRuntimeArtifactsAsync(Path.Combine(m_root, "CorruptRuntimeArtifacts")));

        Assert.Contains("corrupt current-format manifest", failure.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ChangedAuthoringGenerationCannotCommitAMixedBuildSnapshot()
    {
        SaveStartupScene();
        var target = new BlockingBuildTarget();
        var pipeline = new BuildPipeline(
            m_assets,
            m_plugins,
            m_settings,
            m_engine.serialization,
            m_compiler,
            m_supportPackRoot,
            [target]);
        string outputRoot = Path.Combine(m_root, "Builds", "MixedGeneration");

        Task<BuildResult> build = pipeline.BuildGameAsync(new GameBuildRequest
        {
            profile = CreateProfile(BuildTargetId.macOSArm64),
            outputDirectory = outputRoot
        }).AsTask();
        Assert.True(SpinWait.SpinUntil(
            () => target.started.Task.IsCompleted,
            TimeSpan.FromSeconds(2)));
        Assert.True(m_assets.Save(AssetPath.Project("Changed.txt"), new TextAsset("new generation")));
        target.release.SetResult();

        InvalidOperationException failure = await Assert.ThrowsAsync<InvalidOperationException>(() => build);

        Assert.Contains("generation changed", failure.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(Directory.Exists(Path.Combine(outputRoot, "Test Game.app")));
    }

    [Fact]
    public async Task CompilationFailureLeavesNoProductOrStagingDirectory()
    {
        SaveStartupScene();
        string scriptPath = Path.Combine(m_projectRoot, "Assets", "Scripts", "Broken.cs");
        Directory.CreateDirectory(Path.GetDirectoryName(scriptPath)!);
        File.WriteAllText(scriptPath, "public sealed class Broken {");
        m_assets.Rescan();
        string outputRoot = Path.Combine(m_root, "Builds", "Failed");

        BuildResult result = await m_pipeline.BuildGameAsync(new GameBuildRequest
        {
            profile = CreateProfile(BuildTargetId.macOSArm64),
            outputDirectory = outputRoot
        });

        Assert.False(result.succeeded);
        Assert.Null(result.outputPath);
        Assert.Contains(result.diagnostics, static diagnostic =>
            diagnostic.severity == BuildDiagnosticSeverity.Error);
        Assert.False(Directory.Exists(Path.Combine(outputRoot, "Test Game.app")));
        if (Directory.Exists(outputRoot))
        {
            Assert.Empty(Directory.EnumerateDirectories(
                outputRoot,
                ".inno-build-*",
                SearchOption.TopDirectoryOnly));
        }
    }

    [Fact]
    public async Task CanceledGameBuildLeavesNoProductOrStagingDirectory()
    {
        SaveStartupScene();
        string outputRoot = Path.Combine(m_root, "Builds", "CanceledGame");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            m_pipeline.BuildGameAsync(
                new GameBuildRequest
                {
                    profile = CreateProfile(BuildTargetId.macOSArm64),
                    outputDirectory = outputRoot
                },
                cancellationToken: cancellation.Token).AsTask());

        Assert.False(Directory.Exists(Path.Combine(outputRoot, "Test Game.app")));
        if (Directory.Exists(outputRoot))
        {
            Assert.Empty(Directory.EnumerateDirectories(
                outputRoot,
                ".inno-build-*",
                SearchOption.TopDirectoryOnly));
        }
    }

    [Fact]
    public async Task GameBuildRejectsManagedProjectOutputWithoutLeavingAProduct()
    {
        SaveStartupScene();
        string output = Path.Combine(m_projectRoot, "Assets", "Builds");

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            m_pipeline.BuildGameAsync(new GameBuildRequest
            {
                profile = CreateProfile(BuildTargetId.macOSArm64),
                outputDirectory = output
            }).AsTask());

        Assert.Contains("managed project content", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(Directory.Exists(Path.Combine(output, "Test Game.app")));
    }

    [Theory]
    [InlineData("CON")]
    [InlineData("nul.txt")]
    [InlineData("Trailing.")]
    [InlineData("Trailing ")]
    [InlineData("Bad:Name")]
    public void BuildProfileRejectsNamesThatAreInvalidOnEitherTarget(string productName)
    {
        BuildProfile profile = CreateProfile(BuildTargetId.macOSArm64, productName);

        Assert.Throws<InvalidDataException>(profile.Validate);
    }

    [Fact]
    public void BuildProfileStoreRoundTripsGeneratedCurrentFormat()
    {
        string path = Path.Combine(m_projectRoot, "BuildProfile.inno");
        var store = new BuildProfileStore(path, m_engine.serialization);
        BuildProfile source = CreateProfile(BuildTargetId.windowsX64, "Stored Game");
        source.windowWidth = 1920;
        source.windowHeight = 1080;

        store.Save(source);
        BuildProfile restored = store.Load();

        Assert.True(store.exists);
        Assert.Equal(source.applicationId, restored.applicationId);
        Assert.Equal(source.productName, restored.productName);
        Assert.Equal(source.startupScene, restored.startupScene);
        Assert.Equal(BuildTargetId.windowsX64, restored.target);
        Assert.Equal(1920, restored.windowWidth);
        Assert.Equal(1080, restored.windowHeight);
    }

    [Fact]
    public void BuildProfileStorePreservesCommittedDocumentWhenCandidateIsInvalid()
    {
        string path = Path.Combine(m_projectRoot, "BuildProfile.inno");
        var store = new BuildProfileStore(path, m_engine.serialization);
        BuildProfile committed = CreateProfile(BuildTargetId.macOSArm64, "Committed Game");
        store.Save(committed);
        byte[] committedBytes = File.ReadAllBytes(path);
        BuildProfile invalid = CreateProfile(BuildTargetId.windowsX64, "Invalid/Game");

        Assert.Throws<InvalidDataException>(() => store.Save(invalid));

        Assert.Equal(committedBytes, File.ReadAllBytes(path));
        Assert.Empty(Directory.EnumerateFiles(m_projectRoot, "BuildProfile.inno.staging-*"));
    }

    [Fact]
    public void BuildProfileStoreRejectsCorruptCurrentFormatWithoutFallback()
    {
        string path = Path.Combine(m_projectRoot, "BuildProfile.inno");
        File.WriteAllBytes(path, [0x42, 0x41, 0x44]);
        var store = new BuildProfileStore(path, m_engine.serialization);

        InvalidDataException failure = Assert.Throws<InvalidDataException>(store.Load);

        Assert.Contains("current-format", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SupportPackRejectsAuthoringAssetPipelineAssemblies()
    {
        string supportPack = Path.Combine(m_supportPackRoot, BuildTargetId.macOSArm64.value);
        File.WriteAllBytes(Path.Combine(supportPack, "Inno.Assets.Pipeline.dll"), [0x49, 0x4E, 0x4E, 0x4F]);
        var catalog = new PlayerSupportPackCatalog(m_supportPackRoot);

        InvalidDataException exception = Assert.Throws<InvalidDataException>(
            () => catalog.Resolve(BuildTargetId.macOSArm64));

        Assert.Contains("forbidden build-time file", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PluginBuildIsDeterministicSourceOnlyAndUsesTheInstallContract()
    {
        m_assets.CreateDirectory(AssetPath.Project("Content"));
        Assert.True(m_assets.Save(
            AssetPath.Project("Content/value.txt"),
            new TextAsset("deterministic")));
        string firstPath = Path.Combine(m_root, "first.zip");
        string secondPath = Path.Combine(m_root, "second.zip");

        ValueTask<BuildResult> firstBuild = m_pipeline.BuildPluginAsync(new PluginBuildRequest
        {
            pluginId = "tests.export",
            displayName = "Export Test",
            outputPath = firstPath
        });
        ValueTask<BuildResult> secondBuild = m_pipeline.BuildPluginAsync(new PluginBuildRequest
        {
            pluginId = "tests.export",
            displayName = "Export Test",
            outputPath = secondPath
        });
        BuildResult[] results = await Task.WhenAll(firstBuild.AsTask(), secondBuild.AsTask());
        BuildResult first = results[0];
        BuildResult second = results[1];

        Assert.True(first.succeeded);
        Assert.True(second.succeeded);
        Assert.Equal(first.contentHash, second.contentHash);
        Assert.Equal(File.ReadAllBytes(firstPath), File.ReadAllBytes(secondPath));
        using (ZipArchive archive = ZipFile.OpenRead(firstPath))
        {
            Assert.Contains(archive.Entries, static entry => entry.FullName == "Plugin.inno");
            Assert.Contains(archive.Entries, static entry => entry.FullName == "Assets/Content/value.txt");
            Assert.All(archive.Entries, static entry => Assert.Equal(1980, entry.LastWriteTime.Year));
            Assert.DoesNotContain(archive.Entries, static entry =>
                string.Equals(Path.GetExtension(entry.FullName), ".dll", StringComparison.OrdinalIgnoreCase));
        }

        string installRoot = Path.Combine(m_root, "Install");
        Directory.CreateDirectory(installRoot);
        File.Copy(firstPath, Path.Combine(installRoot, "export.zip"));
        PluginScanResult scan = new PluginSourceService(
            m_engine.serialization,
            installRoot,
            Path.Combine(m_root, "InstallLibrary")).Scan();
        Assert.Empty(scan.diagnostics);
        PluginCandidate candidate = Assert.Single(scan.candidates);
        Assert.Equal("tests.export", candidate.manifest.pluginId);
        Assert.Equal(PluginSourceKind.Zip, candidate.sourceKind);
    }

    [Fact]
    public async Task CanceledPluginBuildLeavesNoPackageOrStagingFile()
    {
        Assert.True(m_assets.Save(AssetPath.Project("value.txt"), new TextAsset("content")));
        string output = Path.Combine(m_root, "Canceled.zip");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            m_pipeline.BuildPluginAsync(
                new PluginBuildRequest
                {
                    pluginId = "tests.canceled",
                    displayName = "Canceled",
                    outputPath = output
                },
                cancellationToken: cancellation.Token).AsTask());

        Assert.False(File.Exists(output));
        Assert.Empty(Directory.EnumerateFiles(m_root, "Canceled.zip.staging-*", SearchOption.TopDirectoryOnly));
    }

    private static BuildProfile CreateProfile(BuildTargetId target, string productName = "Test Game")
        => new()
        {
            applicationId = "tests.game",
            productName = productName,
            startupScene = "Scenes/Startup.iscene",
            target = target
        };

    private void SaveStartupScene()
    {
        var scene = new GameScene("Startup");
        scene.CreateObject("Player");
        Assert.True(m_assets.Save(
            AssetPath.Project("Scenes/Startup.iscene"),
            SceneAsset.Capture(scene, m_engine.serialization, m_assets)));
    }

    private void CreateSupportPack(BuildTargetId target, string executable)
    {
        string directory = Path.Combine(m_supportPackRoot, target.value);
        Directory.CreateDirectory(directory);
        foreach (string assembly in Directory
                     .EnumerateFiles(AppContext.BaseDirectory, "Inno.*.dll", SearchOption.TopDirectoryOnly)
                     .Where(static path => !IsAuthoringAssembly(Path.GetFileName(path))))
        {
            File.Copy(assembly, Path.Combine(directory, Path.GetFileName(assembly)));
        }
        File.WriteAllBytes(Path.Combine(directory, executable), [0x49, 0x4E, 0x4E, 0x4F]);
        string native = Path.Combine(directory, "native");
        Directory.CreateDirectory(native);
        string[] required = target == BuildTargetId.macOSArm64
            ? ["libbgfx-shared-lib-release.dylib", "SDL3-release.dylib"]
            : ["bgfx-shared-lib-release.dll", "SDL3-release.dll"];
        foreach (string file in required)
            File.WriteAllBytes(Path.Combine(native, file), [0x49, 0x4E, 0x4E, 0x4F]);
    }

    private static bool IsAuthoringAssembly(string name)
        => name.Contains("Inno.Editor", StringComparison.OrdinalIgnoreCase)
           || name.Contains("Inno.Build", StringComparison.OrdinalIgnoreCase)
           || name.Contains("Inno.Scripting.Compiler", StringComparison.OrdinalIgnoreCase)
           || name.Contains("Inno.Assets.Pipeline", StringComparison.OrdinalIgnoreCase)
           || name.Contains("Inno.Plugins.Authoring", StringComparison.OrdinalIgnoreCase);

    private sealed class BlockingBuildTarget : IGameBuildTarget
    {
        internal TaskCompletionSource started { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource release { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public BuildTargetId id => BuildTargetId.macOSArm64;

        public async ValueTask BuildContentAsync(
            GameBuildContentContext context,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(context);
            started.SetResult();
            await release.Task.WaitAsync(cancellationToken);
        }

        public ValueTask<string> PackageAsync(
            GameBuildPackageContext context,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(context);
            cancellationToken.ThrowIfCancellationRequested();
            throw new InvalidOperationException("A changed generation must fail before packaging.");
        }
    }
}

[CollectionDefinition("Build pipeline serialization", DisableParallelization = true)]
public sealed class BuildPipelineSerializationCollection
{
}
