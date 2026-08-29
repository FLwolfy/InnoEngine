using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Inno.Assets;
using Inno.Assets.Core;
using Inno.Assets.File;
using Inno.Assets.Loader;
using Inno.Assets.Plugins;
using Inno.Assets.Serialization;
using Inno.Assets.Types;
using Inno.Core.Assemblies;
using Inno.Core.Diagnose;
using Inno.Core.Events;
using Inno.Core.Graphs;
using Inno.Core.Identity;
using Inno.Core.Input;
using Inno.Core.Reflection;
using Inno.Core.Serialization;
using Inno.Core.Settings;
using Inno.Editor.Core;
using Inno.Editor.Interactions;
using Inno.Editor.ImGui;
using Inno.Editor.ImGui.ImGuiWidget;
using EditorWidget = Inno.Editor.ImGui.ImGuiWidget.ImGuiWidget;
using Inno.Editor.Inspection;
using Inno.Editor.Panel.FileBrowser;
using Inno.Editor.Panel.Hierarchy;
using Inno.Editor.Panel.Inspector;
using Inno.Editor.Rendering;
using Inno.Editor.Scene;
using Inno.Editor.Settings;
using Inno.Editor.Scripting;
using Inno.Engine.Scene;
using Inno.Engine.Scene.Assets;
using Inno.Engine.Scene.Layers;
using Inno.Platform.ImGui;
using Inno.Rendering;
using Inno.Rendering.Core;
using Inno.Rendering.ShaderGraph;

using Xunit;

namespace Inno.Editor.Scripting.Tests;

public sealed class ScriptManagerTests : IDisposable
{
    private readonly string m_projectRoot = Path.Combine(
        Path.GetTempPath(),
        "InnoScriptManagerTests",
        Guid.NewGuid().ToString("N"));
    private readonly ScriptManager m_manager;
    private readonly IDisposable m_sceneReloadIntegration;

    public ScriptManagerTests()
    {
        Directory.CreateDirectory(Path.Combine(m_projectRoot, "Assets"));
        _ = Assembly.Load(typeof(GameBehavior).Assembly.GetName());
        _ = Assembly.Load(typeof(SceneAsset).Assembly.GetName());
        _ = Assembly.Load(typeof(AssetImporter).Assembly.GetName());
        _ = Assembly.Load(typeof(TextAsset).Assembly.GetName());
        _ = Assembly.Load(typeof(EditorContext).Assembly.GetName());
        _ = Assembly.Load(typeof(ImGuiWidget).Assembly.GetName());
        _ = Assembly.Load(typeof(AssetEditor).Assembly.GetName());
        _ = Assembly.Load(typeof(IPropertyDrawer).Assembly.GetName());
        _ = Assembly.Load(typeof(EditorRenderingModule).Assembly.GetName());
        _ = Assembly.Load("Inno.Editor.Panel.Settings");
        IdentityManager.Initialize();
        AssemblyManager.Initialize(new AssemblyManagerOptions
        {
            cacheDirectory = Path.Combine(m_projectRoot, "Library", "Assemblies")
        });
        TypeCacheManager.Initialize();
        SerializationManager.Initialize();
        ProjectSettingsManager.Initialize(Path.Combine(m_projectRoot, "ProjectSettings.inno"));
        AssetManager.Initialize(AssetManagerOptions.Create(
            Path.Combine(m_projectRoot, "Assets"),
            Path.Combine(m_projectRoot, "Library")) with
        {
            enableFileSystemWatcher = false
        });
        Type sceneReloadIntegrationType = typeof(SceneEdits).Assembly.GetType(
            "Inno.Editor.Scene.SceneReloadIntegration",
            throwOnError: true)!;
        m_sceneReloadIntegration = ScriptingTestReflection.InvokeStatic<IDisposable>(
            sceneReloadIntegrationType,
            "Acquire");
        m_manager = ScriptingTestReflection.CreateScriptManager(
            new ScriptManagerOptions
            {
                projectRootDirectory = m_projectRoot,
                autoCompile = false,
                debounceMilliseconds = 0
            },
            compileGateProbe: null);
    }

    public void Dispose()
    {
        SceneManager.UnloadAllScenes();
        m_manager.Dispose();
        m_sceneReloadIntegration.Dispose();
        PluginManager.Shutdown();
        PluginCatalog.Shutdown();
        AssetManager.Shutdown();
        ProjectSettingsManager.Shutdown();
        SerializationManager.Shutdown();
        TypeCacheManager.Shutdown();
        AssemblyManager.Shutdown();
        IdentityManager.Shutdown();
        if (Directory.Exists(m_projectRoot))
            Directory.Delete(m_projectRoot, recursive: true);
    }

    [Fact]
    public async Task InitialAutomaticCompilationDoesNotWaitForFocusOrDebounce()
    {
        Write("InitialCompileProbe.cs", "public sealed class InitialCompileProbe { }");
        using var automaticManager = new ScriptManager(new ScriptManagerOptions
        {
            projectRootDirectory = m_projectRoot,
            autoCompile = true,
            debounceMilliseconds = 60_000
        });

        automaticManager.Start();

        Assert.Contains("plugin", automaticManager.compilationStatus, StringComparison.OrdinalIgnoreCase);
        Assert.True(automaticManager.TryCompilePending(out Task<ScriptCompilationResult>? compilation));
        Assert.NotNull(compilation);
        ScriptCompilationResult result = await compilation!;
        Assert.True(result.success, FormatDiagnostics(result));
    }

    [Fact]
    public void QueuedMenuReloadIsVisibleAtZeroProgressBeforeCompilationStarts()
    {
        Type editorScriptingType = typeof(ScriptManager).Assembly.GetType(
            "Inno.Editor.Scripting.EditorScripting",
            throwOnError: true)!;
        object scripting = ScriptingTestReflection.Create(editorScriptingType);
        FieldInfo managerField = editorScriptingType.GetField(
            "m_manager",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        managerField.SetValue(scripting, m_manager);
        try
        {
            ScriptingTestReflection.Invoke(scripting, "ReloadPlugins");

            Assert.True(ScriptingTestReflection.Get<bool>(scripting, "isCompiling"));
            Assert.Equal(0f, ScriptingTestReflection.Get<float>(scripting, "progress"));
            Assert.Contains(
                "queued",
                ScriptingTestReflection.Get<string>(scripting, "status"),
                StringComparison.OrdinalIgnoreCase);
            Assert.True(m_manager.isCompilationPending);
        }
        finally
        {
            managerField.SetValue(scripting, null);
        }
    }

    [Fact]
    public void SuccessfulCompilationReservesProgressForAtomicActivation()
    {
        Write("ProgressProbe.cs", "public sealed class ProgressProbe { }");

        ScriptCompilationResult result = Compile();

        Assert.True(result.success, FormatDiagnostics(result));
        Assert.Equal(0.8f, m_manager.compilationProgress, 3);
        Assert.True(m_manager.ApplyPendingReload());
        Assert.Equal(1f, m_manager.compilationProgress);
    }

    [Fact]
    public async Task ConcurrentCompilationAllowsOnlyOneCompilerGateOwner()
    {
        Write("ConcurrentProbe.cs", "public sealed class ConcurrentProbe { }");
        AssetManager.Update();
        AssetManager.Rescan();
        var firstEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        int entryCount = 0;
        using var manager = ScriptingTestReflection.CreateScriptManager(
            new ScriptManagerOptions
            {
                projectRootDirectory = m_projectRoot,
                autoCompile = false,
                debounceMilliseconds = 0
            },
            async cancellationToken =>
            {
                int entry = Interlocked.Increment(ref entryCount);
                if (entry != 1)
                    return;
                firstEntered.SetResult();
                await releaseFirst.Task.WaitAsync(cancellationToken);
            });

        Task<ScriptCompilationResult> first = manager.CompileAsync().AsTask();
        await firstEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Task<ScriptCompilationResult> second = manager.CompileAsync().AsTask();
        await Task.Delay(50);

        Assert.True(manager.isCompiling);
        Assert.Equal(1, Volatile.Read(ref entryCount));
        releaseFirst.SetResult();
        ScriptCompilationResult[] results = await Task.WhenAll(first, second);
        Assert.Equal(2, Volatile.Read(ref entryCount));
        Assert.All(results, result => Assert.True(result.success, FormatDiagnostics(result)));
        Assert.False(manager.isCompiling);
    }

    // AssetManager deliberately enforces one owner thread, so this test must not resume on a pool thread.
#pragma warning disable xUnit1031
    [Fact]
    public void StrongerRequestDuringCompilationSupersedesTheIntermediateGeneration()
    {
        int entryCount = 0;
        var firstEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var manager = ScriptingTestReflection.CreateScriptManager(
            new ScriptManagerOptions
            {
                projectRootDirectory = m_projectRoot,
                autoCompile = false,
                debounceMilliseconds = 0
            },
            async cancellationToken =>
            {
                if (Interlocked.Increment(ref entryCount) != 1)
                    return;
                firstEntered.SetResult();
                await releaseFirst.Task.WaitAsync(cancellationToken);
            });
        manager.RecompileScripting();
        Assert.True(manager.TryCompilePending(out Task<ScriptCompilationResult>? first));
        Assert.True(firstEntered.Task.Wait(TimeSpan.FromSeconds(5)));

        manager.ReloadScripting();
        manager.ReloadPlugins();
        releaseFirst.SetResult();
        Assert.True(first!.GetAwaiter().GetResult().success);

        Assert.False(manager.ApplyPendingReload());
        Assert.Empty(AssemblyManager.modules);
        Assert.True(manager.isCompilationPending);
        Assert.True(manager.TryCompilePending(out Task<ScriptCompilationResult>? replacement));
        Assert.True(replacement!.GetAwaiter().GetResult().success);
        Assert.True(manager.ApplyPendingReload());
        Assert.Equal(3, AssemblyManager.modules.Count);
        Assert.Single(AssemblyManager.modules, static module => module.domain == AssemblyDomain.InnoPlugin);
        Assert.Equal(2, AssemblyManager.modules.Count(static module =>
            module.domain == AssemblyDomain.InnoScripting));
    }
#pragma warning restore xUnit1031

    [Fact]
    public async Task DisposeWaitsUntilTheActiveCompilationHasObservedCancellationAndExited()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancellationObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowExit = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var manager = ScriptingTestReflection.CreateScriptManager(
            new ScriptManagerOptions
            {
                projectRootDirectory = m_projectRoot,
                autoCompile = false,
                debounceMilliseconds = 0
            },
            async cancellationToken =>
            {
                entered.SetResult();
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    cancellationObserved.SetResult();
                    await allowExit.Task;
                    throw;
                }
            });

        Task<ScriptCompilationResult> compilation = manager.CompileAsync().AsTask();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Task disposal = Task.Run(manager.Dispose);
        await cancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Task concurrentDisposal = Task.Run(manager.Dispose);

        Assert.False(disposal.IsCompleted);
        Assert.False(concurrentDisposal.IsCompleted);
        allowExit.SetResult();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => compilation);
        await Task.WhenAll(disposal, concurrentDisposal).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(manager.isCompiling);
        Assert.Null(manager.lastCompilation);
    }

    [Fact]
    public async Task DisposeCancelsQueuedCompilationWithoutAllowingItToEnterTheCompilerGate()
    {
        int entryCount = 0;
        var firstEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var manager = ScriptingTestReflection.CreateScriptManager(
            new ScriptManagerOptions
            {
                projectRootDirectory = m_projectRoot,
                autoCompile = false,
                debounceMilliseconds = 0
            },
            async cancellationToken =>
            {
                Interlocked.Increment(ref entryCount);
                firstEntered.SetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            });

        Task<ScriptCompilationResult> first = manager.CompileAsync().AsTask();
        await firstEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Task<ScriptCompilationResult> queued = manager.CompileAsync().AsTask();
        await Task.Delay(50);

        Task disposal = Task.Run(manager.Dispose);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => queued);
        await disposal.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, Volatile.Read(ref entryCount));
        Assert.False(manager.isCompiling);
        Assert.Null(manager.lastCompilation);
    }

    [Fact]
    public void RuntimeAndEditorScriptsCompileWithExplicitFacadeUsings()
    {
        Write("ProjectBehavior.CS", """
            using InnoEngine.Assets;
            using InnoEngine.Reflection;
            using InnoEngine.Scene;
            using InnoEngine.Serialization;

            [StableTypeId("f2278b82-f39a-4d87-8dc5-440c52971c51")]
            public class ProjectBehavior : GameBehavior
            {
                [SerializableProperty]
                public int value { get; set; } = 7;

                [SerializableProperty]
                public GameLayer layer { get; set; } = GameLayer.defaultLayer;

                [SerializableProperty]
                public GameLayerMask mask { get; set; } = GameLayerMask.everything;
            }

            [AssetImporterExtension]
            public sealed class ProjectImporter : AssetImporter<TextAsset>
            {
                public override string[] supportedExtensions => [".project"];

                protected override async System.Threading.Tasks.ValueTask ImportAsync(
                    AssetImportContext context,
                    AssetImportWriter<TextAsset> output,
                    System.Threading.CancellationToken cancellationToken)
                {
                    output.SetAsset(new TextAsset(context.ReadUtf8Text()));
                    await output.WriteArtifactAsync("runtime", context.sourceBytes, cancellationToken);
                }
            }

            [AssetBuildProcessorExtension]
            public sealed class ProjectBuildProcessor : AssetBuildProcessor<TextAsset>
            {
                protected override System.Threading.Tasks.ValueTask BuildAsync(
                    AssetBuildContext<TextAsset> context,
                    AssetArtifactWriter output,
                    System.Threading.CancellationToken cancellationToken)
                {
                    return output.WriteAsync(
                        "result",
                        System.BitConverter.GetBytes(context.inputs.Count),
                        cancellationToken);
                }
            }
            """);
        Write("ProjectTools.EDITOR.CS", """
            using InnoEditor.Inspection;

            [PropertyDrawer(typeof(ProjectBehavior))]
            public sealed class ProjectBehaviorDrawer : IPropertyDrawer
            {
                public void Draw(PropertyDrawContext context)
                {
                }
            }
            """);

        ScriptCompilationResult result = Compile();

        Assert.True(result.success, FormatDiagnostics(result));
        Assert.NotNull(result.outputDirectory);
        Assert.True(File.Exists(Path.Combine(result.outputDirectory!, "Inno.GameScripts.dll")));
        Assert.True(File.Exists(Path.Combine(result.outputDirectory!, "Inno.GameScripts.pdb")));
        Assert.True(File.Exists(Path.Combine(result.outputDirectory!, "Inno.EditorScripts.dll")));
        Assert.True(m_manager.ApplyPendingReload());
        Type behavior = ResolveTypeByName("ProjectBehavior");
        Type drawer = ResolveTypeByName("ProjectBehaviorDrawer");
        Assert.Equal(AssemblyDomain.InnoScripting, behavior.Assembly.GetInnoAssemblyDomain());
        Assert.Equal(AssemblyScope.Runtime, behavior.Assembly.GetInnoAssemblyScope());
        Assert.Equal(AssemblyDomain.InnoScripting, drawer.Assembly.GetInnoAssemblyDomain());
        Assert.Equal(AssemblyScope.Editor, drawer.Assembly.GetInnoAssemblyScope());
    }

    [Fact]
    public void TrustedZipPluginProvidesRasterComputeShaderNodeAndGameplayExtensions()
    {
        const string pipelineId = "tests.fixture.pipeline";
        const string shaderNodeId = "tests.fixture.compute-value";
        InstallProgrammableRenderingPlugin();

        ScriptCompilationResult result = Compile();

        Assert.True(result.success, FormatDiagnostics(result));
        Assert.True(m_manager.ApplyPendingReload());
        Assert.False(PluginManager.hasPendingActivation);
        Assert.Equal("tests.rendering-fixture", Assert.Single(PluginCatalog.activePlugins).manifest.pluginId);

        Type componentType = ResolveTypeByName("FixtureRenderComponent");
        Type pipelineType = ResolveTypeByName("FixtureRenderPipeline");
        Type nodeType = ResolveTypeByName("FixtureComputeValueNode");
        Assert.True(typeof(GameBehavior).IsAssignableFrom(componentType));
        Assert.True(typeof(RenderPipeline).IsAssignableFrom(pipelineType));
        Assert.True(typeof(ShaderNodeDefinition).IsAssignableFrom(nodeType));
        Assert.All(
            new[] { componentType, pipelineType, nodeType },
            static type => Assert.Equal(AssemblyDomain.InnoPlugin, type.Assembly.GetInnoAssemblyDomain()));

        using var nodes = new ShaderNodeRegistry(discoverExtensions: true);
        nodes.RefreshExtensions();
        Assert.True(nodes.TryResolveShader(shaderNodeId, out ShaderNodeDefinition? node));
        Assert.Equal(ShaderStage.Compute, node!.supportedStages);

        var capabilities = new GraphicsCapabilities(
            GraphicsBackend.Noop,
            GraphicsFeature.Compute,
            new GraphicsLimits(64, 4, 4096, 8),
            Enum.GetValues<RenderTextureFormat>(),
            Enum.GetValues<RenderTextureFormat>(),
            Enum.GetValues<RenderTextureFormat>(),
            Enum.GetValues<RenderTextureFormat>(),
            originBottomLeft: false,
            homogeneousDepth: false);
        var graph = new RenderGraphBuilder(1, capabilities);
        var asset = new RenderPipelineAsset { pipelineTypeId = pipelineId };
        var request = new RenderRequest(
            "Plugin Fixture",
            RenderTarget.backbuffer,
            new RenderViewport(0, 0, 64, 64),
            asset);
        IRenderResourceService resources = DispatchProxy.Create<
            IRenderResourceService,
            EmptyRenderResourceServiceProxy>();
        var context = new RenderPipelineContext(
            request,
            asset,
            graph,
            capabilities,
            new RenderResourceRegistry(),
            new FixtureRenderDiagnosticSink(),
            resources,
            frameIndex: 0);
        using RenderPipeline pipeline = (RenderPipeline)Activator.CreateInstance(pipelineType)!;
        pipeline.Build(context);
        RenderGraphCompileResult compiled = graph.Compile();

        Assert.True(compiled.succeeded, string.Join("; ", compiled.diagnostics.Select(d => d.message)));
        Assert.Collection(
            compiled.graph!.passes,
            pass =>
            {
                Assert.Equal("Fixture Clear Triangle", pass.name);
                Assert.Equal(RenderPassKind.Raster, pass.kind);
                Assert.True(pass.clearsPresentationTarget);
            },
            pass =>
            {
                Assert.Equal("Fixture Compute", pass.name);
                Assert.Equal(RenderPassKind.Compute, pass.kind);
            });
    }

#pragma warning disable xUnit1031
    [Fact]
    public void EditorViewportProviderFollowsTheActivePluginGeneration()
    {
        const string pluginId = "tests.rendering-fixture";
        var kind = new EditorViewportKindId("tests.fixture.viewport");
        InstallProgrammableRenderingPlugin();
        ScriptCompilationResult initial = Compile();
        Assert.True(initial.success, FormatDiagnostics(initial));
        Assert.True(m_manager.ApplyPendingReload());

        var host = new TestEditorRenderingHost();
        Type providerType;
        {
            var context = new EditorContext(m_projectRoot);
            using var interactions = new EditorInteractionRuntime(context);
            using var module = new EditorRenderingModule(host, interactions.interactions);
            module.Start(context);
            try
            {
                Assert.True(module.HasProvider(kind));
                Assert.True(module.TrySubmit(kind, "tests.viewport", 320, 180, out EditorViewportOutput output));
                Assert.True(output.isReady);
                Assert.NotNull(host.lastRequest);
                EditorViewportRequest request = host.lastRequest!;
                Assert.Equal("tests.viewport", request.viewportId);
                Assert.Equal(320, request.pixelWidth);
                Assert.Equal(180, request.pixelHeight);
                Assert.Equal(RenderTextureFormat.RGBA16Float, request.targetFormat);
                Assert.Equal(17, request.priority);
                Assert.True(request.data.TryGet(
                    new RenderDataChannelId("tests.fixture.viewport-size"),
                    out string? viewportSize));
                Assert.Equal("320x180", viewportSize);

                module.DrawProviderToolbar(kind, "tests.viewport", 320, 180);
                module.HandlePointer(kind, "tests.viewport", 320, 180, -1f, 2f, 4);
                providerType = ResolveTypeByName("FixtureViewportProvider");
                Assert.Equal(1, GetStaticField<int>(providerType, "toolbarDrawCount"));
                Assert.Equal(0f, GetStaticField<float>(providerType, "lastPointerX"));
                Assert.Equal(1f, GetStaticField<float>(providerType, "lastPointerY"));
                Assert.Equal(4, GetStaticField<int>(providerType, "lastPointerButton"));
            }
            finally
            {
                module.Stop(context);
            }
        }

        string archivePath = Assert.Single(PluginCatalog.activePlugins).archivePath;
        Assert.Equal(pluginId, Assert.Single(PluginCatalog.activePlugins).manifest.pluginId);
        File.Delete(archivePath);
        Assert.True(PluginManager.Refresh());
        m_manager.ReloadPlugins();
        Assert.True(m_manager.TryCompilePending(out Task<ScriptCompilationResult>? compilation));
        ScriptCompilationResult removed = compilation!.GetAwaiter().GetResult();
        Assert.True(removed.success, FormatDiagnostics(removed));
        Assert.True(m_manager.ApplyPendingReload());
        providerType = null!;

        Assert.Empty(PluginCatalog.activePlugins);
        {
            var context = new EditorContext(m_projectRoot);
            using var interactions = new EditorInteractionRuntime(context);
            using var module = new EditorRenderingModule(host, interactions.interactions);
            module.Start(context);
            try
            {
                Assert.False(module.HasProvider(kind));
                Assert.False(module.TrySubmit(kind, "tests.viewport", 320, 180, out _));
                Assert.Contains("tests.viewport", host.releasedViewportIds);
                Assert.Null(module.GetProviderError(kind));
            }
            finally
            {
                module.Stop(context);
            }
        }
    }
#pragma warning restore xUnit1031

#pragma warning disable xUnit1031
    [Fact]
    public void PluginCompilationFailureKeepsTheCompleteActiveGeneration()
    {
        InstallProgrammableRenderingPlugin();
        ScriptCompilationResult initial = Compile();
        Assert.True(initial.success, FormatDiagnostics(initial));
        Assert.True(m_manager.ApplyPendingReload());
        PluginArchiveCandidate activePlugin = Assert.Single(PluginCatalog.activePlugins);
        string activeHash = activePlugin.contentHash;
        Type activePipeline = ResolveTypeByName("FixtureRenderPipeline");
        string archivePath = activePlugin.archivePath;
        byte[] metadata;
        using (var archive = ZipFile.OpenRead(archivePath))
        {
            ZipArchiveEntry entry = archive.GetEntry(
                "Assets/ProgrammableRenderingFixture.cs.imeta")!;
            using Stream input = entry.Open();
            using var bytes = new MemoryStream();
            input.CopyTo(bytes);
            metadata = bytes.ToArray();
        }

        using (FileStream stream = File.Create(archivePath))
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
        {
            WritePluginEntry(archive, "Plugin.inno", SerializationManager.Serialize(new PluginManifest
            {
                pluginId = "tests.rendering-fixture",
                displayName = "Programmable Rendering Fixture"
            }));
            WritePluginEntry(
                archive,
                "Assets/ProgrammableRenderingFixture.cs",
                "public sealed class BrokenPlugin {"u8.ToArray());
            WritePluginEntry(
                archive,
                "Assets/ProgrammableRenderingFixture.cs.imeta",
                metadata);
        }

        Assert.True(PluginManager.Refresh());
        Assert.True(PluginManager.hasPendingActivation);
        Assert.Equal(activeHash, Assert.Single(PluginCatalog.activePlugins).contentHash);
        Assert.Same(activePipeline, ResolveTypeByName("FixtureRenderPipeline"));
        Assert.Equal(
            [AssetSourceId.project, new AssetSourceId("tests.rendering-fixture")],
            AssetManager.sourceMounts.Select(static mount => mount.id));

        m_manager.ReloadPlugins();
        Assert.True(m_manager.TryCompilePending(out Task<ScriptCompilationResult>? compilation));
        ScriptCompilationResult failed = compilation!.GetAwaiter().GetResult();
        Assert.False(failed.success);
        CompleteCompilationThroughEditorHost(failed);

        Assert.False(PluginManager.hasPendingActivation);
        Assert.Equal(activeHash, Assert.Single(PluginCatalog.activePlugins).contentHash);
        Assert.Same(activePipeline, ResolveTypeByName("FixtureRenderPipeline"));
        Assert.Equal(
            [AssetSourceId.project, new AssetSourceId("tests.rendering-fixture")],
            AssetManager.sourceMounts.Select(static mount => mount.id));
    }
#pragma warning restore xUnit1031

    [Fact]
    public void ProjectImporterCandidateFailureRollsBackAssemblyAndAssetCatalog()
    {
        Write("Data/value.candidate", "accepted");
        WriteCandidateImporterScript(generation: 1, rejectFailureToken: false);
        ScriptCompilationResult initial = Compile();
        Assert.True(initial.success, FormatDiagnostics(initial));
        Assert.True(m_manager.ApplyPendingReload());

        Type previousType = ResolveTypeByName("CandidateImportedAsset");
        AssetObject initialAsset = Assert.IsAssignableFrom<AssetObject>(
            LoadAsset("Data/value.candidate", previousType));
        Assert.Equal(1, GetProperty<int>(initialAsset, "importerGeneration"));
        Assert.Equal("accepted", GetProperty<string>(initialAsset, "value"));

        Write("Data/value.candidate", "fail");
        WriteCandidateImporterScript(generation: 2, rejectFailureToken: true);
        ScriptCompilationResult candidate = Compile();
        Assert.True(candidate.success, FormatDiagnostics(candidate));

        Exception failure = Assert.ThrowsAny<Exception>(() => m_manager.ApplyPendingReload());

        Assert.Contains(
            "introduced or changed writable Asset import failures",
            failure.ToString(),
            StringComparison.Ordinal);
        Assert.Same(previousType, ResolveTypeByName("CandidateImportedAsset"));
        AssetObject restoredAsset = Assert.IsAssignableFrom<AssetObject>(
            LoadAsset("Data/value.candidate", previousType));
        Assert.Equal(1, GetProperty<int>(restoredAsset, "importerGeneration"));
        Assert.Equal("fail", GetProperty<string>(restoredAsset, "value"));
        Assert.True(AssetManager.TryGetInfo("Data/value.candidate", out AssetInfo? restoredInfo));
        Assert.Equal(AssetImportStatus.Imported, restoredInfo!.status);
    }

    [Fact]
    public void ExistingWritableImportFailureDoesNotBlockUnrelatedAssemblyCandidate()
    {
        Write("Data/value.candidate", "accepted");
        WriteCandidateImporterScript(generation: 1, rejectFailureToken: true);
        WriteUnrelatedCandidateScript(version: 1);
        ScriptCompilationResult initial = Compile();
        Assert.True(initial.success, FormatDiagnostics(initial));
        Assert.True(m_manager.ApplyPendingReload());

        Write("Data/value.candidate", "fail");
        AssetManager.Rescan();
        Assert.True(AssetManager.TryGetInfo("Data/value.candidate", out AssetInfo? failedInfo));
        Assert.Equal(AssetImportStatus.Failed, failedInfo!.status);

        WriteUnrelatedCandidateScript(version: 2);
        ScriptCompilationResult candidate = Compile();
        Assert.True(candidate.success, FormatDiagnostics(candidate));
        Assert.True(m_manager.ApplyPendingReload());

        Assert.Equal(2, ReadVersion(ResolveTypeByName("UnrelatedCandidateBehavior")));
        Assert.True(AssetManager.TryGetInfo("Data/value.candidate", out AssetInfo? retainedFailure));
        Assert.Equal(AssetImportStatus.Failed, retainedFailure!.status);
        Assert.Contains(
            retainedFailure.diagnostics,
            static diagnostic => diagnostic.Contains(
                "Injected candidate importer failure",
                StringComparison.Ordinal));
    }

    [Fact]
    public void RepeatedReloadReleasesTypeCachesObserversLoadedAssetsAndPanelReloadMemory()
    {
        Write("Data/Reload.rasset", "payload");
        WriteReloadRetentionScripts(generation: 1);
        ScriptCompilationResult initial = Compile();
        Assert.True(initial.success, FormatDiagnostics(initial));
        Assert.True(m_manager.ApplyPendingReload());
        using var runtime = new EditorInteractionRuntime(m_projectRoot);
        runtime.Start();
        AssetManager.Rescan();
        ReloadGenerationReferences firstGeneration = CaptureReloadGeneration(runtime, generation: 1);

        WriteReloadRetentionScripts(generation: 2);
        ScriptCompilationResult second = Compile();
        Assert.True(second.success, FormatDiagnostics(second));
        Action<IIdentityObject> failingIdentityObserver = value =>
        {
            if (value.GetType().Name == "ReloadAsset")
                throw new InvalidOperationException("Injected retired asset identity observer failure.");
        };
        IdentityManager.ObjectUnregistered += failingIdentityObserver;
        try
        {
            Assert.True(m_manager.ApplyPendingReload());
        }
        finally
        {
            IdentityManager.ObjectUnregistered -= failingIdentityObserver;
        }
        ForceCollection();
        Assert.False(firstGeneration.asset.IsAlive, "The retired canonical asset is still strongly reachable.");
        Assert.False(firstGeneration.panel.IsAlive, "The retired editor panel is still strongly reachable.");
        Assert.False(firstGeneration.observerType.IsAlive, "The retired static event observer type is still strongly reachable.");
        Assert.False(firstGeneration.context.IsAlive, "The first collectible load context is still strongly reachable.");

        AssetManager.Rescan();
        ReloadGenerationReferences secondGeneration = CaptureReloadGeneration(runtime, generation: 2);
        WriteReloadRetentionScripts(generation: 3);
        ScriptCompilationResult third = Compile();
        Assert.True(third.success, FormatDiagnostics(third));
        Assert.True(m_manager.ApplyPendingReload());
        ForceCollection();
        Assert.False(secondGeneration.asset.IsAlive, "The second retired canonical asset is still strongly reachable.");
        Assert.False(secondGeneration.panel.IsAlive, "The second retired editor panel is still strongly reachable.");
        Assert.False(secondGeneration.observerType.IsAlive, "The second retired observer type is still strongly reachable.");
        Assert.False(secondGeneration.context.IsAlive, "The second collectible load context is still strongly reachable.");
    }

    [Fact]
    public void DisposeReleasesTheActiveScriptGenerationAndItsEngineOwnedReferences()
    {
        Write("Data/Reload.rasset", "payload");
        WriteReloadRetentionScripts(generation: 1);
        ScriptCompilationResult initial = Compile();
        Assert.True(initial.success, FormatDiagnostics(initial));
        Assert.True(m_manager.ApplyPendingReload());
        using var runtime = new EditorInteractionRuntime(m_projectRoot);
        runtime.Start();
        AssetManager.Rescan();
        ReloadGenerationReferences generation = CaptureReloadGeneration(runtime, generation: 1);

        m_manager.Dispose();
        ForceCollection();

        Assert.False(generation.asset.IsAlive);
        Assert.False(generation.panel.IsAlive);
        Assert.False(generation.observerType.IsAlive);
        Assert.False(generation.context.IsAlive);
    }

    [Fact]
    public void GameScriptsCanUseGameObjectTagsAndSceneTagQueriesThroughTheFacade()
    {
        Write("TagFacadeProbe.cs", """
            using InnoEngine.Scene;

            public static class TagFacadeProbe
            {
                public static GameObject? FindPlayer(GameScene scene)
                {
                    GameObject gameObject = scene.CreateObject("Player");
                    gameObject.tag = "Player";
                    _ = GameObject.defaultTag;
                    _ = scene.FindObjectsWithTag("Player");
                    return scene.FindObjectWithTag("Player");
                }
            }
            """);

        ScriptCompilationResult result = Compile();

        Assert.True(result.success, FormatDiagnostics(result));
    }

    [Fact]
    public void InspectorTagCatalogRoundTripsThroughReadableModuleStateAndProtectsTheDefaultTag()
    {
        Type catalogType = typeof(AssetReferenceDropTarget).Assembly.GetType(
            "Inno.Editor.Panel.Inspector.GameObjectTagCatalog",
            throwOnError: true)!;
        object catalog = Activator.CreateInstance(
            catalogType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            args: null,
            culture: null)!;
        MethodInfo add = catalogType.GetMethod(
            "Add",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        MethodInfo remove = catalogType.GetMethod(
            "Remove",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        MethodInfo restore = catalogType.GetMethod(
            "Restore",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        MethodInfo getTags = catalogType.GetMethod(
            "GetTags",
            BindingFlags.Instance | BindingFlags.NonPublic)!;

        Assert.True((bool)add.Invoke(catalog, ["Player"])!);
        Assert.False((bool)add.Invoke(catalog, [" Player "])!);
        Assert.False((bool)remove.Invoke(catalog, [GameObject.defaultTag])!);

        string payload = JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["tags"] = getTags.Invoke(catalog, null)
        });
        Assert.Contains("\"tags\"", payload, StringComparison.Ordinal);
        Assert.Contains(GameObject.defaultTag, payload, StringComparison.Ordinal);
        Assert.Contains("Player", payload, StringComparison.Ordinal);

        object restored = Activator.CreateInstance(
            catalogType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            args: null,
            culture: null)!;
        string[] restoredValues = JsonSerializer
            .Deserialize<Dictionary<string, string[]>>(payload)!["tags"];
        _ = restore.Invoke(restored, [restoredValues]);
        var restoredTags = Assert.IsAssignableFrom<System.Collections.Generic.IReadOnlyList<string>>(
            getTags.Invoke(restored, null));
        Assert.Contains(GameObject.defaultTag, restoredTags);
        Assert.Contains("Player", restoredTags);

        Assert.True((bool)remove.Invoke(catalog, ["Player"])!);
        Assert.DoesNotContain("Player", Assert.IsAssignableFrom<System.Collections.Generic.IReadOnlyList<string>>(
            getTags.Invoke(catalog, null)));
    }

    [Fact]
    public void SceneWorkspaceSaveRenamesTheExistingSceneAsset()
    {
        var workspace = new ReflectedSceneWorkspace();
        GameScene scene = workspace.CreateScene();
        scene.name = "Original";
        string originalPath = workspace.Save(scene, "Scenes");
        Assert.True(AssetManager.TryGetPersistentId(originalPath, out Guid persistentId));

        scene.name = "Renamed";
        Assert.True(workspace.IsDirty(scene));
        string renamedPath = workspace.Save(scene, "Scenes");

        Assert.Equal("Scenes/Renamed.iscene", renamedPath);
        Assert.False(AssetManager.TryGetFileSystemEntry(originalPath, out _));
        Assert.True(AssetManager.TryGetPersistentId(renamedPath, out Guid renamedId));
        Assert.Equal(persistentId, renamedId);
        Assert.False(workspace.IsDirty(scene));
    }

    [Fact]
    public void SceneAssetRenameUpdatesTheLoadedDocumentWithoutMarkingItDirty()
    {
        var workspace = new ReflectedSceneWorkspace();
        var context = new EditorContext(m_projectRoot);
        workspace.Start(context);
        try
        {
            GameScene scene = workspace.CreateScene();
            scene.name = "TestScene";
            string sourcePath = workspace.Save(scene, "Scenes");
            Assert.False(workspace.IsDirty(scene));

            scene.name = "Temporary Name";
            Assert.True(workspace.IsDirty(scene));
            scene.name = "TestScene";

            const string renamedPath = "Scenes/TestScene1.iscene";
            AssetManager.Move(sourcePath, renamedPath);
            Assert.False(workspace.IsDirty(scene));
            workspace.Update(context);

            Assert.Equal("TestScene1", scene.name);
            Assert.True(workspace.TryGetSourcePath(scene, out string synchronizedPath));
            Assert.Equal(renamedPath, synchronizedPath);
            Assert.False(workspace.IsDirty(scene));
        }
        finally
        {
            workspace.Stop(context);
        }
    }

    [Fact]
    public void MovingALoadedSceneDirectoryDoesNotMarkTheSceneDirty()
    {
        var workspace = new ReflectedSceneWorkspace();
        var context = new EditorContext(m_projectRoot);
        workspace.Start(context);
        try
        {
            GameScene scene = workspace.CreateScene();
            scene.name = "MovedScene";
            _ = workspace.Save(scene, "Scenes/Nested");
            Assert.False(workspace.IsDirty(scene));

            AssetManager.Move("Scenes", "ArchivedScenes");
            Assert.False(workspace.IsDirty(scene));
            workspace.Update(context);

            Assert.True(workspace.TryGetSourcePath(scene, out string synchronizedPath));
            Assert.Equal("ArchivedScenes/Nested/MovedScene.iscene", synchronizedPath);
            Assert.Equal("MovedScene", scene.name);
            Assert.False(workspace.IsDirty(scene));
        }
        finally
        {
            workspace.Stop(context);
        }
    }

    [Fact]
    public void MovingALoadedSceneDirectoryPreservesAnExistingDirtyState()
    {
        var workspace = new ReflectedSceneWorkspace();
        var context = new EditorContext(m_projectRoot);
        workspace.Start(context);
        try
        {
            GameScene scene = workspace.CreateScene();
            scene.name = "DirtyMovedScene";
            _ = workspace.Save(scene, "Scenes/Nested");
            _ = scene.CreateObject("UnsavedObject");
            Thread.Sleep(150);
            Assert.True(workspace.IsDirty(scene));

            AssetManager.Move("Scenes", "ArchivedScenes");
            workspace.Update(context);

            Assert.True(workspace.TryGetSourcePath(scene, out string synchronizedPath));
            Assert.Equal("ArchivedScenes/Nested/DirtyMovedScene.iscene", synchronizedPath);
            Assert.True(workspace.IsDirty(scene));
        }
        finally
        {
            workspace.Stop(context);
        }
    }

    [Fact]
    public void FileBrowserDropOfALoadedSceneDoesNotMarkTheSceneDirty()
    {
        using var runtime = new EditorInteractionRuntime(m_projectRoot);
        runtime.Start();
        var workspace = new ReflectedSceneWorkspace(runtime.interactions);
        var context = new EditorContext(m_projectRoot);
        workspace.Start(context);
        try
        {
            AssetManager.CreateDirectory("Destination");
            GameScene scene = workspace.CreateScene();
            scene.name = "DraggedScene";
            string sourcePath = workspace.Save(scene, "Source");
            Assert.False(workspace.IsDirty(scene));
            Assert.True(AssetManager.TryGetInfo(sourcePath, out AssetInfo? sceneInfo));
            Assert.NotNull(sceneInfo);

            Guid token = runtime.interactions
                .For("panel/asset.file-browser", sceneInfo)
                .BeginDrag(new EditorDragData(sceneInfo!, "DraggedScene.iscene"));
            EditorDropResult drop = runtime.interactions
                .For("panel/asset.file-browser", "Destination")
                .Drop(token, EditorDropPlacement.Into);
            Assert.True(drop.accepted);
            Assert.False(workspace.IsDirty(scene));
            workspace.Update(context);

            Assert.True(workspace.TryGetSourcePath(scene, out string synchronizedPath));
            Assert.Equal("Destination/DraggedScene.iscene", synchronizedPath);
            Assert.False(workspace.IsDirty(scene));
        }
        finally
        {
            workspace.Stop(context);
        }
    }

    [Fact]
    public void FileBrowserSceneAssetRelocationDoesNotDirtyLoadedScenesThatReferenceIt()
    {
        using var runtime = new EditorInteractionRuntime(m_projectRoot);
        runtime.Start();
        var workspace = new ReflectedSceneWorkspace(runtime.interactions);
        var context = new EditorContext(m_projectRoot);
        workspace.Start(context);
        try
        {
            AssetManager.CreateDirectory("Destination");
            GameScene referencedScene = workspace.CreateScene();
            referencedScene.name = "Referenced";
            string referencedPath = workspace.Save(referencedScene, "References");
            Assert.True(workspace.CloseScene(referencedScene));
            SceneAsset referencedAsset = AssetManager.Load<SceneAsset>(referencedPath);

            GameScene hostScene = workspace.CreateScene();
            hostScene.name = "Host";
            hostScene.CreateObject("Reference")
                .AddComponent<SceneAssetDirtyProbe>()
                .sceneAsset = referencedAsset;
            _ = workspace.Save(hostScene, "Scenes");
            Assert.False(workspace.IsDirty(hostScene));
            Assert.True(AssetManager.TryGetInfo(referencedPath, out AssetInfo? sceneInfo));
            Assert.NotNull(sceneInfo);

            Guid token = runtime.interactions
                .For("panel/asset.file-browser", sceneInfo)
                .BeginDrag(new EditorDragData(sceneInfo!, "Referenced.iscene"));
            EditorDropResult drop = runtime.interactions
                .For("panel/asset.file-browser", "Destination")
                .Drop(token, EditorDropPlacement.Into);

            Assert.True(drop.accepted);
            Assert.Equal("Destination/Referenced.iscene", referencedAsset.assetPath.ToString());
            Assert.False(workspace.IsDirty(hostScene));

            AssetManager.Move("Destination/Referenced.iscene", "Destination/Renamed.iscene");

            Assert.Equal("Destination/Renamed.iscene", referencedAsset.assetPath.ToString());
            Assert.False(workspace.IsDirty(hostScene));
        }
        finally
        {
            workspace.Stop(context);
        }
    }

    [Theory]
    [InlineData("Player.iscene", false, "Player", "Renamed", "Renamed.iscene")]
    [InlineData("Tool.editor.cs", false, "Tool.editor", "Renamed", "Renamed.cs")]
    [InlineData("Tool.editor.cs", false, "Tool.editor", "Renamed.editor", "Renamed.editor.cs")]
    [InlineData("Tool.cs", false, "Tool", "Renamed.txt", "Renamed.txt.cs")]
    [InlineData("Folder.with.dot", true, "Folder.with.dot", "Renamed.folder", "Renamed.folder")]
    public void FileBrowserRenameEditsOnlyTheNameAndPreservesTheFinalExtension(
        string sourceName,
        bool isDirectory,
        string expectedEditableName,
        string editedName,
        string expectedResult)
    {
        Type utility = typeof(AssetEditor).Assembly.GetType(
            "Inno.Editor.Panel.FileBrowser.FileBrowserUtility",
            throwOnError: true)!;
        MethodInfo getEditableName = utility.GetMethod(
            "GetEditableName",
            BindingFlags.Static | BindingFlags.NonPublic)!;
        MethodInfo composeRenamedEntryName = utility.GetMethod(
            "ComposeRenamedEntryName",
            BindingFlags.Static | BindingFlags.NonPublic)!;

        Assert.Equal(
            expectedEditableName,
            getEditableName.Invoke(null, [sourceName, isDirectory]));
        Assert.Equal(
            expectedResult,
            composeRenamedEntryName.Invoke(null, [sourceName, editedName, isDirectory]));
    }

    [Fact]
    public void GlobalSaveCreatesUnsavedScenesInTheOpenAssetDirectory()
    {
        _ = Assembly.Load(typeof(HierarchyObjectDropTarget).Assembly.GetName());
        TypeCacheManager.Rebuild();
        AssetManager.CreateDirectory("Open Folder");
        var editorContext = new EditorContext(m_projectRoot);
        editorContext.SetLayoutSection(
            "Module.asset-browser",
            new System.Collections.Generic.Dictionary<string, string>
            {
                ["currentDirectory"] = "\"Open Folder\""
            });

        using var runtime = new EditorInteractionRuntime(editorContext);
        runtime.Start();
        var workspace = new ReflectedSceneWorkspace(runtime.interactions);
        GameScene scene = workspace.CreateScene();
        scene.name = "Current Directory Scene";

        Assert.True(runtime.interactions
            .For("editor/main-menu")
            .Execute("editor/save"));
        Assert.True(AssetManager.TryGetFileSystemEntry(
            "Open Folder/Current Directory Scene.iscene",
            out _));
        Assert.False(AssetManager.TryGetFileSystemEntry(
            "Current Directory Scene.iscene",
            out _));
    }

    [Fact]
    public void SettingsMainMenuEntryOpensTheSettingsModal()
    {
        using var runtime = new EditorInteractionRuntime(new EditorContext(m_projectRoot));
        runtime.Start();
        EditorModalExtension modal = Assert.Single(
            runtime.modals,
            static extension => extension.id == "editor.settings");
        Assert.True(modal.TryGetPresentation(out EditorModalExtension.Presentation initialPresentation));
        Assert.False(initialPresentation.isVisible);
        Assert.True(initialPresentation.blocksInteraction);
        Assert.True(initialPresentation.canMove);
        Assert.True(initialPresentation.canResize);
        Assert.True(initialPresentation.initialSize.X > initialPresentation.minimumSize.X);
        Assert.True(initialPresentation.initialSize.Y > initialPresentation.minimumSize.Y);
        EditorMenuModel menu = runtime.interactions.For("editor/main-menu").BuildMenu();
        EditorMenuItem edit = Assert.Single(
            menu.items,
            static item => item.label == "Edit");
        Assert.Contains(edit.children, static item => item.label == "Settings...");

        Assert.True(runtime.interactions
            .For("editor/main-menu")
            .Execute("editor.settings.open"));

        Assert.True(modal.TryGetPresentation(out EditorModalExtension.Presentation openPresentation));
        Assert.True(openPresentation.isVisible);
    }

    [Fact]
    public void GlobalZoomRestoresAndRespondsToMenuAndKeyboardCommands()
    {
        var editorContext = new EditorContext(m_projectRoot);
        string settingsPath = Path.Combine(m_projectRoot, "EditorSettings.json");
        File.WriteAllText(
            settingsPath,
            "{\"Global/Appearance/Accessibility/Actual Size\":{\"value\":1.2}}");

        try
        {
            using var runtime = new EditorInteractionRuntime(editorContext);
            runtime.Start();
            Assert.Equal(1.2f, EditorWidget.style.zoom, 3);
            KeyModifier primary = OperatingSystem.IsMacOS()
                ? KeyModifier.Super
                : KeyModifier.Control;
            EditorInteraction mainMenu = runtime.interactions.For("editor/main-menu");
            Assert.True(mainMenu.TryGetShortcut(
                "editor.ui.zoom-in",
                out HotKeyGesture zoomInGesture));
            Assert.Equal(KeyCode.Plus, zoomInGesture.key);
            Assert.Equal(primary, zoomInGesture.modifiers);

            runtime.HandleKeyPressed(new KeyPressedEvent(
                windowId: 0,
                KeyCode.Plus,
                primary | KeyModifier.Shift));
            Assert.Equal(1.32f, EditorWidget.style.zoom, 3);

            Assert.True(runtime.interactions
                .For("editor/main-menu")
                .Execute("editor.ui.zoom-out"));
            Assert.Equal(1.2f, EditorWidget.style.zoom, 3);

            runtime.HandleKeyPressed(new KeyPressedEvent(
                windowId: 0,
                KeyCode.D0,
                primary));
            Assert.Equal(1.2f, EditorWidget.style.zoom, 3);

            Assert.Contains(
                "Global/Appearance/Accessibility/Actual Size",
                File.ReadAllText(settingsPath));
        }
        finally
        {
            _ = EditorWidget.style.ResetZoom();
        }
    }

    [Fact]
    public void FileBrowserSplitterDefaultsToCenterAndRestoresNormalizedRatio()
    {
        var editorContext = new EditorContext(m_projectRoot);
        using (var runtime = new EditorInteractionRuntime(editorContext))
        {
            runtime.Start();
            runtime.SaveState();
        }

        Assert.True(editorContext.TryGetLayoutSection(
            "Panel.asset.file-browser",
            out System.Collections.Generic.IReadOnlyDictionary<string, string> defaultValues));
        Assert.Equal("0.5", defaultValues["treePaneRatio"]);
        Assert.DoesNotContain("treeWidth", defaultValues.Keys);

        editorContext.SetLayoutSection(
            "Panel.asset.file-browser",
            new System.Collections.Generic.Dictionary<string, string>
            {
                ["treePaneRatio"] = "0.31"
            });
        using (var runtime = new EditorInteractionRuntime(editorContext))
        {
            runtime.Start();
            runtime.SaveState();
        }

        Assert.True(editorContext.TryGetLayoutSection(
            "Panel.asset.file-browser",
            out System.Collections.Generic.IReadOnlyDictionary<string, string> restoredValues));
        Assert.Equal("0.31", restoredValues["treePaneRatio"]);
    }

    [Fact]
    public void MissingLayerSettingsUseDefaultAndReportOnlyUndefinedLoadedAssignments()
    {
        _ = Assembly.Load(typeof(AssetReferenceDropTarget).Assembly.GetName());
        TypeCacheManager.Rebuild();
        var sink = new TestDiagnosticSink();
        DiagnosticManager.RegisterSink(sink);
        try
        {
            using var runtime = new EditorInteractionRuntime(m_projectRoot);
            runtime.Start();
            var workspace = new ReflectedSceneWorkspace(runtime.interactions);
            GameObject gameObject = workspace.CreateScene().CreateObject("Undefined GameLayer Object");
            gameObject.layer = new GameLayer(7);

            Assert.True(UpdateUntil(
                runtime,
                () => sink.ContainsCode("GAMEOBJECT-LAYER-UNDEFINED")));

            gameObject.layer = GameLayer.defaultLayer;
            Assert.True(UpdateUntil(
                runtime,
                () => !sink.ContainsCode("GAMEOBJECT-LAYER-UNDEFINED")));
        }
        finally
        {
            DiagnosticManager.UnregisterSink(sink);
        }
    }

    [Fact]
    public void GameLayersPersistThroughEditorSettingsWithoutAssetRegistration()
    {
        _ = Assembly.Load(typeof(AssetReferenceDropTarget).Assembly.GetName());
        TypeCacheManager.Rebuild();
        const string path = "Project/Layers/Game Layers";
        using (EditorInteractionRuntime runtime = CreateSettingsRuntime(out EditorSettings settings))
        {
            EditorSettingObject value = settings.Get(path);
            string?[] names = value.GetAsStringArray("names");
            uint[] masks = value.GetAsUInt32Array("interactionMasks");
            names[7] = "Gameplay";
            masks[7] &= ~(1u << 8);
            masks[8] &= ~(1u << 7);
            value.SetAsStringArray("names", names);
            value.SetAsUInt32Array("interactionMasks", masks);
            Assert.True(settings.Apply(
                new Dictionary<string, EditorSettingObject>(StringComparer.Ordinal)
                {
                    [path] = value
                }));
        }

        string documentPath = Path.Combine(m_projectRoot, "EditorSettings.json");
        Assert.True(File.Exists(documentPath));
        Assert.Contains(path, File.ReadAllText(documentPath), StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(m_projectRoot, "Assets", "Settings")));
        using EditorInteractionRuntime restoredRuntime = CreateSettingsRuntime(out EditorSettings restored);
        EditorSettingObject restoredValue = restored.Get(path);
        Assert.Equal("Gameplay", restoredValue.GetAsStringArray("names")[7]);
        Assert.Equal(0u, restoredValue.GetAsUInt32Array("interactionMasks")[7] & (1u << 8));
        string?[] detachedNames = restoredValue.GetAsStringArray("names");
        detachedNames[9] = "Detached";
        Assert.Null(restored.Get(path).GetAsStringArray("names")[9]);
    }

    [Fact]
    public void SceneEditingCommandsUndoAndRedoComponentsSystemsAndRecreatedTargets()
    {
        using var runtime = new EditorInteractionRuntime(m_projectRoot);
        runtime.Start();
        Assert.True(runtime.interactions
            .For("panel/scene.hierarchy")
            .Execute("hierarchy/create-scene"));
        GameScene scene = SceneManager.activeScene!;
        GameObject gameObject = scene.CreateObject("History Target");
        Guid sceneId = scene.identity.persistentId;
        Guid gameObjectId = gameObject.identity.persistentId;

        Assert.True(runtime.interactions
            .For("panel/scene.inspector/component", gameObject)
            .Execute(
                "inspector/add-component",
                TypeCacheManager.GetTypeRef(typeof(HistoryTestComponent))));
        Assert.NotNull(gameObject.GetComponent<HistoryTestComponent>());

        Assert.True(runtime.interactions
            .For("panel/scene.inspector/system", scene)
            .Execute(
                "inspector/add-system",
                TypeCacheManager.GetTypeRef(typeof(HistoryTestSystem))));
        Assert.Single(scene.GetSystems().OfType<HistoryTestSystem>());

        Assert.True(runtime.interactions.history.Undo().succeeded);
        GameScene currentScene = IdentityManager.Get<GameScene>(sceneId)!;
        GameObject currentObject = IdentityManager.Get<GameObject>(gameObjectId)!;
        Assert.Empty(currentScene.GetSystems().OfType<HistoryTestSystem>());
        Assert.NotNull(currentObject.GetComponent<HistoryTestComponent>());

        Assert.True(runtime.interactions.history.Undo().succeeded);
        currentObject = IdentityManager.Get<GameObject>(gameObjectId)!;
        Assert.False(currentObject.HasComponent<HistoryTestComponent>());

        Assert.True(runtime.interactions.history.Redo().succeeded);
        currentObject = IdentityManager.Get<GameObject>(gameObjectId)!;
        Assert.NotNull(currentObject.GetComponent<HistoryTestComponent>());

        Assert.True(runtime.interactions.history.Redo().succeeded);
        currentScene = IdentityManager.Get<GameScene>(sceneId)!;
        HistoryTestSystem currentSystem = Assert.Single(currentScene.GetSystems().OfType<HistoryTestSystem>());

        currentObject = IdentityManager.Get<GameObject>(gameObjectId)!;
        HistoryTestComponent currentComponent = currentObject.GetComponent<HistoryTestComponent>();
        currentComponent.value = 42;
        Guid componentId = currentComponent.identity.persistentId;
        Assert.True(runtime.interactions
            .For(
                "panel/scene.inspector/component",
                CreateInspectorTarget(
                    "Inno.Editor.Panel.Inspector.ComponentEditorTarget",
                    currentObject,
                    currentComponent))
            .Execute("inspector/reset-component"));
        Assert.Equal(7, IdentityManager.Get<HistoryTestComponent>(componentId)!.value);
        Assert.True(runtime.interactions.history.Undo().succeeded);
        Assert.Equal(42, IdentityManager.Get<HistoryTestComponent>(componentId)!.value);
        Assert.True(runtime.interactions.history.Redo().succeeded);
        Assert.Equal(7, IdentityManager.Get<HistoryTestComponent>(componentId)!.value);

        currentSystem = IdentityManager.Get<HistoryTestSystem>(currentSystem.identity.persistentId)!;
        currentSystem.value = 84;
        Guid systemId = currentSystem.identity.persistentId;
        Assert.True(runtime.interactions
            .For(
                "panel/scene.inspector/system",
                CreateInspectorTarget(
                    "Inno.Editor.Panel.Inspector.SystemEditorTarget",
                    currentScene,
                    currentSystem))
            .Execute("inspector/remove-system"));
        Assert.Null(IdentityManager.Get<HistoryTestSystem>(systemId));
        Assert.True(runtime.interactions.history.Undo().succeeded);
        Assert.Equal(84, IdentityManager.Get<HistoryTestSystem>(systemId)!.value);
        Assert.True(runtime.interactions.history.Redo().succeeded);
        Assert.Null(IdentityManager.Get<HistoryTestSystem>(systemId));
    }

    [Fact]
    public void NeutralSceneHistorySurvivesAdjacentDocumentUndoAndRedo()
    {
        using var runtime = new EditorInteractionRuntime(m_projectRoot);
        runtime.Start();
        Assert.True(runtime.interactions
            .For("panel/scene.hierarchy")
            .Execute("hierarchy/create-scene"));
        GameScene scene = SceneManager.activeScene!;
        Guid sceneId = scene.identity.persistentId;
        string originalName = scene.name;
        var edits = (SceneEdits)ScriptingTestReflection.Create(
            typeof(SceneEdits),
            new ReflectedSceneWorkspace(runtime.interactions).instance,
            runtime.interactions);
        edits.RenameScene(scene, "Renamed");

        Assert.True(runtime.interactions
            .For("panel/scene.hierarchy")
            .Execute("hierarchy/create-scene"));
        Assert.Equal(2, SceneManager.loadedScenes.Count);
        Assert.True(runtime.interactions.history.Undo().succeeded);
        Assert.Single(SceneManager.loadedScenes);

        Assert.True(runtime.interactions.history.Undo().succeeded);
        Assert.Equal(originalName, IdentityManager.Get<GameScene>(sceneId)!.name);
        Assert.True(runtime.interactions.history.Redo().succeeded);
        Assert.Equal("Renamed", IdentityManager.Get<GameScene>(sceneId)!.name);
    }

    [Fact]
    public void NeutralHistoryUsesTheNewEditorScriptHandlerGenerationAfterReload()
    {
        WriteHistoryHandler(generation: 1);
        ScriptCompilationResult firstCompilation = Compile();
        Assert.True(firstCompilation.success, FormatDiagnostics(firstCompilation));
        Assert.True(m_manager.ApplyPendingReload());
        using var runtime = new EditorInteractionRuntime(m_projectRoot);
        runtime.Start();
        runtime.interactions.history.RecordApplied(
            "Script History",
            new EditorHistoryChange(
                "tests.script-history",
                EditorHistoryPayload.FromBytes([1])));

        WriteHistoryHandler(generation: 2);
        ScriptCompilationResult secondCompilation = Compile();
        Assert.True(secondCompilation.success, FormatDiagnostics(secondCompilation));
        Assert.True(m_manager.ApplyPendingReload());
        _ = runtime.panelCount;

        Assert.True(runtime.interactions.history.canUndo);
        Assert.True(runtime.interactions.history.Undo().succeeded);
        Assert.Equal(
            "2:Undo",
            File.ReadAllText(Path.Combine(m_projectRoot, "history-handler.txt")));
        Assert.True(runtime.interactions.history.Redo().succeeded);
        Assert.Equal(
            "2:Redo",
            File.ReadAllText(Path.Combine(m_projectRoot, "history-handler.txt")));
    }

    [Fact]
    public void SceneWorkspaceOpenSceneLoadsAdditivelyAtTheBottom()
    {
        var workspace = new ReflectedSceneWorkspace();
        GameScene source = workspace.CreateScene();
        source.name = "Additive";
        string sourcePath = workspace.Save(source, "Scenes");
        Assert.True(SceneManager.UnloadScene(source));
        GameScene existing = workspace.CreateScene();

        GameScene opened = workspace.Open(sourcePath);

        Assert.Equal([existing, opened], SceneManager.loadedScenes);
        Assert.Same(opened, SceneManager.activeScene);
        Assert.Equal("Additive", opened.name);
    }

    [Fact]
    public void SceneWorkspaceCloseSceneRemovesOnlyTheLoadedInstance()
    {
        var workspace = new ReflectedSceneWorkspace();
        GameScene first = workspace.CreateScene();
        GameScene second = workspace.CreateScene();

        Assert.True(workspace.CloseScene(second));

        Assert.Equal([first], SceneManager.loadedScenes);
        Assert.Same(first, SceneManager.activeScene);
        Assert.True(second.isDestroyed);
        Assert.False(workspace.CloseScene(second));
        Assert.True(workspace.CloseScene(first));
        Assert.Empty(SceneManager.loadedScenes);
        Assert.Null(SceneManager.activeScene);
        Assert.True(first.isDestroyed);
    }

    [Fact]
    public void SceneWorkspaceDoesNotCreateADefaultSceneForEmptyState()
    {
        var workspace = new ReflectedSceneWorkspace();
        var context = new EditorContext(m_projectRoot);
        workspace.Start(context);

        RestoreExtensionState(workspace.module, new TestEditorState());
        workspace.Update(context);

        Assert.Empty(SceneManager.loadedScenes);
        Assert.Null(workspace.activeScene);
        workspace.Stop(context);
    }

    [Fact]
    public void SceneModuleStateRestoresSavedScenesInOrderAndSelectsTheActiveScene()
    {
        var sourceWorkspace = new ReflectedSceneWorkspace();
        GameScene first = sourceWorkspace.CreateScene();
        first.name = "First";
        _ = sourceWorkspace.Save(first, "Scenes");
        GameScene second = sourceWorkspace.CreateScene();
        second.name = "Second";
        _ = sourceWorkspace.Save(second, "Scenes");
        SceneManager.SetActiveScene(first);
        TestEditorState state = CaptureExtensionState(sourceWorkspace.module);

        SceneManager.UnloadAllScenes();
        var restoredWorkspace = new ReflectedSceneWorkspace();
        var context = new EditorContext(m_projectRoot);
        restoredWorkspace.Start(context);
        RestoreExtensionState(restoredWorkspace.module, state);
        restoredWorkspace.Update(context);

        Assert.Equal(["First", "Second"], restoredWorkspace.scenes.Select(static scene => scene.name));
        Assert.Equal("First", restoredWorkspace.activeScene!.name);
        restoredWorkspace.Stop(context);
    }

    [Fact]
    public void SceneModuleStateReloadsLastSavedDataInsteadOfDirtyMemory()
    {
        var sourceWorkspace = new ReflectedSceneWorkspace();
        GameScene scene = sourceWorkspace.CreateScene();
        scene.name = "Saved";
        _ = scene.CreateObject("Saved Object");
        _ = sourceWorkspace.Save(scene, "Scenes");
        _ = scene.CreateObject("Unsaved Object");
        TestEditorState state = CaptureExtensionState(sourceWorkspace.module);

        SceneManager.UnloadAllScenes();
        var restoredWorkspace = new ReflectedSceneWorkspace();
        var context = new EditorContext(m_projectRoot);
        restoredWorkspace.Start(context);
        RestoreExtensionState(restoredWorkspace.module, state);
        restoredWorkspace.Update(context);

        Assert.NotNull(restoredWorkspace.activeScene!.FindObject("Saved Object"));
        Assert.Null(restoredWorkspace.activeScene!.FindObject("Unsaved Object"));
        restoredWorkspace.Stop(context);
    }

    [Fact]
    public void EditorRuntimePersistsAndRestoresOpenScenesFromReadableProjectSettings()
    {
        using (var runtime = new EditorInteractionRuntime(m_projectRoot))
        {
            runtime.Start();
            runtime.Update(new EditorFrame(0.016f, 1f, isFocused: true));
            Assert.Empty(SceneManager.loadedScenes);
            Assert.True(runtime.interactions
                .For("panel/scene.hierarchy")
                .Execute("hierarchy/create-scene"));
            GameScene first = SceneManager.activeScene!;
            first.name = "Workspace First";
            Assert.True(runtime.interactions
                .For("panel/scene.hierarchy")
                .Execute("hierarchy/create-scene"));
            SceneManager.activeScene!.name = "Workspace Second";
            Assert.True(runtime.interactions
                .For("panel/scene.hierarchy")
                .Execute("editor/save"));

        }

        string settings = File.ReadAllText(Path.Combine(m_projectRoot, "editor.ini"));
        Assert.Contains("[InnoEditor][Module.scene-workspace]", settings);
        Assert.Contains(
            "openScenes=[\"Workspace First.iscene\",\"Workspace Second.iscene\"]",
            settings);
        Assert.DoesNotContain("Payload=", settings);
        Assert.Empty(SceneManager.loadedScenes);

        using var restored = new EditorInteractionRuntime(m_projectRoot);
        restored.Start();
        float totalTime = 1f;
        long deadline = Environment.TickCount64 + 10_000;
        while (Environment.TickCount64 < deadline)
        {
            restored.Update(new EditorFrame(0.016f, totalTime, isFocused: true));
            if (SceneManager.loadedScenes.Select(static scene => scene.name).SequenceEqual(
                    ["Workspace First", "Workspace Second"]))
            {
                break;
            }
            totalTime += 0.016f;
            System.Threading.Thread.Sleep(10);
        }

        Assert.Equal(
            ["Workspace First", "Workspace Second"],
            SceneManager.loadedScenes.Select(static scene => scene.name));
        Assert.Equal("Workspace Second", SceneManager.activeScene!.name);
    }

    [Fact]
    public void CachedCompilationReplaysCompilerWarnings()
    {
        Write(
            "CachedWarningProbe.cs",
            "public sealed class CachedWarningProbe { private int unused; }");

        ScriptCompilationResult compiled = Compile();
        Assert.True(compiled.success, FormatDiagnostics(compiled));
        ScriptDiagnostic warning = Assert.Single(compiled.diagnostics, static diagnostic =>
            diagnostic.id == "CS0169" &&
            diagnostic.severity == ScriptDiagnosticSeverity.Warning);

        ScriptCompilationResult cached = Compile();

        Assert.True(cached.success, FormatDiagnostics(cached));
        ScriptDiagnostic cachedWarning = Assert.Single(cached.diagnostics, static diagnostic =>
            diagnostic.id == "CS0169" &&
            diagnostic.severity == ScriptDiagnosticSeverity.Warning);
        Assert.Equal(warning, cachedWarning);
        Assert.Equal(compiled.outputDirectory, cached.outputDirectory);
    }

    [Fact]
    public void HierarchyCanDeleteAndRestoreTheOnlyLoadedScene()
    {
        using var runtime = new EditorInteractionRuntime(m_projectRoot);
        runtime.Start();
        Assert.True(runtime.interactions
            .For("panel/scene.hierarchy")
            .Execute("hierarchy/create-scene"));
        GameScene scene = Assert.Single(SceneManager.loadedScenes);
        EditorMenuItem delete = Assert.Single(
            runtime.interactions.For("panel/scene.hierarchy", scene).BuildMenu().items,
            static item => item.actionId == "hierarchy/delete-scene");
        Assert.True(delete.status.isEnabled);

        Assert.True(runtime.interactions
            .For("panel/scene.hierarchy", scene)
            .Execute("hierarchy/delete-scene"));
        Assert.Empty(SceneManager.loadedScenes);
        Assert.Null(SceneManager.activeScene);

        Assert.True(runtime.interactions.history.Undo().succeeded);
        Assert.Single(SceneManager.loadedScenes);
    }

    [Fact]
    public void SuccessfulRecompileReplacesTheActiveTypeAndFailureKeepsIt()
    {
        WriteVersionedBehavior(1);
        ScriptCompilationResult firstCompilation = Compile();
        Assert.True(firstCompilation.success, FormatDiagnostics(firstCompilation));
        Assert.True(m_manager.ApplyPendingReload());
        Type first = ResolveVersionedBehavior();
        Assert.Equal(1, ReadVersion(first));
        int firstRuntimeId = TypeCacheManager.GetTypeRef(first).runtimeId;

        WriteVersionedBehavior(2);
        ScriptCompilationResult secondCompilation = Compile();
        Assert.True(secondCompilation.success, FormatDiagnostics(secondCompilation));
        Assert.True(m_manager.ApplyPendingReload());
        Type second = ResolveVersionedBehavior();
        Assert.NotSame(first, second);
        Assert.Equal(2, ReadVersion(second));
        int secondRuntimeId = TypeCacheManager.GetTypeRef(second).runtimeId;
        Assert.NotEqual(firstRuntimeId, secondRuntimeId);

        Write("VersionedBehavior.cs", "public sealed class VersionedBehavior : GameBehavior {");
        ScriptCompilationResult failed = Compile();
        Assert.False(failed.success);
        Assert.Contains(failed.diagnostics, diagnostic =>
            diagnostic.severity == ScriptDiagnosticSeverity.Error &&
            diagnostic.filePath?.EndsWith("VersionedBehavior.cs", StringComparison.Ordinal) == true &&
            diagnostic.line > 0);
        Assert.False(m_manager.ApplyPendingReload());
        Assert.Same(second, ResolveVersionedBehavior());
    }

    [Fact]
    public void FailedCompilationPreservesAnUnappliedSuccessfulCandidate()
    {
        WriteVersionedBehavior(1);
        ScriptCompilationResult successful = Compile();
        Assert.True(successful.success, FormatDiagnostics(successful));

        Write("VersionedBehavior.cs", "public sealed class VersionedBehavior : GameBehavior {");
        ScriptCompilationResult failed = Compile();

        Assert.False(failed.success);
        Assert.True(m_manager.ApplyPendingReload());
        Type applied = ResolveVersionedBehavior();
        Assert.Equal(1, ReadVersion(applied));
    }

    [Fact]
    public void GameScriptsCannotUseEditorProfileButEditorScriptsCan()
    {
        const string source = "using InnoEditor.Core; public sealed class EditorApiProbe { public EditorContext? context { get; set; } }";
        Write("EditorApiProbe.cs", source);

        ScriptCompilationResult runtimeResult = Compile();

        Assert.False(runtimeResult.success);
        Assert.Contains(runtimeResult.diagnostics, diagnostic => diagnostic.id == "CS0246");
        File.Delete(Path.Combine(m_projectRoot, "Assets", "EditorApiProbe.cs"));
        Write("EditorApiProbe.editor.cs", source);

        ScriptCompilationResult editorResult = Compile();

        Assert.True(editorResult.success, FormatDiagnostics(editorResult));
    }

    [Fact]
    public void EditorScriptsCanDeclareEveryInteractionExtensionKind()
    {
        Write("InteractionExtensions.editor.cs", """
            using InnoEditor.Assets;
            using InnoEditor.Core;
            using InnoEditor.Interactions;
            using InnoEngine.Assets;

            [EditorAction("tests.interactions.execute", "tests/interactions")]
            [EditorMenu("tests/interactions", "Tools/Create/Execute")]
            [AssetIcon(typeof(TextAsset), AssetIconKind.FileImage, priority: 100)]
            [AssetIcon(".binary-icon", AssetIconKind.FileAudio, priority: 100)]
            public sealed class ScriptAction : EditorAction
            {
                private int m_value;

                protected override void Execute(EditorActionContext context)
                {
                    int before = m_value;
                    m_value++;
                    byte[] data = new byte[sizeof(int) * 2];
                    System.BitConverter.GetBytes(before).CopyTo(data, 0);
                    System.BitConverter.GetBytes(m_value).CopyTo(data, sizeof(int));
                    context.history.RecordApplied(
                        "Change Script Value",
                        new EditorHistoryChange(
                            "tests.script-value",
                            EditorHistoryPayload.FromBytes(data)));
                }
            }

            [EditorHistoryHandler("tests.script-value")]
            public sealed class ScriptValueHistoryHandler : EditorHistoryHandler
            {
                protected override EditorHistoryAvailability Query(
                    EditorHistoryContext context,
                    EditorHistoryChange change,
                    EditorHistoryDirection direction)
                    => EditorHistoryAvailability.Available();

                protected override EditorHistoryResult Apply(
                    EditorHistoryContext context,
                    EditorHistoryChange change,
                    EditorHistoryDirection direction)
                    => EditorHistoryResult.Success();
            }

            [EditorMenuSource("tests/interactions.dynamic")]
            public sealed class ScriptMenuSource : EditorMenuSource
            {
                public override void Build(EditorMenuContext context, EditorMenuBuilder builder)
                    => builder.Add(
                        "Dynamic",
                        "tests.interactions.execute");
            }

            [AssetEditor(typeof(TextAsset))]
            public sealed class ScriptAssetEditor : AssetEditor
            {
                public override bool CanOpen(AssetEditorContext context) => true;
            }

            [EditorDrop("tests/interactions.drop")]
            public sealed class ScriptDrop : EditorDrop<string, string>
            {
                protected override EditorDropStatus Query(EditorDropContext<string, string> context)
                    => EditorDropStatus.Accept();

                protected override EditorDropResult Drop(EditorDropContext<string, string> context)
                    => EditorDropResult.Accepted();
            }

            [EditorPanel("tests.script-panel", "Script Panel", order: 900, defaultOpen: false)]
            public sealed class ScriptPanel : EditorPanel, IEditorPanelReloadState
            {
                protected override void OnDraw(EditorContext context)
                {
                }

                protected override void Capture(EditorState state)
                    => state.Set("open", isOpen);

                protected override void Restore(EditorState state)
                    => isOpen = state.Get("open", isOpen);

                public System.ReadOnlyMemory<byte> CaptureReloadState()
                    => System.ReadOnlyMemory<byte>.Empty;

                public void RestoreReloadState(System.ReadOnlyMemory<byte> state)
                {
                }
            }
            """);

        ScriptCompilationResult result = Compile();

        Assert.True(result.success, FormatDiagnostics(result));
        Assert.True(m_manager.ApplyPendingReload());
        Assert.True(ContainsType("ScriptAction"));
        Assert.True(ContainsType("ScriptAssetEditor"));
        Assert.True(ContainsType("ScriptDrop"));
        Assert.True(ContainsType("ScriptPanel"));
        Assert.True(ContainsType("ScriptValueHistoryHandler"));

        using IDisposable iconRegistry = CreateAssetIconRegistry();
        Assert.Equal(ResolveImGuiIcon("FileImage"), ResolveAssetIcon(iconRegistry, typeof(TextAsset)));
        Assert.Equal(
            ResolveImGuiIcon("FileAudio"),
            ResolveAssetIcon(iconRegistry, typeof(BinaryAsset), "Assets/Test.binary-icon"));
    }

    [Fact]
    public void ScriptSettingPathAppearsAndDisappearsWithItsGeneration()
    {
        Write("ProjectSettings.editor.cs", """
            using InnoEditor.Settings;

            [EditorSettingPath("Tests/Overlay/Show Overlay")]
            public sealed class ProjectOverlaySetting : EditorSetting
            {
                public override EditorSettingObject defaultValue => CreateDefault();

                public override string section => "Overlay";
                public override string description => "Controls a test overlay.";

                protected override void OnDraw(EditorSettingObject setting)
                {
                }

                private static EditorSettingObject CreateDefault()
                {
                    var result = new EditorSettingObject();
                    result.SetAsBoolean("value", true);
                    return result;
                }
            }
            """);

        ScriptCompilationResult initial = Compile();
        Assert.True(initial.success, FormatDiagnostics(initial));
        Assert.True(m_manager.ApplyPendingReload());
        Type settingType = ResolveTypeByName("ProjectOverlaySetting");
        Assert.NotNull(settingType.GetCustomAttribute<EditorSettingPathAttribute>());
        using EditorInteractionRuntime settingsRuntime = CreateSettingsRuntime(out EditorSettings settings);
        EditorSetting definition = Assert.Single(settings.definitions, static definition =>
            definition.path == "Tests/Overlay/Show Overlay");
        Assert.Equal("Tests/Overlay/Show Overlay", definition.path);
        EditorSettingObject value = settings.Get(definition.path);
        value.SetAsBoolean("value", false);
        Assert.True(settings.Apply(
            new Dictionary<string, EditorSettingObject>(StringComparer.Ordinal)
            {
                [definition.path] = value
            }));

        Write("ProjectSettings.editor.cs", "public sealed class ProjectOverlaySetting { }");

        ScriptCompilationResult replacement = Compile();
        Assert.True(replacement.success, FormatDiagnostics(replacement));
        Assert.True(m_manager.ApplyPendingReload());
        Assert.DoesNotContain(settings.definitions, static definition =>
            definition.path == "Tests/Overlay/Show Overlay");
        Assert.Throws<ArgumentException>(() => settings.Get("Tests/Overlay/Show Overlay"));
    }

    [Fact]
    public void AssetIconKindFacadeAliasesEveryImGuiIconConstant()
    {
        string[] iconNames = GetImGuiIconType()
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(static field => field.IsLiteral && field.FieldType == typeof(string))
            .Select(static field => field.Name)
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToArray();
        string fields = string.Join(
            Environment.NewLine,
            iconNames.Select(static name =>
                $"    public const string {name} = AssetIconKind.{name};"));
        Write("AssetIconCatalog.editor.cs", $$"""
            using InnoEditor.Assets;

            public static class AssetIconCatalog
            {
            {{fields}}
                public const string FullyQualified = InnoEditor.Assets.AssetIconKind.File;
            }
            """);

        ScriptCompilationResult result = Compile();

        Assert.True(result.success, FormatDiagnostics(result));
        string documentationPath = Directory.EnumerateFiles(
            Path.Combine(m_projectRoot, "Library", "ScriptApi", "Editor"),
            "Inno.ScriptApi.Editor.xml",
            SearchOption.AllDirectories).Single();
        string documentation = File.ReadAllText(documentationPath);
        Assert.Contains("T:InnoEditor.Assets.AssetIconKind", documentation);
        Assert.Contains("F:InnoEditor.Assets.AssetIconKind.File", documentation);
        Assert.Contains("Provides the File value from AssetIconKind.", documentation);
    }

    [Fact]
    public void RemovingScriptAssetIconDeclarationRestoresBuiltInIcon()
    {
        Write("AssetIconReloadProbe.editor.cs", """
            using InnoEditor.Assets;
            using InnoEngine.Assets;

            [AssetIcon(typeof(TextAsset), AssetIconKind.FileImage, priority: 100)]
            public sealed class AssetIconReloadProbe
            {
            }
            """);

        ScriptCompilationResult initial = Compile();

        Assert.True(initial.success, FormatDiagnostics(initial));
        Assert.True(m_manager.ApplyPendingReload());
        using IDisposable iconRegistry = CreateAssetIconRegistry();
        Assert.Equal(
            ResolveImGuiIcon("FileImage"),
            ResolveAssetIcon(iconRegistry, typeof(TextAsset), "Assets/Test.txt"));

        Write("AssetIconReloadProbe.editor.cs", """
            public sealed class AssetIconReloadProbe
            {
            }
            """);

        ScriptCompilationResult updated = Compile();

        Assert.True(updated.success, FormatDiagnostics(updated));
        Assert.True(m_manager.ApplyPendingReload());
        Assert.Equal(
            ResolveImGuiIcon("FileLines"),
            ResolveAssetIcon(iconRegistry, typeof(TextAsset), "Assets/Test.txt"));
    }

    [Theory]
    [InlineData("Assets/Scripts/Player.cs", "FileCode")]
    [InlineData("Assets/Scenes/Main.iscene", "Cubes")]
    [InlineData("Assets/Prefabs/Player.iprefab", "DiceD6")]
    [InlineData("Assets/Scripts/Game.iasmdef", "Gears")]
    [InlineData("Assets/Data/Settings.JSON", "FileLines")]
    public void BuiltInAssetIconsResolveFromFileExtensions(string relativePath, string iconName)
    {
        using IDisposable iconRegistry = CreateAssetIconRegistry();

        Assert.Equal(
            ResolveImGuiIcon(iconName),
            ResolveAssetIcon(iconRegistry, assetType: null, relativePath: relativePath));
    }

    [Fact]
    public void RemovingOnlyScriptAssetIconDeclarationLeavesCustomAssetUnregistered()
    {
        Write("CustomIconAsset.editor.cs", """
            using InnoEditor.Assets;
            using InnoEngine.Assets;

            [AssetIcon(typeof(CustomIconAsset), AssetIconKind.FileImage)]
            public sealed class CustomIconAsset : AssetObject
            {
            }
            """);

        ScriptCompilationResult initial = Compile();

        Assert.True(initial.success, FormatDiagnostics(initial));
        Assert.True(m_manager.ApplyPendingReload());
        using IDisposable iconRegistry = CreateAssetIconRegistry();
        Type initialAssetType = ResolveTypeByName("CustomIconAsset");
        Assert.Equal(ResolveImGuiIcon("FileImage"), ResolveAssetIcon(iconRegistry, initialAssetType));

        Write("CustomIconAsset.editor.cs", """
            using InnoEngine.Assets;

            public sealed class CustomIconAsset : AssetObject
            {
            }
            """);

        ScriptCompilationResult updated = Compile();

        Assert.True(updated.success, FormatDiagnostics(updated));
        Assert.True(m_manager.ApplyPendingReload());
        Type updatedAssetType = ResolveTypeByName("CustomIconAsset");
        Assert.False(TryResolveAssetIcon(
            iconRegistry,
            updatedAssetType,
            "Assets/CustomIconAsset.unknown",
            out _));
    }

    [Fact]
    public void GameScriptsCannotAccessInteractionFacades()
    {
        Write("ForbiddenEditorAction.cs", """
            using InnoEditor.Interactions;
            public sealed class ForbiddenEditorAction : EditorAction
            {
                protected override void Execute(EditorActionContext context)
                {
                }
            }
            """);

        ScriptCompilationResult result = Compile();

        Assert.False(result.success);
        Assert.Contains(result.diagnostics, static diagnostic => diagnostic.id == "CS0246");
    }

    [Fact]
    public void LocalPluginTypesRequireAnExplicitNamespaceImport()
    {
        InstallSourcePlugin();
        Write("PluginConsumer.cs", """
            using ProjectPluginApi;

            public sealed class PluginConsumer
            {
                public int value => PluginValue.value;
            }
            """);

        ScriptCompilationResult result = Compile();

        Assert.True(result.success, FormatDiagnostics(result));
        Assert.True(m_manager.ApplyPendingReload());
        Type consumerType = ResolveTypeByName("PluginConsumer");
        object consumer = Activator.CreateInstance(consumerType)!;
        Assert.Equal(42, consumerType.GetProperty("value")!.GetValue(consumer));
    }

    [Fact]
    public void PluginRuntimeAssemblyCanReferenceDeclaredPluginDependency()
    {
        InstallSourcePlugins(
            new SourcePluginFixture(
                "tests.dependency-base",
                [],
                "Base.cs",
                "namespace DependencyBaseApi; public static class BaseValue { public const int value = 42; }"),
            new SourcePluginFixture(
                "tests.dependency-consumer",
                ["tests.dependency-base"],
                "Consumer.cs",
                "using DependencyBaseApi; namespace DependencyConsumerApi; "
                + "public sealed class ConsumerValue { public int value => BaseValue.value; }"));

        ScriptCompilationResult result = Compile();

        Assert.True(result.success, FormatDiagnostics(result));
        Assert.True(m_manager.ApplyPendingReload());
        Type consumerType = ResolveTypeByName("ConsumerValue");
        object consumer = Activator.CreateInstance(consumerType)!;
        Assert.Equal(42, consumerType.GetProperty("value")!.GetValue(consumer));
    }

    [Fact]
    public void PluginAssemblyDefinitionCannotReferenceProjectAssembly()
    {
        Write("ProjectOnly.cs", "public sealed class ProjectOnly { }");
        const string pluginId = "tests.project-reference";
        const string sourcePath = "Plugin.cs";
        const string definitionPath = "Plugin.iasmdef";
        Write(sourcePath, "public sealed class ForbiddenPluginType { }");
        WriteAssemblyDefinition(
            definitionPath,
            "Plugin.Forbidden",
            ScriptAssemblyScope.Runtime,
            references: ["Inno.GameScripts"]);
        (byte[] source, byte[] sourceMeta) = CaptureProjectSource(sourcePath);
        (byte[] definition, byte[] definitionMeta) = CaptureProjectSource(definitionPath);
        InstallPluginArchives(
        [
            new PluginArchiveFixture(
                pluginId,
                [],
                ["Assets/" + definitionPath],
                new Dictionary<string, byte[]>
                {
                    ["Assets/" + sourcePath] = source,
                    ["Assets/" + sourcePath + ".imeta"] = sourceMeta,
                    ["Assets/" + definitionPath] = definition,
                    ["Assets/" + definitionPath + ".imeta"] = definitionMeta
                })
        ]);

        ScriptCompilationResult result = Compile();

        Assert.False(result.success);
        Assert.Contains(result.diagnostics, static diagnostic =>
            diagnostic.message.Contains("cannot reference project assembly", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void EditorPluginDescriptorIsVisibleOnlyToEditorScripts()
    {
        InstallSourcePlugin(editorOnly: true);
        Write("RuntimePluginConsumer.cs", """
            using ProjectPluginApi;
            public sealed class RuntimePluginConsumer
            {
                public PluginObject value = new();
            }
            """);

        ScriptCompilationResult rejected = Compile();
        Assert.False(rejected.success);
        Assert.Contains(rejected.diagnostics, static diagnostic => diagnostic.id == "CS0246");

        File.Delete(Path.Combine(m_projectRoot, "Assets", "RuntimePluginConsumer.cs"));
        Write("EditorPluginConsumer.editor.cs", """
            using ProjectPluginApi;
            public sealed class EditorPluginConsumer
            {
                public PluginObject value = new();
            }
            """);
        ScriptCompilationResult accepted = Compile();
        Assert.True(accepted.success, FormatDiagnostics(accepted));
        Assert.True(m_manager.ApplyPendingReload());
        Assert.Equal("PluginObject", ResolveTypeByName("EditorPluginConsumer")
            .GetField("value")!.FieldType.Name);
    }

    [Fact]
    public void ProcessWideAssetResolverRejectsACollectiblePluginTarget()
    {
        InstallSourcePlugin();
        ScriptCompilationResult initial = Compile();
        Assert.True(initial.success, FormatDiagnostics(initial));
        Assert.True(m_manager.ApplyPendingReload());
        object pluginTarget = Activator.CreateInstance(ResolveTypeByName("PluginObject"))!;
        MethodInfo method = typeof(ScriptManagerTests).GetMethod(
            nameof(ResolveAssetReferenceFromTarget),
            BindingFlags.Static | BindingFlags.NonPublic)!;
        var resolver = (Func<Guid, Guid, string, Type, string, AssetObject>)method.CreateDelegate(
            typeof(Func<Guid, Guid, string, Type, string, AssetObject>),
            pluginTarget);

        Assert.Throws<ArgumentException>(() => AssetSerializationServices.SetReferenceResolver(resolver));
    }

    [Fact]
    public void ReloadOperationsUseTheRequiredDependencyClosureAndExactUpstreamAssemblies()
    {
        InstallSourcePlugin();
        Write("RuntimeReloadProbe.cs", """
            using ProjectPluginApi;

            public sealed class RuntimeReloadProbe
            {
                public int generation => 1;
                public PluginObject CreatePlugin() => new PluginObject();
            }
            """);
        Write("EditorReloadProbe.editor.cs", """
            public sealed class EditorReloadProbe
            {
                public int generation => 1;
                public RuntimeReloadProbe CreateRuntime() => new RuntimeReloadProbe();
            }
            """);

        ScriptCompilationResult initial = Compile();
        Assert.True(initial.success, FormatDiagnostics(initial));
        Assert.True(m_manager.ApplyPendingReload());
        IReadOnlyDictionary<string, int> generations = GetReloadModuleGenerations();
        Type pluginType = ResolveTypeByName("PluginObject");
        Type runtimeType = ResolveTypeByName("RuntimeReloadProbe");
        Type editorType = ResolveTypeByName("EditorReloadProbe");
        Assert.Same(pluginType, runtimeType.GetMethod("CreatePlugin")!.ReturnType);
        Assert.Same(runtimeType, editorType.GetMethod("CreateRuntime")!.ReturnType);
        Assert.NotSame(
            AssemblyLoadContext.GetLoadContext(pluginType.Assembly),
            AssemblyLoadContext.GetLoadContext(runtimeType.Assembly));
        Assert.NotSame(
            AssemblyLoadContext.GetLoadContext(runtimeType.Assembly),
            AssemblyLoadContext.GetLoadContext(editorType.Assembly));

        ScriptCompilationResult unchanged = Compile();
        Assert.True(unchanged.success, FormatDiagnostics(unchanged));
        Assert.False(m_manager.ApplyPendingReload());
        Assert.Equal(generations, GetReloadModuleGenerations());

        Write("EditorReloadProbe.editor.cs", """
            public sealed class EditorReloadProbe
            {
                public int generation => 2;
                public RuntimeReloadProbe CreateRuntime() => new RuntimeReloadProbe();
            }
            """);
        ScriptCompilationResult editorChange = Compile();
        Assert.True(editorChange.success, FormatDiagnostics(editorChange));
        Assert.True(m_manager.ApplyPendingReload());
        IReadOnlyDictionary<string, int> afterEditor = GetReloadModuleGenerations();
        Assert.Equal(generations["ProjectPlugins"], afterEditor["ProjectPlugins"]);
        Assert.Equal(generations["RuntimeScripts"], afterEditor["RuntimeScripts"]);
        Assert.Equal(generations["EditorScripts"] + 1, afterEditor["EditorScripts"]);

        Write("RuntimeReloadProbe.cs", """
            using ProjectPluginApi;

            public sealed class RuntimeReloadProbe
            {
                public int generation => 2;
                public PluginObject CreatePlugin() => new PluginObject();
            }
            """);
        ScriptCompilationResult runtimeChange = Compile();
        Assert.True(runtimeChange.success, FormatDiagnostics(runtimeChange));
        Assert.True(m_manager.ApplyPendingReload());
        IReadOnlyDictionary<string, int> afterRuntime = GetReloadModuleGenerations();
        Assert.Equal(afterEditor["ProjectPlugins"], afterRuntime["ProjectPlugins"]);
        Assert.Equal(afterEditor["RuntimeScripts"] + 1, afterRuntime["RuntimeScripts"]);
        Assert.Equal(afterEditor["EditorScripts"] + 1, afterRuntime["EditorScripts"]);

        m_manager.ReloadScripting();
        ScriptCompilationResult fullScripting = Compile();
        Assert.True(fullScripting.success, FormatDiagnostics(fullScripting));
        Assert.True(m_manager.ApplyPendingReload());
        IReadOnlyDictionary<string, int> afterScripting = GetReloadModuleGenerations();
        Assert.Equal(afterRuntime["ProjectPlugins"], afterScripting["ProjectPlugins"]);
        Assert.Equal(afterRuntime["RuntimeScripts"] + 1, afterScripting["RuntimeScripts"]);
        Assert.Equal(afterRuntime["EditorScripts"] + 1, afterScripting["EditorScripts"]);
        Assert.Contains("Verifying", m_manager.compilationStatus, StringComparison.OrdinalIgnoreCase);

        m_manager.RecompileScripting();
        m_manager.ReloadPlugins();
        m_manager.ReloadScripting();
        Assert.True(m_manager.isCompilationPending);
        ScriptCompilationResult fullPlugin = Compile();
        Assert.True(fullPlugin.success, FormatDiagnostics(fullPlugin));
        Assert.True(m_manager.ApplyPendingReload());
        IReadOnlyDictionary<string, int> afterPlugins = GetReloadModuleGenerations();
        Assert.Equal(afterScripting["ProjectPlugins"] + 1, afterPlugins["ProjectPlugins"]);
        Assert.Equal(afterScripting["RuntimeScripts"] + 1, afterPlugins["RuntimeScripts"]);
        Assert.Equal(afterScripting["EditorScripts"] + 1, afterPlugins["EditorScripts"]);
        Assert.Contains("Verifying", m_manager.compilationStatus, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("generation", m_manager.compilationStatus, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ForcedUnloadVerificationCompletesWhenTheRetiredContextIsUnreachable()
    {
        Write("UnloadProbe.editor.cs", "public sealed class UnloadProbe { public int generation => 1; }");
        Assert.True(Compile().success);
        Assert.True(m_manager.ApplyPendingReload());
        WeakReference retiredContext = CaptureLoadContext("UnloadProbe");

        Write("UnloadProbe.editor.cs", "public sealed class UnloadProbe { public int generation => 2; }");
        Assert.True(Compile().success);
        Assert.True(m_manager.ApplyPendingReload());
        Assert.True(m_manager.IsUnloadVerificationPendingForTest());
        Assert.Equal(0.97f, m_manager.compilationProgress, 3);
        Type editorScriptingType = typeof(ScriptManager).Assembly.GetType(
            "Inno.Editor.Scripting.EditorScripting",
            throwOnError: true)!;
        object scripting = ScriptingTestReflection.Create(editorScriptingType);
        FieldInfo managerField = editorScriptingType.GetField(
            "m_manager",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        managerField.SetValue(scripting, m_manager);
        Type modalType = typeof(ScriptManager).Assembly.GetType(
            "Inno.Editor.Scripting.ScriptCompilationModal",
            throwOnError: true)!;
        object modal = ScriptingTestReflection.Create(modalType, scripting);
        Assert.True(ScriptingTestReflection.Get<bool>(modal, "isVisible"));

        Exception? failure = CompleteUnloadVerification();

        Assert.Null(failure);
        Assert.False(m_manager.IsUnloadVerificationPendingForTest());
        Assert.False(ScriptingTestReflection.Get<bool>(modal, "isVisible"));
        Assert.False(retiredContext.IsAlive);
        Assert.Equal(
            "Script reload and retired assembly unload completed.",
            m_manager.compilationStatus);
        managerField.SetValue(scripting, null);
    }

    [Fact]
    public void RetainedExternalTypeFailsBoundedForcedUnloadVerificationAfterCommit()
    {
        var sink = new TestDiagnosticSink();
        DiagnosticManager.RegisterSink(sink);
        try
        {
            Write("UnloadProbe.editor.cs", "public sealed class UnloadProbe { public int generation => 1; }");
            ScriptCompilationResult initial = Compile();
            Assert.True(initial.success, FormatDiagnostics(initial));
            Assert.True(m_manager.ApplyPendingReload());
            StrongTypeHolder holder = CreateStrongTypeHolder("UnloadProbe");

            Write("UnloadProbe.editor.cs", "public sealed class UnloadProbe { public int generation => 2; }");
            ScriptCompilationResult replacement = Compile();
            Assert.True(replacement.success, FormatDiagnostics(replacement));
            Assert.True(m_manager.ApplyPendingReload());
            Exception? failure = CompleteUnloadVerification();

            Assert.NotNull(failure);
            Assert.False(m_manager.IsUnloadVerificationPendingForTest());
            Assert.Contains("remained reachable", failure.Message, StringComparison.Ordinal);
            Assert.Contains("active generation remains committed", failure.Message, StringComparison.OrdinalIgnoreCase);
            Assert.True(sink.ContainsCode("INNO-ALC-UNLOAD"));
            Assert.True(sink.ContainsDiagnostic("INNO-ALC-UNLOAD", DiagnosticSeverity.Error));
            Assert.Contains(
                sink.GetMessages("INNO-ALC-UNLOAD"),
                static message =>
                    message.Contains("EditorScripts", StringComparison.Ordinal) &&
                    message.Contains("InnoScripting/Editor", StringComparison.Ordinal) &&
                    message.Contains("generation", StringComparison.Ordinal));
            Assert.Contains("committed", m_manager.compilationStatus, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("failed", m_manager.compilationStatus, StringComparison.OrdinalIgnoreCase);
            ClearStrongTypeHolder(holder);
            ForceCollection();
            GC.KeepAlive(holder);
        }
        finally
        {
            DiagnosticManager.UnregisterSink(sink);
        }
    }

    [Fact]
    public void CandidateFailureAtEveryDomainNeverPublishesAPartialGeneration()
    {
        Write("AtomicRuntime.cs", "public sealed class AtomicRuntime { }");
        Write("AtomicEditor.editor.cs", "public sealed class AtomicEditor { public AtomicRuntime value = new(); }");
        ScriptCompilationResult initial = Compile();
        Assert.True(initial.success, FormatDiagnostics(initial));
        Assert.True(m_manager.ApplyPendingReload());
        IReadOnlyDictionary<string, int> previous = GetReloadModuleGenerations();

        m_manager.ReloadPlugins();
        ScriptCompilationResult candidate = Compile();
        Assert.True(candidate.success, FormatDiagnostics(candidate));
        foreach (string failedModule in new[] { "ProjectPlugins", "RuntimeScripts", "EditorScripts" })
        {
            IReadOnlyList<AssemblyLoadRequest> invalid = candidate.ReloadRequestsForTest()
                .Select(request => string.Equals(request.moduleName, failedModule, StringComparison.Ordinal)
                    ? new AssemblyLoadRequest
                    {
                        moduleName = request.moduleName,
                        mainAssemblyPath = Path.Combine(m_projectRoot, "Missing", failedModule + ".dll"),
                        preloadAssemblyPaths = request.preloadAssemblyPaths,
                        collectible = request.collectible,
                        domain = request.domain,
                        scope = request.scope,
                        assemblyScopes = request.assemblyScopes
                    }
                    : request)
                .ToArray();

            Assert.ThrowsAny<Exception>(() => AssemblyManager.BeginReload(invalid));
            Assert.Equal(previous, GetReloadModuleGenerations());
        }

        AssemblyLoadRequest plugin = candidate.ReloadRequestsForTest().Single(static request =>
            request.domain == AssemblyDomain.InnoPlugin);
        AssemblyLoadRequest runtime = candidate.ReloadRequestsForTest().Single(static request =>
            request.domain == AssemblyDomain.InnoScripting && request.scope == AssemblyScope.Runtime);
        Assert.Throws<InvalidOperationException>(() => AssemblyManager.BeginReload([plugin]));
        Assert.Throws<InvalidOperationException>(() => AssemblyManager.BeginReload([runtime]));
        Assert.Equal(previous, GetReloadModuleGenerations());

        Assert.True(m_manager.ApplyPendingReload());
        IReadOnlyDictionary<string, int> current = GetReloadModuleGenerations();
        Assert.All(current, pair => Assert.Equal(previous[pair.Key] + 1, pair.Value));
        ForceCollection();
    }

    [Fact]
    public void PreviousExtensionsStopAndDetachBeforeCandidateStartAndAttach()
    {
        string moduleStopped = Path.Combine(m_projectRoot, "module-stopped.txt");
        string panelDetached = Path.Combine(m_projectRoot, "panel-detached.txt");
        WriteExtensionLifecycleProbe(1, moduleStopped, panelDetached, logPath: null, failStart: false);
        ScriptCompilationResult initial = Compile();
        Assert.True(initial.success, FormatDiagnostics(initial));
        Assert.True(m_manager.ApplyPendingReload());
        using var runtime = new EditorInteractionRuntime(m_projectRoot);
        runtime.Start();

        WriteExtensionLifecycleProbe(2, moduleStopped, panelDetached, logPath: null, failStart: false);
        ScriptCompilationResult replacement = Compile();
        Assert.True(replacement.success, FormatDiagnostics(replacement));
        Assert.True(m_manager.ApplyPendingReload());

        Assert.True(File.Exists(moduleStopped));
        Assert.True(File.Exists(panelDetached));
        Assert.Contains(runtime.panels, static panel => panel.id == "tests.lifecycle-order");
    }

    [Fact]
    public void CandidateExtensionFailureRestartsAndReattachesPreviousGeneration()
    {
        string moduleStopped = Path.Combine(m_projectRoot, "rollback-module-stopped.txt");
        string panelDetached = Path.Combine(m_projectRoot, "rollback-panel-detached.txt");
        string logPath = Path.Combine(m_projectRoot, "extension-lifecycle.log");
        WriteExtensionLifecycleProbe(1, moduleStopped, panelDetached, logPath, failStart: false);
        ScriptCompilationResult initial = Compile();
        Assert.True(initial.success, FormatDiagnostics(initial));
        Assert.True(m_manager.ApplyPendingReload());
        using var runtime = new EditorInteractionRuntime(m_projectRoot);
        runtime.Start();

        WriteExtensionLifecycleProbe(2, moduleStopped, panelDetached, logPath, failStart: true);
        ScriptCompilationResult replacement = Compile();
        Assert.True(replacement.success, FormatDiagnostics(replacement));
        Assert.ThrowsAny<Exception>(() => m_manager.ApplyPendingReload());

        string[] events = File.ReadAllText(logPath).Split(';', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, events.Count(static value => value == "module-start-1"));
        Assert.Equal(2, events.Count(static value => value == "panel-attach-1"));
        Assert.Contains("module-stop-1", events);
        Assert.Contains("panel-detach-1", events);
        Assert.Contains(runtime.panels, static panel => panel.id == "tests.lifecycle-order");
    }

    [Fact]
    public void PublicReloadSurfaceAndMenusExposeExactlyTheThreeUserOperations()
    {
        MethodInfo[] publicMethods = typeof(ScriptManager)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);
        string[] reloadMethods = publicMethods
            .Where(static method => method.Name.Contains("Scripting", StringComparison.Ordinal) ||
                                    method.Name.Contains("Plugins", StringComparison.Ordinal))
            .Select(static method => method.Name)
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(
            new[] { "RecompileScripting", "ReloadPlugins", "ReloadScripting" },
            reloadMethods);
        Assert.DoesNotContain(publicMethods, static method =>
            method.Name is "TryCompilePending" or "CompileAsync" or "ApplyPendingReload" or "RequestCompile");

        string[] menus = typeof(ScriptManager).Assembly.GetTypes()
            .SelectMany(static type => type.GetCustomAttributes<EditorMenuAttribute>(inherit: false))
            .Where(static menu => menu.path.StartsWith("Scripting/", StringComparison.Ordinal))
            .Select(static menu => menu.path)
            .OrderBy(static path => path, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(
            new[]
            {
                "Scripting/Recompile Scripting",
                "Scripting/Reload Plugins",
                "Scripting/Reload Scripting"
            },
            menus);
    }

    [Fact]
    public void InspectorActionAndDropProtocolsDoNotStoreGenerationBoundTypes()
    {
        Assembly inspector = typeof(AssetReferenceDropTarget).Assembly;
        Type addComponent = inspector.GetType(
            "Inno.Editor.Panel.Inspector.AddComponentCommand",
            throwOnError: true)!;
        Type addSystem = inspector.GetType(
            "Inno.Editor.Panel.Inspector.AddSystemCommand",
            throwOnError: true)!;

        Assert.Equal(typeof(TypeRef), addComponent.BaseType!.GetGenericArguments()[1]);
        Assert.Equal(typeof(TypeRef), addSystem.BaseType!.GetGenericArguments()[1]);
        Assert.DoesNotContain(
            typeof(AssetReferenceDropTarget).GetFields(BindingFlags.Instance | BindingFlags.NonPublic),
            static field => field.FieldType == typeof(Type));
        Assert.DoesNotContain(
            typeof(EngineObjectReferenceDropTarget).GetFields(BindingFlags.Instance | BindingFlags.NonPublic),
            static field => field.FieldType == typeof(Type));
    }

    [Fact]
    public void CompilationWideUsingDirectivesAreRejected()
    {
        Write(
            "ForbiddenUsing.cs",
            string.Concat(
                "global",
                " using InnoEngine.Scene;\n\n",
                "public sealed class ForbiddenUsingBehavior : GameBehavior { }"));

        ScriptCompilationResult result = Compile();

        Assert.False(result.success);
        Assert.Contains(result.diagnostics, static diagnostic => diagnostic.id == "INNO2003");
    }

    [Fact]
    public void GenerateProjectFilesMirrorsRuntimeClassificationAndReferences()
    {
        Write("Runtime.cs", "using InnoEngine.Scene; public sealed class RuntimeScript : GameBehavior { }");
        Write("Tools.editor.cs", "public sealed class EditorScript { }");

        m_manager.GenerateProjectFiles();

        string gameProject = File.ReadAllText(Path.Combine(m_projectRoot, "Inno.GameScripts.csproj"));
        string editorProject = File.ReadAllText(Path.Combine(m_projectRoot, "Inno.EditorScripts.csproj"));
        Assert.Contains("Compile Include=\"Assets/Runtime.cs\"", gameProject);
        Assert.DoesNotContain("Tools.editor.cs", gameProject);
        Assert.Contains("Compile Include=\"Assets/Tools.editor.cs\"", editorProject);
        Assert.DoesNotContain("Library/**.cs", gameProject);
        Assert.Contains("Inno.GameScripts.csproj", editorProject);
        Assert.Contains("<EnableDefaultItems>false</EnableDefaultItems>", gameProject);
        Assert.Contains("<Folder Include=\"Assets/\"", gameProject);
        Assert.Contains("<RestoreOutputPath>Library/IDE/obj/Inno.GameScripts/</RestoreOutputPath>", gameProject);
        Assert.Contains("<MSBuildProjectExtensionsPath>Library/IDE/obj/Inno.GameScripts/</MSBuildProjectExtensionsPath>", gameProject);
        Assert.Contains("<Import Project=\"Sdk.props\" Sdk=\"Microsoft.NET.Sdk\"", gameProject);
        Assert.Contains("<Import Project=\"Sdk.targets\" Sdk=\"Microsoft.NET.Sdk\"", gameProject);
        Assert.DoesNotContain("<Reference Include=\"System.Private.CoreLib\"", gameProject);
        Assert.Contains("Library/IDE/", gameProject);
        Assert.True(File.Exists(Path.Combine(m_projectRoot, "InnoProject.sln")));
    }

    [Fact]
    public void RuntimeReloadReplacesLiveBehaviorAndPreservesLifecycleState()
    {
        WriteMigratingBehavior(1);
        ScriptCompilationResult firstCompilation = Compile();
        Assert.True(firstCompilation.success, FormatDiagnostics(firstCompilation));
        Assert.True(m_manager.ApplyPendingReload());
        Type previousType = ResolveTypeByName("MigratingBehavior");
        var scene = new GameScene("Hot Reload");
        GameObject gameObject = scene.CreateObject("Actor");
        GameObject referencedObject = scene.CreateObject("Referenced");
        GameComponent previous = gameObject.AddComponent(previousType);
        Type previousSystemType = ResolveTypeByName("MigratingSystem");
        GameSystem previousSystem = scene.AddSystem(previousSystemType);
        int previousComponentRuntimeTypeId = TypeCacheManager.GetTypeRef(previousType).runtimeId;
        int previousSystemRuntimeTypeId = TypeCacheManager.GetTypeRef(previousSystemType).runtimeId;
        SetProperty(previous, "value", 37);
        previousType.GetProperty("target")!.SetValue(previous, referencedObject);
        SetProperty(previousSystem, "value", 51);
        Guid persistentId = previous.identity.persistentId;
        Guid systemPersistentId = previousSystem.identity.persistentId;
        SceneManager.LoadScene(scene);
        SceneManager.Update(0.016f);
        Assert.Equal(1, GetProperty(previous, "awakeCount"));
        Assert.Equal(1, GetProperty(previous, "startCount"));
        Assert.Equal(1, GetProperty(previous, "enableCount"));
        Assert.Equal(1, GetProperty(previousSystem, "awakeCount"));

        WriteMigratingBehavior(2);
        ScriptCompilationResult secondCompilation = Compile();
        Assert.True(secondCompilation.success, FormatDiagnostics(secondCompilation));
        Assert.True(m_manager.ApplyPendingReload());
        GameComponent current = gameObject.GetComponents()
            .Single(component => component.GetType().Name == "MigratingBehavior");
        GameSystem currentSystem = scene.GetSystems()
            .Single(system => system.GetType().Name == "MigratingSystem");
        TypeRef currentComponentType = TypeCacheManager.GetTypeRef(current.GetType());
        TypeRef currentSystemType = TypeCacheManager.GetTypeRef(currentSystem.GetType());

        Assert.NotSame(previous, current);
        Assert.NotEqual(previousComponentRuntimeTypeId, currentComponentType.runtimeId);
        Assert.Same(current, Assert.Single(GetComponents(gameObject, current.GetType())));
        _ = Assert.Throws<InvalidOperationException>(() => gameObject.AddComponent(current.GetType()));
        Assert.True(previous.isDestroyed);
        Assert.Equal(1, GetProperty(previous, "disableCount"));
        Assert.Equal(persistentId, current.identity.persistentId);
        Assert.Equal(37, GetProperty(current, "value"));
        Assert.Same(referencedObject, current.GetType().GetProperty("target")!.GetValue(current));
        Assert.Equal(1, GetProperty(current, "awakeCount"));
        Assert.Equal(1, GetProperty(current, "startCount"));
        Assert.Equal(1, GetProperty(current, "enableCount"));
        Assert.Equal(2, GetProperty(current, "generation"));
        Assert.NotSame(previousSystem, currentSystem);
        Assert.NotEqual(previousSystemRuntimeTypeId, currentSystemType.runtimeId);
        _ = Assert.Throws<InvalidOperationException>(() => scene.AddSystem(currentSystem.GetType()));
        Assert.True(previousSystem.isDestroyed);
        Assert.Equal(1, GetProperty(previousSystem, "disableCount"));
        Assert.Equal(systemPersistentId, currentSystem.identity.persistentId);
        Assert.Equal(51, GetProperty(currentSystem, "value"));
        Assert.Equal(1, GetProperty(currentSystem, "awakeCount"));
        Assert.Equal(1, GetProperty(currentSystem, "startCount"));
        Assert.Equal(1, GetProperty(currentSystem, "enableCount"));
        Assert.Equal(2, GetProperty(currentSystem, "generation"));

        SceneManager.Update(0.016f);
        Assert.Equal(1, GetProperty(current, "awakeCount"));
        Assert.Equal(1, GetProperty(current, "startCount"));
        Assert.Equal(2, GetProperty(current, "enableCount"));
        Assert.Equal(2, GetProperty(current, "updateCount"));
        Assert.Equal(1, GetProperty(currentSystem, "awakeCount"));
        Assert.Equal(1, GetProperty(currentSystem, "startCount"));
        Assert.Equal(2, GetProperty(currentSystem, "enableCount"));
        Assert.Equal(2, GetProperty(currentSystem, "updateCount"));
    }

    [Fact]
    public void IdentityObserverFailureDuringReplacementRestoresTheExactPreviousGeneration()
    {
        WriteMigratingBehavior(1);
        ScriptCompilationResult firstCompilation = Compile();
        Assert.True(firstCompilation.success, FormatDiagnostics(firstCompilation));
        Assert.True(m_manager.ApplyPendingReload());
        Type previousType = ResolveTypeByName("MigratingBehavior");
        var scene = new GameScene("Identity Rollback");
        GameObject gameObject = scene.CreateObject("Actor");
        GameComponent previous = gameObject.AddComponent(previousType);
        Guid persistentId = previous.identity.persistentId;
        SceneManager.LoadScene(scene);

        WriteMigratingBehavior(2);
        ScriptCompilationResult secondCompilation = Compile();
        Assert.True(secondCompilation.success, FormatDiagnostics(secondCompilation));
        Action<IIdentityObject> observer = value =>
        {
            if (ReferenceEquals(value, previous))
                throw new InvalidOperationException("Injected identity observer failure.");
        };
        IdentityManager.ObjectUnregistered += observer;
        try
        {
            _ = Assert.ThrowsAny<Exception>(() => m_manager.ApplyPendingReload());
        }
        finally
        {
            IdentityManager.ObjectUnregistered -= observer;
        }

        Assert.Same(previous, IdentityManager.Get<GameComponent>(persistentId));
        Assert.Same(previous, Assert.Single(gameObject.GetComponents(), component => component.GetType() == previousType));
        Assert.Same(previous, Assert.Single(GetComponents(gameObject, previousType)));
        _ = Assert.Throws<InvalidOperationException>(() => gameObject.AddComponent(previousType));
        Assert.False(previous.isDestroyed);
        Assert.Same(previousType, ResolveTypeByName("MigratingBehavior"));

        Assert.True(m_manager.ApplyPendingReload());
        GameComponent current = Assert.Single(
            gameObject.GetComponents(),
            component => component.GetType().Name == "MigratingBehavior");
        Assert.NotSame(previous, current);
        Assert.Same(current, Assert.Single(GetComponents(gameObject, current.GetType())));
        Assert.Equal(persistentId, current.identity.persistentId);
        Assert.Equal(2, GetProperty(current, "generation"));
    }

    [Fact]
    public void LifecycleFailureBeforeActivationRestoresThePreviousEnabledStateImmediately()
    {
        WriteMigratingBehavior(1);
        ScriptCompilationResult firstCompilation = Compile();
        Assert.True(firstCompilation.success, FormatDiagnostics(firstCompilation));
        Assert.True(m_manager.ApplyPendingReload());
        Type previousType = ResolveTypeByName("MigratingBehavior");
        var scene = new GameScene("Lifecycle Rollback");
        GameObject gameObject = scene.CreateObject("Actor");
        GameComponent previous = gameObject.AddComponent(previousType);
        SceneManager.LoadScene(scene);
        SceneManager.Update(0.016f);
        Assert.Equal(1, GetProperty(previous, "enableCount"));

        WriteMigratingBehavior(2);
        ScriptCompilationResult secondCompilation = Compile();
        Assert.True(secondCompilation.success, FormatDiagnostics(secondCompilation));
        FieldInfo throwOnDisable = previousType.GetField(
            "throwOnDisable",
            BindingFlags.Public | BindingFlags.Static)!;
        throwOnDisable.SetValue(null, true);
        try
        {
            _ = Assert.ThrowsAny<Exception>(() => m_manager.ApplyPendingReload());
        }
        finally
        {
            throwOnDisable.SetValue(null, false);
        }

        Assert.Same(previous, Assert.Single(gameObject.GetComponents(), component => component.GetType() == previousType));
        Assert.Same(previousType, ResolveTypeByName("MigratingBehavior"));
        Assert.Equal(0, GetProperty(previous, "disableCount"));
        Assert.Equal(2, GetProperty(previous, "enableCount"));

        SceneManager.Update(0.016f);
        Assert.Equal(2, GetProperty(previous, "enableCount"));
        Assert.True(m_manager.ApplyPendingReload());
    }

    [Fact]
    public void RuntimeReload_SkipsAnIncompatiblePropertyAndPreservesTheNewDefault()
    {
        WriteChangingPropertyBehavior(useString: false);
        ScriptCompilationResult firstCompilation = Compile();
        Assert.True(firstCompilation.success, FormatDiagnostics(firstCompilation));
        Assert.True(m_manager.ApplyPendingReload());
        Type previousType = ResolveTypeByName("ChangingPropertyBehavior");
        var scene = new GameScene("Changing Property");
        GameObject gameObject = scene.CreateObject("Actor");
        GameComponent previous = gameObject.AddComponent(previousType);
        SetField(previous, "changed", 42);
        SetField(previous, "compatible", 73);
        SceneManager.LoadScene(scene);

        WriteChangingPropertyBehavior(useString: true);
        ScriptCompilationResult secondCompilation = Compile();

        Assert.True(secondCompilation.success, FormatDiagnostics(secondCompilation));
        Assert.True(m_manager.ApplyPendingReload());
        GameComponent current = gameObject.GetComponents()
            .Single(component => component.GetType().Name == "ChangingPropertyBehavior");
        Assert.Equal("default", GetField(current, "changed"));
        Assert.Equal(73, GetField(current, "compatible"));
        Assert.True(previous.isDestroyed);
    }

    [Fact]
    public void MissingLiveTypes_PreserveSceneStateAcrossSerializationAndRecoverInPlace()
    {
        const string componentTypeId = "1b11fc01-68f7-48c5-a228-ad2dd311ee6a";
        const string systemTypeId = "0164a502-aea6-427d-998e-026dd4098836";
        Write("Data/MissingDependency.txt", "dependency");
        AssetManager.Rescan();
        TextAsset dependency = AssetManager.Load<TextAsset>("Data/MissingDependency.txt");
        WriteIdentityProbe(componentTypeId, systemTypeId);
        ScriptCompilationResult firstCompilation = Compile();
        Assert.True(firstCompilation.success, FormatDiagnostics(firstCompilation));
        Assert.True(m_manager.ApplyPendingReload());
        Type previousType = ResolveTypeByName("IdentityProbe");
        Type previousSystemType = ResolveTypeByName("IdentityProbeSystem");
        var scene = new GameScene("Identity Probe");
        GameObject gameObject = scene.CreateObject("Actor");
        GameObject target = scene.CreateObject("Target");
        GameComponent previous = gameObject.AddComponent(previousType);
        GameSystem previousSystem = scene.AddSystem(previousSystemType);
        SetProperty(previous, "value", 41);
        previousType.GetProperty("target")!.SetValue(previous, target);
        previousType.GetProperty("asset")!.SetValue(previous, dependency);
        SetProperty(previousSystem, "value", 73);
        Guid componentId = previous.identity.persistentId;
        Guid systemId = previousSystem.identity.persistentId;
        byte[] liveSceneData = SerializationManager.Serialize(scene);
        SceneManager.LoadScene(scene);

        WriteIdentityProbe(
            "69df8ec0-e28d-4769-9e8a-0a83ef18d62c",
            "8245b26f-9325-4de8-ae09-93abc53f5aaa");
        ScriptCompilationResult secondCompilation = Compile();
        Assert.True(secondCompilation.success, FormatDiagnostics(secondCompilation));
        var missingSink = new TestDiagnosticSink();
        DiagnosticManager.RegisterSink(missingSink);
        try
        {
            Assert.True(m_manager.ApplyPendingReload());
            Assert.True(missingSink.ContainsCode("INNOHR0002"));

            missingSink.ClearPresentation();
            ScriptCompilationResult unchangedCompilation = Compile();
            Assert.True(unchangedCompilation.success, FormatDiagnostics(unchangedCompilation));
            Assert.False(m_manager.ApplyPendingReload());
            Assert.True(missingSink.ContainsCode("INNOHR0002"));
        }
        finally
        {
            DiagnosticManager.UnregisterSink(missingSink);
        }

        MissingGameComponent missingComponent = Assert.IsType<MissingGameComponent>(
            gameObject.GetComponents().Single(component => component.identity.persistentId == componentId));
        MissingGameSystem missingSystem = Assert.IsType<MissingGameSystem>(
            scene.GetSystems().Single(system => system.identity.persistentId == systemId));
        Assert.Equal(Guid.Parse(componentTypeId), missingComponent.missingType.stableId);
        Assert.Equal(Guid.Parse(systemTypeId), missingSystem.missingType.stableId);
        Assert.False(missingSystem.enabled);
        missingSystem.enabled = true;
        Assert.False(missingSystem.enabled);
        Assert.False(missingSystem.isActiveAndEnabled);
        Assert.True(previous.isDestroyed);
        Assert.True(previousSystem.isDestroyed);

        byte[] missingSceneData = SerializationManager.Serialize(scene);
        Assert.Equal(liveSceneData, missingSceneData);

        m_manager.ReloadScripting();
        ScriptCompilationResult persistentMissingCompilation = Compile();
        Assert.True(persistentMissingCompilation.success, FormatDiagnostics(persistentMissingCompilation));
        var persistentMissingSink = new TestDiagnosticSink();
        DiagnosticManager.RegisterSink(persistentMissingSink);
        try
        {
            Assert.True(m_manager.ApplyPendingReload());
            Assert.True(persistentMissingSink.ContainsCode("INNOHR0002"));
        }
        finally
        {
            DiagnosticManager.UnregisterSink(persistentMissingSink);
        }

        SceneAsset capturedMissingScene = SceneAsset.Capture(scene);
        Assert.True(AssetManager.Save("Scenes/Missing.iscene", capturedMissingScene));
        SceneAsset savedMissingScene = AssetManager.Load<SceneAsset>("Scenes/Missing.iscene");
        Assert.Contains(
            AssetManager.GetDependencies(savedMissingScene),
            item => item.persistentId == dependency.identity.persistentId);

        var diagnosticWorkspace = new ReflectedSceneWorkspace();
        var diagnosticContext = new EditorContext(m_projectRoot);
        diagnosticWorkspace.Start(diagnosticContext);
        Assert.True(SceneManager.UnloadScene(scene));
        diagnosticWorkspace.Update(diagnosticContext);
        var loadedMissingSink = new TestDiagnosticSink();
        DiagnosticManager.RegisterSink(loadedMissingSink);
        try
        {
            Assert.False(loadedMissingSink.ContainsCode("INNOHR0002"));
            GameScene loadedMissingScene = savedMissingScene.Instantiate();
            SceneManager.LoadScene(loadedMissingScene);
            diagnosticWorkspace.Update(diagnosticContext);
            Assert.True(loadedMissingSink.ContainsCode("INNOHR0002"));
        }
        finally
        {
            DiagnosticManager.UnregisterSink(loadedMissingSink);
            diagnosticWorkspace.Stop(diagnosticContext);
        }
        GameScene restoredScene = SerializationManager.Deserialize<GameScene>(missingSceneData);
        SceneManager.LoadScene(restoredScene);
        GameObject restoredActor = restoredScene.GetObjects().Single(value => value.name == "Actor");
        GameObject restoredTarget = restoredScene.GetObjects().Single(value => value.name == "Target");
        Assert.IsType<MissingGameComponent>(restoredActor.GetComponents()
            .Single(component => component.identity.persistentId == componentId));
        Assert.IsType<MissingGameSystem>(restoredScene.GetSystems()
            .Single(system => system.identity.persistentId == systemId));

        WriteIdentityProbe(componentTypeId, systemTypeId);
        ScriptCompilationResult recoveryCompilation = Compile();
        Assert.True(recoveryCompilation.success, FormatDiagnostics(recoveryCompilation));
        var recoverySink = new TestDiagnosticSink();
        DiagnosticManager.RegisterSink(recoverySink);
        try
        {
            Assert.True(m_manager.ApplyPendingReload());
            Assert.False(recoverySink.ContainsCode("INNOHR0002"));
            Assert.False(recoverySink.ContainsCode("INNOHR0003"));
        }
        finally
        {
            DiagnosticManager.UnregisterSink(recoverySink);
        }

        GameComponent recovered = restoredActor.GetComponents()
            .Single(component => component.identity.persistentId == componentId);
        GameSystem recoveredSystem = restoredScene.GetSystems()
            .Single(system => system.identity.persistentId == systemId);
        Assert.Equal("IdentityProbe", recovered.GetType().Name);
        Assert.Equal("IdentityProbeSystem", recoveredSystem.GetType().Name);
        Assert.Equal(41, GetProperty(recovered, "value"));
        Assert.Same(restoredTarget, recovered.GetType().GetProperty("target")!.GetValue(recovered));
        Assert.Same(dependency, recovered.GetType().GetProperty("asset")!.GetValue(recovered));
        Assert.Equal(73, GetProperty(recoveredSystem, "value"));
    }

    [Fact]
    public void MissingTransitionsPreserveExistingDirtyStateAndRemainRecoverableAfterSavingOtherChanges()
    {
        const string componentTypeId = "1d930a18-6073-45d5-b600-229bac89382c";
        const string systemTypeId = "0c947cea-3cc4-47c3-8bb8-d5ef842af1bb";
        WriteIdentityProbe(componentTypeId, systemTypeId);
        Assert.True(Compile().success);
        Assert.True(m_manager.ApplyPendingReload());

        var workspace = new ReflectedSceneWorkspace();
        var context = new EditorContext(m_projectRoot);
        workspace.Start(context);
        try
        {
            GameScene scene = workspace.CreateScene();
            scene.name = "Missing Dirty State";
            GameObject actor = scene.CreateObject("Actor");
            GameComponent component = actor.AddComponent(ResolveTypeByName("IdentityProbe"));
            GameSystem system = scene.AddSystem(ResolveTypeByName("IdentityProbeSystem"));
            Guid componentId = component.identity.persistentId;
            Guid systemId = system.identity.persistentId;
            _ = workspace.Save(scene, "Scenes");
            Assert.False(workspace.IsDirty(scene));

            WriteIdentityProbe(
                "f67718d8-7376-4f8e-a685-e42a317024f4",
                "8dd959be-5a8e-4499-bbab-8ef015ed310c");
            Assert.True(Compile().success);
            Assert.True(m_manager.ApplyPendingReload());
            Assert.IsType<MissingGameComponent>(actor.GetComponents()
                .Single(value => value.identity.persistentId == componentId));
            Assert.IsType<MissingGameSystem>(scene.GetSystems()
                .Single(value => value.identity.persistentId == systemId));
            Assert.False(workspace.IsDirty(scene));

            _ = scene.CreateObject("Saved While Missing");
            Thread.Sleep(150);
            Assert.True(workspace.IsDirty(scene));
            _ = workspace.Save(scene, "Scenes");
            Assert.False(workspace.IsDirty(scene));

            _ = scene.CreateObject("Unsaved Before Recovery");
            Thread.Sleep(150);
            Assert.True(workspace.IsDirty(scene));

            WriteIdentityProbe(componentTypeId, systemTypeId);
            Assert.True(Compile().success);
            Assert.True(m_manager.ApplyPendingReload());
            Assert.Equal("IdentityProbe", actor.GetComponents()
                .Single(value => value.identity.persistentId == componentId).GetType().Name);
            Assert.Equal("IdentityProbeSystem", scene.GetSystems()
                .Single(value => value.identity.persistentId == systemId).GetType().Name);
            Thread.Sleep(150);
            Assert.True(workspace.IsDirty(scene));
        }
        finally
        {
            workspace.Stop(context);
        }
    }

    [Fact]
    public void RecoveringMissingTypesInASceneOpenedWhileTheyWereUnavailableDoesNotDirtyTheScene()
    {
        const string componentTypeId = "9f7f512e-33d0-4e83-95d1-52bbd114ed21";
        const string systemTypeId = "bf46939c-127b-45a0-8f8d-6d593dd3ed16";
        WriteIdentityProbe(componentTypeId, systemTypeId);
        Assert.True(Compile().success);
        Assert.True(m_manager.ApplyPendingReload());

        var workspace = new ReflectedSceneWorkspace();
        var context = new EditorContext(m_projectRoot);
        workspace.Start(context);
        try
        {
            GameScene source = workspace.CreateScene();
            source.name = "Opened Missing State";
            GameObject actor = source.CreateObject("Actor");
            _ = actor.AddComponent(ResolveTypeByName("IdentityProbe"));
            _ = source.AddSystem(ResolveTypeByName("IdentityProbeSystem"));
            string sourcePath = workspace.Save(source, "Scenes");

            WriteIdentityProbe(
                "f109df2f-0c6d-48b2-a44e-36c198bebeca",
                "3c815b71-799a-4d88-a546-e40b1334eb81");
            Assert.True(Compile().success);
            Assert.True(m_manager.ApplyPendingReload());
            Assert.True(workspace.CloseScene(source));

            GameScene openedMissing = workspace.Open(sourcePath);
            Assert.Contains(
                openedMissing.GetObjects().SelectMany(static value => value.GetComponents()),
                static value => value is MissingGameComponent);
            Assert.Contains(openedMissing.GetSystems(), static value => value is MissingGameSystem);
            Assert.False(workspace.IsDirty(openedMissing));

            WriteIdentityProbe(componentTypeId, systemTypeId, includeAddedProperty: true);
            Assert.True(Compile().success);
            Assert.True(m_manager.ApplyPendingReload());
            Thread.Sleep(150);

            Assert.Contains(
                openedMissing.GetObjects().SelectMany(static value => value.GetComponents()),
                static value => value.GetType().Name == "IdentityProbe");
            Assert.Contains(
                openedMissing.GetSystems(),
                static value => value.GetType().Name == "IdentityProbeSystem");
            Assert.False(workspace.IsDirty(openedMissing));
        }
        finally
        {
            workspace.Stop(context);
        }
    }

    [Fact]
    public void MissingPrefabState_RemapsReferenceAliasesAndRecoversAgainstTheInstantiatedGraph()
    {
        const string componentTypeId = "4cf986c5-9ab2-449b-bb72-16611bc7a1dc";
        const string systemTypeId = "e72c99dd-da6d-471e-a396-60a5fc12f1c2";
        WriteIdentityProbe(componentTypeId, systemTypeId);
        ScriptCompilationResult firstCompilation = Compile();
        Assert.True(firstCompilation.success, FormatDiagnostics(firstCompilation));
        Assert.True(m_manager.ApplyPendingReload());

        Type componentType = ResolveTypeByName("IdentityProbe");
        var sourceScene = new GameScene("Missing Prefab Source");
        GameObject sourceRoot = sourceScene.CreateObject("Root");
        GameObject sourceTarget = sourceScene.CreateObject("Target");
        sourceTarget.transform.SetParent(sourceRoot.transform);
        GameComponent sourceComponent = sourceRoot.AddComponent(componentType);
        SetProperty(sourceComponent, "value", 29);
        componentType.GetProperty("target")!.SetValue(sourceComponent, sourceTarget);
        SceneManager.LoadScene(sourceScene);

        WriteIdentityProbe(
            "2ff3c70d-4aca-4f46-b6fa-084582d96364",
            "ed01e4f6-fac2-4e13-b4a0-d12d35450aec");
        ScriptCompilationResult missingCompilation = Compile();
        Assert.True(missingCompilation.success, FormatDiagnostics(missingCompilation));
        Assert.True(m_manager.ApplyPendingReload());
        Assert.Contains(sourceRoot.GetComponents(), static component => component is MissingGameComponent);

        byte[] prefabData = SerializationManager.Serialize(sourceRoot);
        var targetScene = new GameScene("Missing Prefab Instance");
        GameObject instanceRoot = SerializationManager.Deserialize<GameObject>(
            prefabData,
            SerializationContext.empty.With(targetScene));
        SceneManager.LoadSceneAdditive(targetScene);
        Assert.Contains(instanceRoot.GetComponents(), static component => component is MissingGameComponent);

        WriteIdentityProbe(componentTypeId, systemTypeId);
        ScriptCompilationResult recoveryCompilation = Compile();
        Assert.True(recoveryCompilation.success, FormatDiagnostics(recoveryCompilation));
        Assert.True(m_manager.ApplyPendingReload());

        GameComponent recovered = instanceRoot.GetComponents()
            .Single(component => component.GetType().Name == "IdentityProbe");
        GameObject instanceTarget = Assert.Single(instanceRoot.transform.children).gameObject;
        Assert.Equal(29, GetProperty(recovered, "value"));
        Assert.Same(instanceTarget, recovered.GetType().GetProperty("target")!.GetValue(recovered));
        Assert.NotSame(sourceTarget, instanceTarget);
    }

    [Fact]
    public void MissingTypeRecoveryFailure_RollsBackToTheExactPlaceholderAndCanRetry()
    {
        const string componentTypeId = "c1329ee9-79c3-4414-99be-24ea95fc5c8e";
        const string systemTypeId = "c0ac7f51-89d1-48ea-9b73-2830684c6b3e";
        WriteIdentityProbe(componentTypeId, systemTypeId);
        Assert.True(Compile().success);
        Assert.True(m_manager.ApplyPendingReload());
        Type originalType = ResolveTypeByName("IdentityProbe");
        var scene = new GameScene("Missing Recovery Rollback");
        GameObject gameObject = scene.CreateObject("Actor");
        GameComponent original = gameObject.AddComponent(originalType);
        SetProperty(original, "value", 67);
        Guid componentId = original.identity.persistentId;
        SceneManager.LoadScene(scene);

        WriteIdentityProbe(
            "a5c14c91-5501-47d1-b90a-94a314423c99",
            "1a754422-e81b-4617-81ce-a4e180b7b4b6");
        Assert.True(Compile().success);
        Assert.True(m_manager.ApplyPendingReload());
        MissingGameComponent placeholder = Assert.IsType<MissingGameComponent>(
            gameObject.GetComponents().Single(component => component.identity.persistentId == componentId));

        WriteIdentityProbe(componentTypeId, systemTypeId, throwOnConstruction: true);
        Assert.True(Compile().success);
        WeakReference failedCandidateContext = ApplyFailedRecoveryAndCaptureCandidateContext();

        Assert.Same(
            placeholder,
            gameObject.GetComponents().Single(component => component.identity.persistentId == componentId));
        Assert.False(placeholder.isDestroyed);
        Assert.False(new TypeRef(Guid.Parse(componentTypeId)).isValid);
        ForceCollection();
        Assert.False(
            failedCandidateContext.IsAlive,
            "A rolled-back missing-script recovery retained its candidate ALC.");

        WriteIdentityProbe(componentTypeId, systemTypeId);
        Assert.True(Compile().success);
        Assert.True(m_manager.ApplyPendingReload());
        GameComponent recovered = gameObject.GetComponents()
            .Single(component => component.identity.persistentId == componentId);
        Assert.Equal("IdentityProbe", recovered.GetType().Name);
        Assert.Equal(67, GetProperty(recovered, "value"));
        Assert.True(placeholder.isDestroyed);
    }

    [Fact]
    public void MissingPlaceholders_DoNotRetainTheRetiredCollectibleLoadContext()
    {
        MissingGenerationReferences generation = CreateMissingGenerationAndRetireIt();

        ForceCollection();

        Assert.False(generation.context.IsAlive, "A missing placeholder retained its retired script ALC.");
        Assert.IsType<MissingGameComponent>(generation.gameObject.GetComponents()
            .Single(component => component.identity.persistentId == generation.componentId));
        Assert.IsType<MissingGameSystem>(generation.scene.GetSystems()
            .Single(system => system.identity.persistentId == generation.systemId));
    }

    [Fact]
    public void ScriptSourcesAreCatalogedWithMetadataAndImmutableSourceArtifacts()
    {
        Write("Scripts/Tracked.cs", "using InnoEngine.Scene; public sealed class Tracked : GameBehavior { }");

        ScriptCompilationResult result = Compile();

        Assert.True(result.success, FormatDiagnostics(result));
        Assert.True(File.Exists(Path.Combine(m_projectRoot, "Assets", "Scripts", "Tracked.cs.imeta")));
        Assert.True(AssetManager.TryGetInfo("Scripts/Tracked.cs", out AssetInfo? info));
        Assert.NotNull(info);
        Assert.Equal(AssetImportStatus.Imported, info.status);
        Assert.Equal("inno.editor.csharp-script", info.importerId);
        Assert.True(AssetManager.TryGetArtifact(info.persistentId, "source", out AssetArtifactInfo? source));
        Assert.NotNull(source);
        Assert.True(AssetManager.TryGetArtifact(
            info.persistentId,
            "type-manifest",
            out AssetArtifactInfo? typeManifest));
        Assert.NotNull(typeManifest);
        Guid manifestSourceId = SerializationManager.Decode(
            File.ReadAllBytes(typeManifest.absolutePath),
            static reader => reader.Read<Guid>("sourcePersistentId"));
        Assert.Equal(info.persistentId, manifestSourceId);
        Assert.Equal(
            File.ReadAllText(Path.Combine(m_projectRoot, "Assets", "Scripts", "Tracked.cs")),
            File.ReadAllText(source.absolutePath));
        Assert.NotNull(result.outputDirectory);
        Assert.True(File.Exists(Path.Combine(
            result.outputDirectory!,
            "Inno.GameScripts.types.inno")));
        Assert.DoesNotMatch(@"[/\\]ScriptAssemblies[/\\]\d+$", result.outputDirectory!);
    }

    [Fact]
    public void ImplicitAttachableTypeIdentitySurvivesFileAndTypeRename()
    {
        Write("RenameProbe.cs", """
            using InnoEngine.Scene;
            public sealed class RenameProbe : GameBehavior { }
            """);
        ScriptCompilationResult firstCompilation = Compile();
        Assert.True(firstCompilation.success, FormatDiagnostics(firstCompilation));
        Assert.True(m_manager.ApplyPendingReload());
        Type previous = ResolveTypeByName("RenameProbe");
        Guid previousStableTypeId = TypeCacheManager.GetTypeRef(previous).stableId;
        Assert.True(AssetManager.TryGetInfo("RenameProbe.cs", out AssetInfo? previousInfo));
        Assert.NotNull(previousInfo);
        var scene = new GameScene("Rename Probe");
        GameObject gameObject = scene.CreateObject("Actor");
        GameComponent previousComponent = gameObject.AddComponent(previous);
        SceneManager.LoadScene(scene);

        MoveWithMeta("RenameProbe.cs", "RenamedProbe.cs");
        Write("RenamedProbe.cs", """
            using InnoEngine.Scene;
            public sealed class RenamedProbe : GameBehavior { }
            """);
        ScriptCompilationResult secondCompilation = Compile();

        Assert.True(secondCompilation.success, FormatDiagnostics(secondCompilation));
        Assert.True(m_manager.ApplyPendingReload());
        Type current = ResolveTypeByName("RenamedProbe");
        Guid currentStableTypeId = TypeCacheManager.GetTypeRef(current).stableId;
        Assert.Equal(previousStableTypeId, currentStableTypeId);
        Assert.Equal(current, new TypeRef(previousStableTypeId).Resolve());
        GameComponent currentComponent = gameObject.GetComponents()
            .Single(component => component.GetType() == current);
        Assert.NotSame(previousComponent, currentComponent);
        Assert.True(previousComponent.isDestroyed);
        Assert.True(AssetManager.TryGetInfo("RenamedProbe.cs", out AssetInfo? currentInfo));
        Assert.NotNull(currentInfo);
        Assert.Equal(previousInfo.persistentId, currentInfo.persistentId);
    }

    [Fact]
    public void PartialAttachableTypeUsesOnlyItsMatchingCanonicalSource()
    {
        Write("PartialProbe.cs", """
            using InnoEngine.Scene;
            public sealed partial class PartialProbe : GameBehavior { }
            """);
        Write("PartialProbe.State.cs", """
            public sealed partial class PartialProbe
            {
                public int value => 7;
            }
            """);

        ScriptCompilationResult result = Compile();

        Assert.True(result.success, FormatDiagnostics(result));
        Assert.DoesNotContain(result.diagnostics, static diagnostic =>
            diagnostic.id is "INNO2001" or "INNO2003" or "INNO2004");
        Assert.True(m_manager.ApplyPendingReload());
        Type type = ResolveTypeByName("PartialProbe");
        Guid stableTypeId = TypeCacheManager.GetTypeRef(type).stableId;
        Assert.True(AssetManager.TryGetInfo("PartialProbe.cs", out AssetInfo? canonicalInfo));
        Assert.NotNull(canonicalInfo);
        Assert.NotEqual(Guid.Empty, stableTypeId);
        Assert.Equal(type, new TypeRef(stableTypeId).Resolve());
    }

    [Fact]
    public void AdditionalAttachableTypeWithoutCanonicalSourceFailsCompilation()
    {
        Write("PrimaryProbe.cs", """
            using InnoEngine.Scene;

            public sealed class PrimaryProbe : GameBehavior { }
            public sealed class SecondaryProbe : GameBehavior { }
            """);

        ScriptCompilationResult result = Compile();

        Assert.False(result.success);
        Assert.Contains(result.diagnostics, static diagnostic =>
            diagnostic.id == "INNO2001" &&
            diagnostic.severity == ScriptDiagnosticSeverity.Error &&
            diagnostic.message.Contains("SecondaryProbe", StringComparison.Ordinal));
    }

    [Fact]
    public void AssemblyDefinitionsControlScopeReferencesAndGeneratedIdeProjects()
    {
        WriteAssemblyDefinition(
            "Runtime/Runtime.iasmdef",
            "Project.Runtime",
            ScriptAssemblyScope.Runtime,
            defines: ["PROJECT_RUNTIME"]);
        Write("Runtime/RuntimeType.cs", "public sealed class RuntimeType { }");
        WriteAssemblyDefinition(
            "Editor/Editor.iasmdef",
            "Project.Editor",
            ScriptAssemblyScope.Editor,
            references: ["Project.Runtime"]);
        Write("Editor/EditorType.cs", "public sealed class EditorType { public RuntimeType value = new(); }");

        ScriptCompilationResult result = Compile();
        m_manager.GenerateProjectFiles();

        Assert.True(result.success, FormatDiagnostics(result));
        Assert.True(File.Exists(Path.Combine(result.outputDirectory!, "Project.Runtime.dll")));
        Assert.True(File.Exists(Path.Combine(result.outputDirectory!, "Project.Editor.dll")));
        string editorProject = File.ReadAllText(Path.Combine(m_projectRoot, "Project.Editor.csproj"));
        Assert.Contains("Project.Runtime.csproj", editorProject);
        Assert.Contains("Assets/Editor/EditorType.cs", editorProject);
        Assert.DoesNotContain("<Compile Include=\"Library", editorProject);
    }

    [Fact]
    public void IncrementalCompilation_RebuildsOnlyChangedAssemblyAndTransitiveDependents()
    {
        WriteAssemblyDefinition(
            "Core/Core.iasmdef",
            "Project.Core",
            ScriptAssemblyScope.Runtime);
        Write("Core/CoreValue.cs", "public static class CoreValue { public const int value = 1; }");
        WriteAssemblyDefinition(
            "Gameplay/Gameplay.iasmdef",
            "Project.Gameplay",
            ScriptAssemblyScope.Runtime,
            references: ["Project.Core"]);
        Write("Gameplay/GameplayValue.cs", "public static class GameplayValue { public const int value = CoreValue.value; }");
        WriteAssemblyDefinition(
            "Independent/Independent.iasmdef",
            "Project.Independent",
            ScriptAssemblyScope.Runtime);
        Write("Independent/IndependentValue.cs", "public static class IndependentValue { public const int value = 9; }");

        ScriptCompilationResult first = Compile();
        Assert.True(first.success, FormatDiagnostics(first));
        Assert.Contains("Project.Core", first.CompiledAssembliesForTest());
        Assert.Contains("Project.Gameplay", first.CompiledAssembliesForTest());
        Assert.Contains("Project.Independent", first.CompiledAssembliesForTest());

        Write("Core/CoreValue.cs", "public static class CoreValue { public const int value = 2; }");
        ScriptCompilationResult second = Compile();

        Assert.True(second.success, FormatDiagnostics(second));
        Assert.Contains("Project.Core", second.CompiledAssembliesForTest());
        Assert.Contains("Project.Gameplay", second.CompiledAssembliesForTest());
        Assert.DoesNotContain("Project.Independent", second.CompiledAssembliesForTest());
        Assert.Contains("Project.Independent", second.ReusedAssembliesForTest());
        Assert.Contains("Inno.GameScripts", second.ReusedAssembliesForTest());
        Assert.Contains("Inno.EditorScripts", second.ReusedAssembliesForTest());

        ScriptCompilationResult third = Compile();
        Assert.True(third.success, FormatDiagnostics(third));
        Assert.Empty(third.CompiledAssembliesForTest());
        Assert.Equal(
            first.CompiledAssembliesForTest().Count,
            third.ReusedAssembliesForTest().Count);
    }

    [Fact]
    public void ArtifactCacheCollectsExpiredAssemblyAndInterruptedStagingEntries()
    {
        string root = Path.Combine(m_projectRoot, "Library", "Artifacts", "ScriptAssemblies");
        string protectedGeneration = Path.Combine(root, new string('A', 64));
        string staleAssembly = Path.Combine(root, ".assemblies", new string('B', 64));
        string recentAssembly = Path.Combine(root, ".assemblies", new string('C', 64));
        string staleGenerationStaging = Path.Combine(root, ".staging", "generation");
        string staleAssemblyStaging = Path.Combine(root, ".assembly-staging", "assembly");
        string[] directories =
        [
            protectedGeneration,
            staleAssembly,
            recentAssembly,
            staleGenerationStaging,
            staleAssemblyStaging
        ];
        foreach (string directory in directories)
            Directory.CreateDirectory(directory);
        DateTime stale = DateTime.UtcNow - TimeSpan.FromDays(8);
        Directory.SetLastWriteTimeUtc(protectedGeneration, stale);
        Directory.SetLastWriteTimeUtc(staleAssembly, stale);
        Directory.SetLastWriteTimeUtc(staleGenerationStaging, stale);
        Directory.SetLastWriteTimeUtc(staleAssemblyStaging, stale);

        Type cacheType = typeof(ScriptManager).Assembly.GetType(
            "Inno.Editor.Scripting.ScriptArtifactCache",
            throwOnError: true)!;
        object cache = ScriptingTestReflection.Create(cacheType, root);
        int removed = ScriptingTestReflection.Invoke<int>(
            cache,
            "Collect",
            (object)new string?[] { protectedGeneration });

        Assert.Equal(3, removed);
        Assert.True(Directory.Exists(protectedGeneration));
        Assert.False(Directory.Exists(staleAssembly));
        Assert.True(Directory.Exists(recentAssembly));
        Assert.False(Directory.Exists(staleGenerationStaging));
        Assert.False(Directory.Exists(staleAssemblyStaging));
    }

    [Fact]
    public void CorruptedGenerationCacheIsReconstructedFromValidatedAssemblyArtifacts()
    {
        Write("CacheProbe.cs", "public sealed class CacheProbe { }");
        ScriptCompilationResult first = Compile();
        Assert.True(first.success, FormatDiagnostics(first));
        string gameAssembly = Path.Combine(first.outputDirectory!, "Inno.GameScripts.dll");
        File.WriteAllBytes(gameAssembly, [0, 1, 2, 3]);

        ScriptCompilationResult repaired = Compile();

        Assert.True(repaired.success, FormatDiagnostics(repaired));
        Assert.Equal("Inno.GameScripts", AssemblyName.GetAssemblyName(
            Path.Combine(repaired.outputDirectory!, "Inno.GameScripts.dll")).Name);
        Assert.Empty(repaired.CompiledAssembliesForTest());
        Assert.Contains("Inno.GameScripts", repaired.ReusedAssembliesForTest());
    }

    [Fact]
    public void FailedCompilationImmediatelyReleasesItsStagingDirectories()
    {
        Write("Broken.cs", "public sealed class Broken {");

        ScriptCompilationResult result = Compile();

        Assert.False(result.success);
        string root = Path.Combine(m_projectRoot, "Library", "Artifacts", "ScriptAssemblies");
        Assert.Empty(EnumerateDirectories(Path.Combine(root, ".staging")));
        Assert.Empty(EnumerateDirectories(Path.Combine(root, ".assembly-staging")));
    }

    [Fact]
    public void RuntimeAssemblyDefinitionCannotReferenceEditorAssembly()
    {
        WriteAssemblyDefinition(
            "Editor/Editor.iasmdef",
            "Project.Editor",
            ScriptAssemblyScope.Editor);
        Write("Editor/Tool.cs", "public sealed class Tool { }");
        WriteAssemblyDefinition(
            "Runtime/Runtime.iasmdef",
            "Project.Runtime",
            ScriptAssemblyScope.Runtime,
            references: ["Project.Editor"]);
        Write("Runtime/Game.cs", "public sealed class Game { }");

        ScriptCompilationResult result = Compile();

        Assert.False(result.success);
        Assert.Contains(result.diagnostics, static diagnostic =>
            diagnostic.message.Contains("cannot reference editor assembly", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void AssetManagerOptionsIsNotPartOfTheScriptFacade()
    {
        Write("HiddenHostOptions.cs", """
            using InnoEngine.Assets;
            public sealed class HiddenHostOptions
            {
                public AssetManagerOptions value;
            }
            """);

        ScriptCompilationResult result = Compile();

        Assert.False(result.success);
        Assert.Contains(result.diagnostics, static diagnostic =>
            diagnostic.id == "CS0246" &&
            diagnostic.message.Contains("AssetManagerOptions", StringComparison.Ordinal));
    }

    private void WriteVersionedBehavior(int version)
        => Write("VersionedBehavior.cs", $$"""
            using InnoEngine.Reflection;
            using InnoEngine.Scene;

            [StableTypeId("4bd6efba-f60e-4d7a-a508-f79a2278317a")]
            public sealed class VersionedBehavior : GameBehavior
            {
                public int version => {{version}};
            }
            """);

    private void WriteCandidateImporterScript(int generation, bool rejectFailureToken)
        => Write("CandidateImporter.cs", $$"""
            using InnoEngine.Assets;
            using InnoEngine.Reflection;
            using InnoEngine.Serialization;

            [StableTypeId("4342a609-aa4d-492a-b65e-823276041a31")]
            public sealed class CandidateImportedAsset : AssetObject
            {
                [SerializableProperty]
                public int importerGeneration { get; set; }

                [SerializableProperty]
                public string value { get; set; } = string.Empty;
            }

            [AssetImporterExtension]
            public sealed class CandidateImporter : AssetImporter<CandidateImportedAsset>
            {
                public override string importerId => "tests.candidate-importer";

                public override string[] supportedExtensions => [".candidate"];

                protected override async System.Threading.Tasks.ValueTask ImportAsync(
                    AssetImportContext context,
                    AssetImportWriter<CandidateImportedAsset> output,
                    System.Threading.CancellationToken cancellationToken)
                {
                    string content = context.ReadUtf8Text();
                    if ({{rejectFailureToken.ToString().ToLowerInvariant()}}
                        && string.Equals(content, "fail", System.StringComparison.Ordinal))
                    {
                        throw new System.IO.InvalidDataException("Injected candidate importer failure.");
                    }
                    output.SetAsset(new CandidateImportedAsset
                    {
                        importerGeneration = {{generation}},
                        value = content
                    });
                    await output.WriteArtifactAsync("runtime", context.sourceBytes, cancellationToken);
                }
            }
            """);

    private void WriteUnrelatedCandidateScript(int version)
        => Write("UnrelatedCandidateBehavior.cs", $$"""
            using InnoEngine.Reflection;

            [StableTypeId("8fd1b10a-ea68-4878-a885-8623aab5d8e8")]
            public sealed class UnrelatedCandidateBehavior
            {
                public int version => {{version}};
            }
            """);

    private ScriptCompilationResult Compile()
    {
        m_manager.RecompileScripting();
        Assert.True(m_manager.TryCompilePending(out Task<ScriptCompilationResult>? compilation));
        Assert.NotNull(compilation);
        return compilation.GetAwaiter().GetResult();
    }

    private void CompleteCompilationThroughEditorHost(ScriptCompilationResult compilation)
    {
        Type editorScriptingType = typeof(ScriptManager).Assembly.GetType(
            "Inno.Editor.Scripting.EditorScripting",
            throwOnError: true)!;
        object scripting = ScriptingTestReflection.Create(editorScriptingType);
        FieldInfo managerField = editorScriptingType.GetField(
            "m_manager",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        FieldInfo compilationField = editorScriptingType.GetField(
            "m_compilation",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        managerField.SetValue(scripting, m_manager);
        compilationField.SetValue(scripting, Task.FromResult(compilation));
        try
        {
            ScriptingTestReflection.Invoke(scripting, "CompleteCompilation");
        }
        finally
        {
            compilationField.SetValue(scripting, null);
            managerField.SetValue(scripting, null);
        }
    }

    private static IReadOnlyDictionary<string, int> GetReloadModuleGenerations()
        => AssemblyManager.modules
            .Where(static module => module.moduleName is "ProjectPlugins" or "RuntimeScripts" or "EditorScripts")
            .ToDictionary(static module => module.moduleName, static module => module.generation, StringComparer.Ordinal);

    private Exception? CompleteUnloadVerification()
    {
        for (int attempt = 0; attempt < 20; attempt++)
        {
            if (m_manager.AdvanceUnloadVerification(out Exception? failure))
                return failure;
        }
        throw new Xunit.Sdk.XunitException("Unload verification did not finish within its bounded attempt count.");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference CaptureLoadContext(string typeName)
    {
        Type type = ResolveTypeByName(typeName);
        AssemblyLoadContext context = AssemblyLoadContext.GetLoadContext(type.Assembly)!;
        Assert.True(context.IsCollectible);
        return new WeakReference(context);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static StrongTypeHolder CreateStrongTypeHolder(string typeName)
        => new(ResolveTypeByName(typeName));

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ClearStrongTypeHolder(StrongTypeHolder holder)
        => holder.type = null;

    private static bool UpdateUntil(EditorInteractionRuntime runtime, Func<bool> predicate)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(10);
        int frameIndex = 0;
        while (DateTime.UtcNow < deadline)
        {
            runtime.Update(new EditorFrame(0.016f, frameIndex * 0.016f, isFocused: true));
            if (predicate())
                return true;
            frameIndex++;
            System.Threading.Thread.Sleep(10);
        }
        return predicate();
    }

    private void WriteMigratingBehavior(int generation)
        => Write("MigratingBehavior.cs", $$"""
            using InnoEngine.Reflection;
            using InnoEngine.Scene;
            using InnoEngine.Serialization;

            [StableTypeId("c14f7138-0c5c-4e69-8376-cec8edc3056c")]
            public sealed class MigratingBehavior : GameBehavior
            {
                public static bool throwOnDisable;

                [SerializableProperty]
                public int value { get; set; }

                [SerializableProperty]
                public GameObject? target { get; set; }

                [SerializableProperty]
                public int awakeCount { get; set; }

                [SerializableProperty]
                public int startCount { get; set; }

                [SerializableProperty]
                public int enableCount { get; set; }

                [SerializableProperty]
                public int disableCount { get; set; }

                [SerializableProperty]
                public int updateCount { get; set; }

                public int generation => {{generation}};

                protected override void Awake() => awakeCount++;
                protected override void Start() => startCount++;
                protected override void OnEnable() => enableCount++;
                protected override void OnDisable()
                {
                    disableCount++;
                    if (throwOnDisable)
                        throw new System.InvalidOperationException("Injected disable failure.");
                }
                protected override void Update() => updateCount++;
            }

            [StableTypeId("ed10ef42-6a0f-4fd1-b178-8714a0d349d8")]
            public sealed class MigratingSystem : GameSystem
            {
                [SerializableProperty]
                public int value { get; set; }

                [SerializableProperty]
                public int awakeCount { get; set; }

                [SerializableProperty]
                public int startCount { get; set; }

                [SerializableProperty]
                public int enableCount { get; set; }

                [SerializableProperty]
                public int disableCount { get; set; }

                [SerializableProperty]
                public int updateCount { get; set; }

                public int generation => {{generation}};

                protected override void Awake() => awakeCount++;
                protected override void Start() => startCount++;
                protected override void OnEnable() => enableCount++;
                protected override void OnDisable() => disableCount++;
                protected override void OnUpdate() => updateCount++;
            }
            """);

    private void WriteIdentityProbe(
        string stableTypeId,
        string systemStableTypeId,
        bool throwOnConstruction = false,
        bool includeAddedProperty = false)
        => Write("IdentityProbe.cs", $$"""
            using InnoEngine.Assets;
            using InnoEngine.Reflection;
            using InnoEngine.Scene;
            using InnoEngine.Serialization;

            [StableTypeId("{{stableTypeId}}")]
            public sealed class IdentityProbe : GameBehavior
            {
                {{(throwOnConstruction ? "public IdentityProbe() => throw new System.InvalidOperationException(\"Injected constructor failure.\");" : string.Empty)}}

                [SerializableProperty]
                public int value { get; set; }

                [SerializableProperty]
                public GameObject? target { get; set; }

                [SerializableProperty]
                public AssetObject? asset { get; set; }

                {{(includeAddedProperty ? "[SerializableProperty] public int addedValue { get; set; } = 17;" : string.Empty)}}
            }

            [StableTypeId("{{systemStableTypeId}}")]
            public sealed class IdentityProbeSystem : GameSystem
            {
                [SerializableProperty]
                public int value { get; set; }

                {{(includeAddedProperty ? "[SerializableProperty] public int addedValue { get; set; } = 23;" : string.Empty)}}
            }
            """);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private MissingGenerationReferences CreateMissingGenerationAndRetireIt()
    {
        WriteIdentityProbe(
            "e9c4c719-ad2c-453c-a35a-a91e2af506ba",
            "49a0bc84-aac0-4918-b2e1-813fe3a0761e");
        Assert.True(Compile().success);
        Assert.True(m_manager.ApplyPendingReload());
        Type componentType = ResolveTypeByName("IdentityProbe");
        Type systemType = ResolveTypeByName("IdentityProbeSystem");
        AssemblyLoadContext context = AssemblyLoadContext.GetLoadContext(componentType.Assembly)!;
        var scene = new GameScene("Missing ALC");
        GameObject gameObject = scene.CreateObject("Actor");
        GameComponent component = gameObject.AddComponent(componentType);
        GameSystem system = scene.AddSystem(systemType);
        Guid componentId = component.identity.persistentId;
        Guid systemId = system.identity.persistentId;
        SceneManager.LoadScene(scene);

        WriteIdentityProbe(
            "de6a22ca-6669-4837-8aca-9a8ef332f028",
            "302432de-c60d-46fa-9e22-379802de8fe8");
        Assert.True(Compile().success);
        Assert.True(m_manager.ApplyPendingReload());
        return new MissingGenerationReferences(
            new WeakReference(context),
            scene,
            gameObject,
            componentId,
            systemId);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private WeakReference ApplyFailedRecoveryAndCaptureCandidateContext()
    {
        AssemblyLoadContext? candidateContext = null;
        AssemblyLoadEventHandler handler = (_, args) =>
        {
            if (!string.Equals(
                    args.LoadedAssembly.GetName().Name,
                    "Inno.GameScripts",
                    StringComparison.Ordinal))
            {
                return;
            }
            AssemblyLoadContext? context = AssemblyLoadContext.GetLoadContext(args.LoadedAssembly);
            if (context?.IsCollectible == true)
                candidateContext = context;
        };
        AppDomain.CurrentDomain.AssemblyLoad += handler;
        try
        {
            _ = Assert.ThrowsAny<Exception>(() => m_manager.ApplyPendingReload());
        }
        finally
        {
            AppDomain.CurrentDomain.AssemblyLoad -= handler;
        }
        return new WeakReference(Assert.IsAssignableFrom<AssemblyLoadContext>(candidateContext));
    }

    private void WriteChangingPropertyBehavior(bool useString)
        => Write("ChangingPropertyBehavior.cs", $$"""
            using InnoEngine.Reflection;
            using InnoEngine.Scene;
            using InnoEngine.Serialization;

            [StableTypeId("98f01b0c-8aa5-4f21-b160-0bd42d159247")]
            public sealed class ChangingPropertyBehavior : GameBehavior
            {
                [SerializableProperty]
                private {{(useString ? "string changed = \"default\";" : "int changed = 10;")}}

                [SerializableProperty]
                private int compatible = 20;
            }
            """);

    private void WriteHistoryHandler(int generation)
        => Write("HistoryHandler.editor.cs", $$"""
            using InnoEditor.Interactions;

            [EditorHistoryHandler("tests.script-history")]
            public sealed class ScriptHistoryHandler : EditorHistoryHandler
            {
                protected override EditorHistoryAvailability Query(
                    EditorHistoryContext context,
                    EditorHistoryChange change,
                    EditorHistoryDirection direction)
                    => EditorHistoryAvailability.Available();

                protected override EditorHistoryResult Apply(
                    EditorHistoryContext context,
                    EditorHistoryChange change,
                    EditorHistoryDirection direction)
                {
                    System.IO.File.WriteAllText(
                        System.IO.Path.Combine(context.editor.projectDirectory, "history-handler.txt"),
                        "{{generation}}:" + direction);
                    return EditorHistoryResult.Success();
                }
            }
            """);

    private void WriteExtensionLifecycleProbe(
        int generation,
        string moduleStoppedPath,
        string panelDetachedPath,
        string? logPath,
        bool failStart)
    {
        string moduleStopped = JsonSerializer.Serialize(moduleStoppedPath);
        string panelDetached = JsonSerializer.Serialize(panelDetachedPath);
        string log = JsonSerializer.Serialize(logPath ?? Path.Combine(m_projectRoot, "unused-lifecycle.log"));
        Write("ExtensionLifecycle.editor.cs", $$"""
            using InnoEditor.Core;

            [EditorModule("tests.lifecycle-order-module")]
            public sealed class LifecycleOrderModule : EditorModule
            {
                protected override void OnStart(EditorContext context)
                {
                    System.IO.File.AppendAllText({{log}}, "module-start-{{generation}};");
                    {{(generation > 1 ? $"if (!System.IO.File.Exists({moduleStopped}) || !System.IO.File.Exists({panelDetached})) throw new System.InvalidOperationException(\"Previous extensions were not retired before candidate start.\");" : string.Empty)}}
                    {{(failStart ? "throw new System.InvalidOperationException(\"Injected candidate module start failure.\");" : string.Empty)}}
                }

                protected override void OnStop(EditorContext context)
                {
                    System.IO.File.WriteAllText({{moduleStopped}}, "stopped");
                    System.IO.File.AppendAllText({{log}}, "module-stop-{{generation}};");
                }
            }

            [EditorPanel("tests.lifecycle-order", "Lifecycle Order", defaultOpen: false)]
            public sealed class LifecycleOrderPanel : EditorPanel
            {
                protected override void OnAttach(EditorContext context)
                {
                    System.IO.File.AppendAllText({{log}}, "panel-attach-{{generation}};");
                    {{(generation > 1 ? $"if (!System.IO.File.Exists({moduleStopped}) || !System.IO.File.Exists({panelDetached})) throw new System.InvalidOperationException(\"Previous extensions were not retired before candidate attach.\");" : string.Empty)}}
                }

                protected override void OnDetach(EditorContext context)
                {
                    System.IO.File.WriteAllText({{panelDetached}}, "detached");
                    System.IO.File.AppendAllText({{log}}, "panel-detach-{{generation}};");
                }

                protected override void OnDraw(EditorContext context)
                {
                }
            }
            """);
    }

    private void WriteReloadRetentionScripts(int generation)
    {
        Write("ReloadRetention.cs", $$"""
            using InnoEngine.Assets;
            using InnoEngine.Reflection;
            using InnoEngine.Serialization;

            [StableTypeId("3ba01d16-c29e-48bc-a5c9-7a4f3b4d8150")]
            public sealed class ReloadAsset : AssetObject
            {
                [SerializableProperty]
                public int generation { get; set; } = {{generation}};
            }

            [AssetImporterExtension]
            public sealed class ReloadAssetImporter : AssetImporter<ReloadAsset>
            {
                public override string importerId => "tests.reload-retention";
                public override string[] supportedExtensions => [".rasset"];

                protected override async System.Threading.Tasks.ValueTask ImportAsync(
                    AssetImportContext context,
                    AssetImportWriter<ReloadAsset> output,
                    System.Threading.CancellationToken cancellationToken)
                {
                    output.SetAsset(new ReloadAsset());
                    await output.WriteArtifactAsync("runtime", context.sourceBytes, cancellationToken);
                }
            }

            public static class ReloadObserver
            {
                public static void Install() => AssetManager.Changed += OnChanged;
                private static void OnChanged(AssetChangeSet changes)
                {
                }
            }
            """);
        Write("ReloadRetention.editor.cs", """
            using InnoEditor.Core;

            [EditorPanel("tests.reload-memory", "Reload Memory", defaultOpen: false)]
            public sealed class ReloadMemoryPanel : EditorPanel, IEditorPanelReloadState
            {
                protected override void OnDraw(EditorContext context)
                {
                }

                public System.ReadOnlyMemory<byte> CaptureReloadState()
                    => new ReloadMemoryManager().Memory;

                public void RestoreReloadState(System.ReadOnlyMemory<byte> state)
                {
                }
            }

            internal sealed class ReloadMemoryManager : System.Buffers.MemoryManager<byte>
            {
                private readonly byte[] m_bytes = [1, 2, 3];

                public override System.Span<byte> GetSpan() => m_bytes;
                public override System.Buffers.MemoryHandle Pin(int elementIndex = 0)
                    => throw new System.NotSupportedException();
                public override void Unpin()
                {
                }
                protected override void Dispose(bool disposing)
                {
                }
            }
            """);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ReloadGenerationReferences CaptureReloadGeneration(
        EditorInteractionRuntime runtime,
        int generation)
    {
        Type observer = ResolveTypeByName("ReloadObserver");
        observer.GetMethod("Install", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, null);
        AssetObject asset = AssetManager.Load<AssetObject>("Data/Reload.rasset");
        Assert.Equal("ReloadAsset", asset.GetType().Name);
        Assert.Equal(generation, asset.GetType().GetProperty("generation")!.GetValue(asset));
        AssemblyLoadContext context = AssemblyLoadContext.GetLoadContext(asset.GetType().Assembly)!;
        Assert.True(context.IsCollectible);
        EditorPanelExtension extension = Assert.Single(
            runtime.panels.Where(static panel => panel.id == "tests.reload-memory"));
        object panel = typeof(EditorPanelExtension)
            .GetField("m_panel", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(extension)!;
        return new ReloadGenerationReferences(
            new WeakReference(context),
            new WeakReference(asset),
            new WeakReference(panel),
            new WeakReference(observer));
    }

    private static AssetObject ResolveAssetReferenceFromTarget(
        object target,
        Guid persistentId,
        Guid stableTypeId,
        string lastKnownPath,
        Type expectedType,
        string propertyPath)
        => throw new NotSupportedException(target.GetType().FullName);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ForceCollection()
    {
        for (int i = 0; i < 10; i++)
        {
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
        }
    }

    private sealed record ReloadGenerationReferences(
        WeakReference context,
        WeakReference asset,
        WeakReference panel,
        WeakReference observerType);

    private sealed record SourcePluginFixture(
        string id,
        string[] dependencies,
        string sourceName,
        string source);

    private sealed record PluginArchiveFixture(
        string id,
        string[] dependencies,
        string[] assemblyDefinitions,
        IReadOnlyDictionary<string, byte[]> entries);

    private sealed record MissingGenerationReferences(
        WeakReference context,
        GameScene scene,
        GameObject gameObject,
        Guid componentId,
        Guid systemId);

    private sealed class FixtureRenderDiagnosticSink : IRenderDiagnosticSink
    {
        public void Publish(RenderDiagnostic diagnostic) => _ = diagnostic;
    }

    private class EmptyRenderResourceServiceProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            _ = args;
            if (targetMethod is null)
                throw new InvalidOperationException("A resource service proxy call requires method metadata.");
            if (targetMethod.Name == "get_capabilities")
            {
                return new GraphicsCapabilities(
                    GraphicsBackend.Noop,
                    GraphicsFeature.Compute,
                    new GraphicsLimits(64, 4, 4096, 8),
                    Enum.GetValues<RenderTextureFormat>(),
                    Enum.GetValues<RenderTextureFormat>(),
                    Enum.GetValues<RenderTextureFormat>(),
                    Enum.GetValues<RenderTextureFormat>(),
                    originBottomLeft: false,
                    homogeneousDepth: false);
            }
            return targetMethod.ReturnType == typeof(void)
                ? null
                : targetMethod.ReturnType.IsValueType
                    ? Activator.CreateInstance(targetMethod.ReturnType)
                    : null;
        }
    }

    private void Write(string relativePath, string content)
    {
        string path = Path.Combine(m_projectRoot, "Assets", relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private void WriteAssemblyDefinition(
        string relativePath,
        string assemblyName,
        ScriptAssemblyScope scope,
        string[]? references = null,
        string[]? defines = null)
    {
        var definition = new ScriptAssemblyDefinitionAsset(
            assemblyName,
            scope,
            references,
            defines);
        string path = Path.Combine(m_projectRoot, "Assets", relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, NativeAssetSourceSerialization.Export(definition));
    }

    private void InstallSourcePlugin(bool editorOnly = false)
    {
        const string pluginId = "tests.scripting";
        string sourceName = editorOnly ? "PluginValue.editor.cs" : "PluginValue.cs";
        Write(sourceName, """
            namespace ProjectPluginApi;

            public static class PluginValue
            {
                public const int value = 42;
            }

            public sealed class PluginObject
            {
                public int value => PluginValue.value;
            }
            """);
        Assert.True(AssetManager.Import(sourceName));
        string projectSource = Path.Combine(m_projectRoot, "Assets", sourceName);
        byte[] sourceBytes = File.ReadAllBytes(projectSource);
        byte[] metadataBytes = File.ReadAllBytes(projectSource + ".imeta");
        File.Delete(projectSource);
        File.Delete(projectSource + ".imeta");
        AssetManager.Rescan();

        string pluginRoot = Path.Combine(m_projectRoot, "Plugins");
        Directory.CreateDirectory(pluginRoot);
        string archivePath = Path.Combine(pluginRoot, "ScriptingTests.zip");
        using (FileStream stream = File.Create(archivePath))
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
        {
            WritePluginEntry(archive, "Plugin.inno", SerializationManager.Serialize(new PluginManifest
            {
                pluginId = pluginId,
                displayName = "Scripting Tests"
            }));
            WritePluginEntry(archive, "Assets/" + sourceName, sourceBytes);
            WritePluginEntry(archive, "Assets/" + sourceName + ".imeta", metadataBytes);
        }

        var archives = new PluginArchiveService(
            pluginRoot,
            Path.Combine(m_projectRoot, "Library"));
        PluginScanResult scan = archives.Scan(new HashSet<string>([pluginId], StringComparer.Ordinal));
        Assert.Empty(scan.diagnostics);
        PluginCatalog.Activate(scan);
        AssetSourceMount projectMount = AssetManager.sourceMounts.Single(
            static mount => mount.id == AssetSourceId.project);
        AssetManager.ReplaceSourceMounts(
            [projectMount, .. PluginArchiveService.GetActivatableMounts(scan)]);
    }

    private void InstallSourcePlugins(params SourcePluginFixture[] plugins)
    {
        var archives = new List<PluginArchiveFixture>();
        foreach (SourcePluginFixture plugin in plugins)
        {
            string stagingPath = "PluginStaging/" + plugin.id + "/" + plugin.sourceName;
            Write(stagingPath, plugin.source);
            (byte[] source, byte[] metadata) = CaptureProjectSource(stagingPath);
            archives.Add(new PluginArchiveFixture(
                plugin.id,
                plugin.dependencies,
                [],
                new Dictionary<string, byte[]>
                {
                    ["Assets/" + plugin.sourceName] = source,
                    ["Assets/" + plugin.sourceName + ".imeta"] = metadata
                }));
        }
        InstallPluginArchives(archives);
    }

    private (byte[] source, byte[] metadata) CaptureProjectSource(string relativePath)
    {
        Assert.True(AssetManager.Import(relativePath));
        string projectSource = Path.Combine(m_projectRoot, "Assets", relativePath);
        byte[] source = File.ReadAllBytes(projectSource);
        byte[] metadata = File.ReadAllBytes(projectSource + ".imeta");
        File.Delete(projectSource);
        File.Delete(projectSource + ".imeta");
        AssetManager.Rescan();
        return (source, metadata);
    }

    private void InstallPluginArchives(IReadOnlyList<PluginArchiveFixture> plugins)
    {
        string pluginRoot = Path.Combine(m_projectRoot, "Plugins");
        Directory.CreateDirectory(pluginRoot);
        foreach (PluginArchiveFixture plugin in plugins)
        {
            string archivePath = Path.Combine(pluginRoot, plugin.id + ".zip");
            using FileStream stream = File.Create(archivePath);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
            WritePluginEntry(archive, "Plugin.inno", SerializationManager.Serialize(new PluginManifest
            {
                pluginId = plugin.id,
                displayName = plugin.id,
                dependencies = plugin.dependencies,
                assemblyDefinitions = plugin.assemblyDefinitions
            }));
            foreach ((string path, byte[] bytes) in plugin.entries)
                WritePluginEntry(archive, path, bytes);
        }

        var archiveService = new PluginArchiveService(
            pluginRoot,
            Path.Combine(m_projectRoot, "Library"));
        PluginScanResult scan = archiveService.Scan(
            plugins.Select(static plugin => plugin.id).ToHashSet(StringComparer.Ordinal));
        Assert.Empty(scan.diagnostics);
        PluginCatalog.Activate(scan);
        AssetSourceMount projectMount = AssetManager.sourceMounts.Single(
            static mount => mount.id == AssetSourceId.project);
        AssetManager.ReplaceSourceMounts(
            [projectMount, .. PluginArchiveService.GetActivatableMounts(scan)]);
    }

    private void InstallProgrammableRenderingPlugin()
    {
        const string pluginId = "tests.rendering-fixture";
        const string sourceName = "ProgrammableRenderingFixture.cs";
        const string editorSourceName = "ProgrammableRenderingFixture.editor.cs";
        Write(sourceName, """
            using System.Collections.Generic;
            using InnoEngine.Graphs;
            using InnoEngine.Reflection;
            using InnoEngine.Rendering;
            using InnoEngine.Rendering.ShaderGraph;
            using InnoEngine.Scene;

            [StableTypeId("85070103-bc64-4461-b197-105cbeea7a8b")]
            public sealed class FixtureRenderComponent : GameBehavior
            {
            }

            [RenderPipelineExtension("tests.fixture.pipeline")]
            public sealed class FixtureRenderPipeline : RenderPipeline
            {
                private static readonly RenderPhaseId s_raster = new("tests.fixture.raster");
                private static readonly RenderPhaseId s_compute = new("tests.fixture.compute");

                public override void Build(RenderPipelineContext context)
                {
                    context.graph
                        .AddRasterPass(
                            "Fixture Clear Triangle",
                            s_raster,
                            0,
                            static (_, pass) => pass.commands.DrawProcedural(3))
                        .ClearPresentationTarget(new RenderClearColor(0.05f, 0.1f, 0.2f, 1f))
                        .HasSideEffect();
                    context.graph
                        .AddComputePass(
                            "Fixture Compute",
                            s_compute,
                            0,
                            static (_, pass) => pass.commands.Dispatch(1, 1, 1))
                        .After(s_raster)
                        .HasSideEffect();
                }
            }

            [ShaderNodeExtension("tests.fixture.compute-value")]
            public sealed class FixtureComputeValueNode : ShaderNodeDefinition
            {
                private static readonly GraphPortId s_output = new("value");

                public FixtureComputeValueNode()
                    : base(
                        "tests.fixture.compute-value",
                        "Fixture Compute Value",
                        "Tests/Fixture",
                        ShaderStage.Compute)
                {
                }

                public override IReadOnlyList<GraphPortDefinition> GetPorts(GraphNodeRecord node)
                {
                    _ = node;
                    return
                    [
                        new GraphPortDefinition(
                            s_output,
                            "Value",
                            ShaderGraphValueTypes.GetId(ShaderValueType.Float),
                            GraphPortDirection.Output)
                    ];
                }

                public override void Emit(ShaderNodeEmitContext context)
                    => context.SetOutput(
                        s_output,
                        new ShaderValue(ShaderValueType.Float, "1.0", context.node.id));
            }
            """);
        Write(editorSourceName, """
            using InnoEditor.Rendering;
            using InnoEngine.Rendering;

            [EditorViewportProviderExtension(
                "tests.fixture.viewport-provider",
                "tests.fixture.viewport")]
            public sealed class FixtureViewportProvider : EditorViewportProvider
            {
                private static readonly RenderDataChannelId s_size =
                    new("tests.fixture.viewport-size");

                public static int toolbarDrawCount;
                public static float lastPointerX;
                public static float lastPointerY;
                public static int lastPointerButton;

                public override EditorViewportSubmission Build(EditorViewportContext context)
                {
                    var data = new RenderFrameData();
                    data.Set(s_size, $"{context.pixelWidth}x{context.pixelHeight}");
                    return new EditorViewportSubmission(
                        data,
                        targetFormat: RenderTextureFormat.RGBA16Float,
                        priority: 17);
                }

                public override void DrawToolbar(EditorViewportContext context)
                {
                    _ = context;
                    toolbarDrawCount++;
                }

                public override void HandlePointer(EditorViewportPointerContext context)
                {
                    lastPointerX = context.x;
                    lastPointerY = context.y;
                    lastPointerButton = context.button;
                }
            }
            """);
        Assert.True(AssetManager.Import(sourceName));
        Assert.True(AssetManager.Import(editorSourceName));
        string projectSource = Path.Combine(m_projectRoot, "Assets", sourceName);
        string projectEditorSource = Path.Combine(m_projectRoot, "Assets", editorSourceName);
        byte[] sourceBytes = File.ReadAllBytes(projectSource);
        byte[] metadataBytes = File.ReadAllBytes(projectSource + ".imeta");
        byte[] editorSourceBytes = File.ReadAllBytes(projectEditorSource);
        byte[] editorMetadataBytes = File.ReadAllBytes(projectEditorSource + ".imeta");
        File.Delete(projectSource);
        File.Delete(projectSource + ".imeta");
        File.Delete(projectEditorSource);
        File.Delete(projectEditorSource + ".imeta");
        AssetManager.Rescan();

        string pluginRoot = Path.Combine(m_projectRoot, "Plugins");
        Directory.CreateDirectory(pluginRoot);
        string archivePath = Path.Combine(pluginRoot, "ProgrammableRenderingFixture.zip");
        using (FileStream stream = File.Create(archivePath))
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
        {
            WritePluginEntry(archive, "Plugin.inno", SerializationManager.Serialize(new PluginManifest
            {
                pluginId = pluginId,
                displayName = "Programmable Rendering Fixture"
            }));
            WritePluginEntry(archive, "Assets/" + sourceName, sourceBytes);
            WritePluginEntry(archive, "Assets/" + sourceName + ".imeta", metadataBytes);
            WritePluginEntry(archive, "Assets/" + editorSourceName, editorSourceBytes);
            WritePluginEntry(archive, "Assets/" + editorSourceName + ".imeta", editorMetadataBytes);
        }

        string libraryRoot = Path.Combine(m_projectRoot, "Library");
        var archives = new PluginArchiveService(pluginRoot, libraryRoot);
        PluginScanResult untrusted = archives.Scan(new HashSet<string>(StringComparer.Ordinal));
        Assert.Empty(PluginArchiveService.GetActivatableCandidates(untrusted));
        Assert.Contains(untrusted.diagnostics, static diagnostic =>
            diagnostic.message.Contains("trust", StringComparison.OrdinalIgnoreCase));
        PluginManager.Initialize(pluginRoot, libraryRoot, untrusted);
        PluginManager.SetTrusted(pluginId, trusted: true);
        Assert.True(PluginManager.hasPendingActivation);
    }

    private static void WritePluginEntry(ZipArchive archive, string path, byte[] bytes)
    {
        ZipArchiveEntry entry = archive.CreateEntry(path, CompressionLevel.Optimal);
        using Stream output = entry.Open();
        output.Write(bytes);
    }

    private void MoveWithMeta(string sourceRelativePath, string destinationRelativePath)
    {
        string sourcePath = Path.Combine(m_projectRoot, "Assets", sourceRelativePath);
        string destinationPath = Path.Combine(m_projectRoot, "Assets", destinationRelativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        File.Move(sourcePath, destinationPath);
        File.Move(sourcePath + ".imeta", destinationPath + ".imeta");
    }

    private static Type ResolveVersionedBehavior()
        => ResolveTypeByName("VersionedBehavior");

    private static Type ResolveTypeByName(string name)
        => TypeCacheManager.current.types.Single(type => type.Resolve().Name == name).Resolve();

    private static bool ContainsType(string name)
        => TypeCacheManager.current.types.Any(type => type.Resolve().Name == name);

    private static int ReadVersion(Type type)
        => (int)type.GetProperty("version")!.GetValue(Activator.CreateInstance(type))!;

    private static IReadOnlyList<GameComponent> GetComponents(GameObject gameObject, Type componentType)
    {
        MethodInfo getComponents = typeof(GameObject).GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Single(static method =>
                method.Name == nameof(GameObject.GetComponents) &&
                method.IsGenericMethodDefinition &&
                method.GetParameters().Length == 0);
        object result = getComponents.MakeGenericMethod(componentType).Invoke(gameObject, null)!;
        return ((IEnumerable<GameComponent>)result).ToArray();
    }

    private static AssetObject LoadAsset(string relativePath, Type assetType)
    {
        MethodInfo load = typeof(AssetManager).GetMethods(BindingFlags.Static | BindingFlags.Public)
            .Single(static method =>
                method.Name == nameof(AssetManager.Load) &&
                method.IsGenericMethodDefinition &&
                method.GetParameters() is [{ ParameterType: var parameterType }] &&
                parameterType == typeof(string));
        return (AssetObject)load.MakeGenericMethod(assetType).Invoke(null, [relativePath])!;
    }

    private sealed class StrongTypeHolder(Type type)
    {
        internal Type? type = type;
    }

    private static int GetProperty(object target, string propertyName)
        => (int)target.GetType().GetProperty(propertyName)!.GetValue(target)!;

    private static void SetProperty(object target, string propertyName, int value)
        => target.GetType().GetProperty(propertyName)!.SetValue(target, value);

    private static object? GetField(object target, string fieldName)
        => target.GetType().GetField(
            fieldName,
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target);

    private static void SetField(object target, string fieldName, object value)
        => target.GetType().GetField(
            fieldName,
            BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);

    private static TestEditorState CaptureExtensionState(EditorModule module)
    {
        var state = new TestEditorState();
        MethodInfo capture = module.GetType().GetMethod(
            "Capture",
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            [typeof(EditorState)],
            modifiers: null)!;
        _ = capture.Invoke(module, [state]);
        return state;
    }

    private static void RestoreExtensionState(EditorModule module, EditorState state)
    {
        MethodInfo restore = module.GetType().GetMethod(
            "Restore",
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            [typeof(EditorState)],
            modifiers: null)!;
        _ = restore.Invoke(module, [state]);
    }

    private sealed class TestEditorState : EditorState
    {
        private readonly Dictionary<string, object?> m_values = new(StringComparer.Ordinal);

        public override T Get<T>(string key, T fallback)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(key);
            return m_values.TryGetValue(key, out object? value) && value is T compatible
                ? compatible
                : fallback;
        }

        public override void Set<T>(string key, T value)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(key);
            m_values[key] = value;
        }
    }

    private static object CreateInspectorTarget(string typeName, params object[] arguments)
    {
        Type type = typeof(AssetReferenceDropTarget).Assembly.GetType(typeName, throwOnError: true)!;
        return Activator.CreateInstance(
                   type,
                   BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                   binder: null,
                   args: arguments,
                   culture: null)
               ?? throw new InvalidOperationException($"Could not create '{typeName}'.");
    }

    private static T GetProperty<T>(object target, string propertyName)
        => Assert.IsType<T>(target.GetType().GetProperty(
            propertyName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(target));

    private static T GetStaticField<T>(Type type, string fieldName)
        => Assert.IsType<T>(type.GetField(
            fieldName,
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(null));

    private static void SetProperty<T>(object target, string propertyName, T value)
        => target.GetType().GetProperty(
            propertyName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.SetValue(target, value);

    private EditorInteractionRuntime CreateSettingsRuntime(out EditorSettings settings)
    {
        SettingsCaptureModule.current = null;
        var runtime = new EditorInteractionRuntime(m_projectRoot);
        try
        {
            runtime.Start();
            settings = SettingsCaptureModule.current
                ?? throw new InvalidOperationException("The Settings module was not discovered.");
            return runtime;
        }
        catch
        {
            runtime.Dispose();
            throw;
        }
    }

    private IDisposable CreateAssetIconRegistry()
    {
        EditorInteractionRuntime runtime = CreateSettingsRuntime(out EditorSettings settings);
        Type? registryType = typeof(AssetIconAttribute).Assembly.GetType(
            "Inno.Editor.Panel.FileBrowser.AssetIconRegistry",
            throwOnError: true);
        Assert.NotNull(registryType);
        object? registry = Activator.CreateInstance(
            registryType!,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            args: [settings],
            culture: null);
        Assert.NotNull(registry);
        return new AssetIconRegistryScope(
            Assert.IsAssignableFrom<IDisposable>(registry),
            runtime);
    }

    private static string ResolveAssetIcon(
        IDisposable registry,
        Type? assetType,
        string relativePath = "Assets/Unknown.unknown")
    {
        Assert.True(TryResolveAssetIcon(registry, assetType, relativePath, out string icon));
        return icon;
    }

    private static bool TryResolveAssetIcon(
        IDisposable registry,
        Type? assetType,
        string relativePath,
        out string icon)
    {
        object target = registry is AssetIconRegistryScope scope ? scope.registry : registry;
        MethodInfo? resolveIcon = target.GetType().GetMethod(
            "TryResolve",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(resolveIcon);
        object?[] arguments = [assetType, relativePath, null];
        bool resolved = Assert.IsType<bool>(resolveIcon!.Invoke(target, arguments));
        icon = Assert.IsType<string>(arguments[2]);
        return resolved;
    }

    private static Type GetImGuiIconType()
        => Assembly.Load("Inno.Platform.ImGui").GetType(
            "Inno.Platform.ImGui.ImGuiIcon",
            throwOnError: true)!;

    private static string ResolveImGuiIcon(string name)
        => Assert.IsType<string>(GetImGuiIconType().GetField(
            name,
            BindingFlags.Public | BindingFlags.Static)!.GetRawConstantValue());

    [EditorModule("tests.settings-capture", order: int.MaxValue)]
    private sealed class SettingsCaptureModule(EditorSettings settings) : EditorModule
    {
        internal static EditorSettings? current;

        protected override void OnStart(EditorContext context)
            => current = settings;
    }

    private sealed class AssetIconRegistryScope(
        IDisposable registryValue,
        EditorInteractionRuntime runtime) : IDisposable
    {
        internal IDisposable registry { get; } = registryValue;

        public void Dispose()
        {
            registry.Dispose();
            runtime.Dispose();
        }
    }

    private static string FormatDiagnostics(ScriptCompilationResult result)
        => string.Join(
            Environment.NewLine,
            result.diagnostics.Select(diagnostic =>
                $"{diagnostic.id}: {diagnostic.filePath}({diagnostic.line},{diagnostic.column}) {diagnostic.message}"));

    private static IReadOnlyList<string> EnumerateDirectories(string path)
        => Directory.Exists(path) ? Directory.EnumerateDirectories(path).ToArray() : [];

    private sealed class TestDiagnosticSink : IDiagnosticSink
    {
        private readonly object m_sync = new();
        private readonly System.Collections.Generic.Dictionary<string, DiagnosticReport> m_reports =
            new(StringComparer.Ordinal);

        internal bool ContainsCode(string code)
        {
            lock (m_sync)
            {
                return m_reports.Values.Any(
                    report => report.diagnostics.Any(
                        diagnostic => string.Equals(diagnostic.code, code, StringComparison.Ordinal)));
            }
        }

        internal bool ContainsDiagnostic(string code, DiagnosticSeverity severity)
        {
            lock (m_sync)
            {
                return m_reports.Values.Any(
                    report => report.diagnostics.Any(
                        diagnostic =>
                            string.Equals(diagnostic.code, code, StringComparison.Ordinal) &&
                            diagnostic.severity == severity));
            }
        }

        internal IReadOnlyList<string> GetMessages(string code)
        {
            lock (m_sync)
            {
                return m_reports.Values
                    .SelectMany(static report => report.diagnostics)
                    .Where(diagnostic => string.Equals(diagnostic.code, code, StringComparison.Ordinal))
                    .Select(static diagnostic => diagnostic.message)
                    .ToArray();
            }
        }

        internal void ClearPresentation()
        {
            lock (m_sync)
                m_reports.Clear();
        }

        public void Replace(DiagnosticReport report)
        {
            lock (m_sync)
                m_reports[report.source.id] = report;
        }

        public void Clear(DiagnosticSource source)
        {
            lock (m_sync)
                m_reports.Remove(source.id);
        }
    }

    private sealed class TestEditorRenderingHost : IEditorRenderingHost
    {
        internal EditorViewportRequest? lastRequest { get; private set; }

        internal List<string> releasedViewportIds { get; } = [];

        public EditorViewportOutput Submit(EditorViewportRequest request)
        {
            lastRequest = request;
            return new EditorViewportOutput(
                request.viewportId,
                new ImGuiTextureHandle(1),
                request.pixelWidth,
                request.pixelHeight);
        }

        public void Draw(EditorViewportOutput output, System.Numerics.Vector2 logicalSize)
        {
            _ = output;
            _ = logicalSize;
        }

        public void Release(string viewportId) => releasedViewportIds.Add(viewportId);

        public void ReleaseAll()
        {
        }
    }
}

[StableTypeId("753b0a86-dffc-4ac5-bb12-f4ad20179ea0")]
internal sealed class HistoryTestComponent : GameComponent
{
    [SerializableProperty]
    public int value { get; set; }

    protected override void Reset() => value = 7;
}

[StableTypeId("ae8468c3-b20a-44ed-916f-172d0244ed51")]
internal sealed class HistoryTestSystem : GameSystem
{
    [SerializableProperty]
    public int value { get; set; }
}

[StableTypeId("6e40313b-5b67-46f4-9ad4-19c6ef7db20e")]
internal sealed class SceneAssetDirtyProbe : GameComponent
{
    [SerializableProperty]
    public SceneAsset? sceneAsset { get; set; }
}
