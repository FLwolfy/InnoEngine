using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Inno.Assets;
using Inno.Assets.Core;
using Inno.Assets.File;
using Inno.Assets.Loader;
using Inno.Assets.Types;
using Inno.Core.Assemblies;
using Inno.Core.Diagnose;
using Inno.Core.Events;
using Inno.Core.Identity;
using Inno.Core.Input;
using Inno.Core.Reflection;
using Inno.Core.Serialization;
using Inno.Editor.Core;
using Inno.Editor.Interactions;
using Inno.Editor.ImGui;
using Inno.Editor.ImGui.ImGuiWidget;
using EditorWidget = Inno.Editor.ImGui.ImGuiWidget.ImGuiWidget;
using Inno.Editor.Inspection;
using Inno.Editor.Panel.FileBrowser;
using Inno.Editor.Panel.Hierarchy;
using Inno.Editor.Panel.Inspector;
using Inno.Editor.Scene;
using Inno.Editor.Settings;
using Inno.Editor.Scripting;
using Inno.Engine.Scene;
using Inno.Engine.Scene.Assets;
using Inno.Engine.Scene.Layers;

using Xunit;

namespace Inno.Editor.Scripting.Tests;

public sealed class ScriptManagerTests : IDisposable
{
    private readonly string m_projectRoot = Path.Combine(
        Path.GetTempPath(),
        "InnoScriptManagerTests",
        Guid.NewGuid().ToString("N"));
    private readonly ScriptManager m_manager;

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
        _ = Assembly.Load("Inno.Editor.Panel.Settings");
        IdentityManager.Initialize();
        AssemblyManager.Initialize(new AssemblyManagerOptions
        {
            cacheDirectory = Path.Combine(m_projectRoot, "Library", "Assemblies")
        });
        TypeCacheManager.Initialize();
        SerializationManager.Initialize();
        AssetManager.Initialize(AssetManagerOptions.Create(
            Path.Combine(m_projectRoot, "Assets"),
            Path.Combine(m_projectRoot, "Library")) with
        {
            enableFileSystemWatcher = false
        });
        m_manager = new ScriptManager(new ScriptManagerOptions
        {
            projectRootDirectory = m_projectRoot,
            autoCompile = false,
            debounceMilliseconds = 0
        });
    }

    public void Dispose()
    {
        SceneManager.UnloadAllScenes();
        m_manager.Dispose();
        AssetManager.Shutdown();
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

        Assert.True(automaticManager.TryCompilePending(out Task<ScriptCompilationResult>? compilation));
        Assert.NotNull(compilation);
        ScriptCompilationResult result = await compilation!;
        Assert.True(result.success, FormatDiagnostics(result));
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
        using var manager = new ScriptManager(
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
        Type behavior = TypeCacheManager.current.types.Single(type => type.Name == "ProjectBehavior");
        Type drawer = TypeCacheManager.current.types.Single(type => type.Name == "ProjectBehaviorDrawer");
        Assert.Equal(AssemblyGroup.Game, behavior.Assembly.GetInnoAssemblyGroup());
        Assert.Equal(AssemblyGroup.Editor, drawer.Assembly.GetInnoAssemblyGroup());
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
        var workspace = new EditorSceneWorkspace();
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
        var workspace = new EditorSceneWorkspace();
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
        var workspace = new EditorSceneWorkspace(runtime.interactions);
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
            var workspace = new EditorSceneWorkspace(runtime.interactions);
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
    public void GameLayersPersistThroughProjectSettingsWithoutAssetRegistration()
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
            .Execute("inspector/add-component", typeof(HistoryTestComponent)));
        Assert.NotNull(gameObject.GetComponent<HistoryTestComponent>());

        Assert.True(runtime.interactions
            .For("panel/scene.inspector/system", scene)
            .Execute("inspector/add-system", typeof(HistoryTestSystem)));
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
        var edits = new SceneEdits(new EditorSceneWorkspace(runtime.interactions), runtime.interactions);
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
        var workspace = new EditorSceneWorkspace();
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
        var workspace = new EditorSceneWorkspace();
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
        var workspace = new EditorSceneWorkspace();
        var context = new EditorContext(m_projectRoot);
        workspace.Start(context);

        RestoreExtensionState(workspace, new TestEditorState());
        workspace.Update(context);

        Assert.Empty(SceneManager.loadedScenes);
        Assert.Null(workspace.activeScene);
        workspace.Stop(context);
    }

    [Fact]
    public void SceneModuleStateRestoresSavedScenesInOrderAndSelectsTheActiveScene()
    {
        var sourceWorkspace = new EditorSceneWorkspace();
        GameScene first = sourceWorkspace.CreateScene();
        first.name = "First";
        _ = sourceWorkspace.Save(first, "Scenes");
        GameScene second = sourceWorkspace.CreateScene();
        second.name = "Second";
        _ = sourceWorkspace.Save(second, "Scenes");
        SceneManager.SetActiveScene(first);
        TestEditorState state = CaptureExtensionState(sourceWorkspace);

        SceneManager.UnloadAllScenes();
        var restoredWorkspace = new EditorSceneWorkspace();
        var context = new EditorContext(m_projectRoot);
        restoredWorkspace.Start(context);
        RestoreExtensionState(restoredWorkspace, state);
        restoredWorkspace.Update(context);

        Assert.Equal(["First", "Second"], restoredWorkspace.scenes.Select(static scene => scene.name));
        Assert.Equal("First", restoredWorkspace.activeScene!.name);
        restoredWorkspace.Stop(context);
    }

    [Fact]
    public void SceneModuleStateReloadsLastSavedDataInsteadOfDirtyMemory()
    {
        var sourceWorkspace = new EditorSceneWorkspace();
        GameScene scene = sourceWorkspace.CreateScene();
        scene.name = "Saved";
        _ = scene.CreateObject("Saved Object");
        _ = sourceWorkspace.Save(scene, "Scenes");
        _ = scene.CreateObject("Unsaved Object");
        TestEditorState state = CaptureExtensionState(sourceWorkspace);

        SceneManager.UnloadAllScenes();
        var restoredWorkspace = new EditorSceneWorkspace();
        var context = new EditorContext(m_projectRoot);
        restoredWorkspace.Start(context);
        RestoreExtensionState(restoredWorkspace, state);
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
        Assert.True(TypeCacheManager.TryGetRuntimeTypeId(first, out int firstRuntimeId));

        WriteVersionedBehavior(2);
        ScriptCompilationResult secondCompilation = Compile();
        Assert.True(secondCompilation.success, FormatDiagnostics(secondCompilation));
        Assert.True(m_manager.ApplyPendingReload());
        Type second = ResolveVersionedBehavior();
        Assert.NotSame(first, second);
        Assert.Equal(2, ReadVersion(second));
        Assert.True(TypeCacheManager.TryGetRuntimeTypeId(second, out int secondRuntimeId));
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
                        new EditorCommand(new EditorActionId("tests.interactions.execute")));
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
        Assert.Contains(TypeCacheManager.current.types, static type => type.Name == "ScriptAction");
        Assert.Contains(TypeCacheManager.current.types, static type => type.Name == "ScriptAssetEditor");
        Assert.Contains(TypeCacheManager.current.types, static type => type.Name == "ScriptDrop");
        Assert.Contains(TypeCacheManager.current.types, static type => type.Name == "ScriptPanel");
        Assert.Contains(TypeCacheManager.current.types, static type => type.Name == "ScriptValueHistoryHandler");

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
        Type settingType = Assert.Single(
            TypeCacheManager.current.types,
            static type => type.Name == "ProjectOverlaySetting");
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
    [InlineData("Assets/Prefabs/Player.iprefab", "Cube")]
    [InlineData("Assets/Plugins/Physics.dll", "Plug")]
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
        Type initialAssetType = TypeCacheManager.current.types.Single(
            static type => type.Name == "CustomIconAsset");
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
        Type updatedAssetType = TypeCacheManager.current.types.Single(
            static type => type.Name == "CustomIconAsset");
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
        string pluginDirectory = Path.Combine(m_projectRoot, "Assets", "Plugins");
        Directory.CreateDirectory(pluginDirectory);
        File.Copy(
            Path.Combine(AppContext.BaseDirectory, "Plugins", "Inno.Editor.Scripting.TestPlugin.dll"),
            Path.Combine(pluginDirectory, "ProjectPlugin.dll"));
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
        Type consumerType = TypeCacheManager.current.types.Single(type => type.Name == "PluginConsumer");
        object consumer = Activator.CreateInstance(consumerType)!;
        Assert.Equal(42, consumerType.GetProperty("value")!.GetValue(consumer));
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
        Type previousType = TypeCacheManager.current.types.Single(type => type.Name == "MigratingBehavior");
        var scene = new GameScene("Hot Reload");
        GameObject gameObject = scene.CreateObject("Actor");
        GameObject referencedObject = scene.CreateObject("Referenced");
        GameComponent previous = gameObject.AddComponent(previousType);
        Type previousSystemType = TypeCacheManager.current.types.Single(type => type.Name == "MigratingSystem");
        GameSystem previousSystem = scene.AddSystem(previousSystemType);
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

        Assert.NotSame(previous, current);
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
    public void RuntimeReload_SkipsAnIncompatiblePropertyAndPreservesTheNewDefault()
    {
        WriteChangingPropertyBehavior(useString: false);
        ScriptCompilationResult firstCompilation = Compile();
        Assert.True(firstCompilation.success, FormatDiagnostics(firstCompilation));
        Assert.True(m_manager.ApplyPendingReload());
        Type previousType = TypeCacheManager.current.types.Single(type => type.Name == "ChangingPropertyBehavior");
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
    public void MissingLiveReplacement_ReportsTheRetiringStableTypeId()
    {
        const string previousStableTypeId = "1b11fc01-68f7-48c5-a228-ad2dd311ee6a";
        WriteIdentityProbe(previousStableTypeId);
        ScriptCompilationResult firstCompilation = Compile();
        Assert.True(firstCompilation.success, FormatDiagnostics(firstCompilation));
        Assert.True(m_manager.ApplyPendingReload());
        Type previousType = TypeCacheManager.current.types.Single(type => type.Name == "IdentityProbe");
        var scene = new GameScene("Identity Probe");
        GameObject gameObject = scene.CreateObject("Actor");
        GameComponent previous = gameObject.AddComponent(previousType);
        SceneManager.LoadScene(scene);

        WriteIdentityProbe("69df8ec0-e28d-4769-9e8a-0a83ef18d62c");
        ScriptCompilationResult secondCompilation = Compile();
        Assert.True(secondCompilation.success, FormatDiagnostics(secondCompilation));

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => m_manager.ApplyPendingReload());

        Assert.Contains(previousStableTypeId, exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("IdentityProbe", exception.Message, StringComparison.Ordinal);
        Assert.Same(previous, gameObject.GetComponents().Single(component => component.GetType() == previousType));

        InvalidOperationException retryException = Assert.Throws<InvalidOperationException>(
            () => m_manager.ApplyPendingReload());

        Assert.Contains(previousStableTypeId, retryException.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Same(previous, gameObject.GetComponents().Single(component => component.GetType() == previousType));
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
        Assert.Contains(info.persistentId.ToString("D"), File.ReadAllText(typeManifest.absolutePath));
        Assert.Equal(
            File.ReadAllText(Path.Combine(m_projectRoot, "Assets", "Scripts", "Tracked.cs")),
            File.ReadAllText(source.absolutePath));
        Assert.NotNull(result.outputDirectory);
        Assert.True(File.Exists(Path.Combine(
            result.outputDirectory!,
            "Inno.GameScripts.types.json")));
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
        Type previous = TypeCacheManager.current.types.Single(type => type.Name == "RenameProbe");
        Assert.True(TypeCacheManager.TryGetStableTypeId(previous, out Guid previousStableTypeId));
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
        Type current = TypeCacheManager.current.types.Single(type => type.Name == "RenamedProbe");
        Assert.True(TypeCacheManager.TryGetStableTypeId(current, out Guid currentStableTypeId));
        Assert.Equal(previousStableTypeId, currentStableTypeId);
        Assert.True(TypeCacheManager.TryResolveType(previousStableTypeId, out Type? resolved));
        Assert.Equal(current, resolved);
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
        Type type = TypeCacheManager.current.types.Single(value => value.Name == "PartialProbe");
        Assert.True(TypeCacheManager.TryGetStableTypeId(type, out Guid stableTypeId));
        Assert.True(AssetManager.TryGetInfo("PartialProbe.cs", out AssetInfo? canonicalInfo));
        Assert.NotNull(canonicalInfo);
        Assert.NotEqual(Guid.Empty, stableTypeId);
        Assert.True(TypeCacheManager.TryResolveType(stableTypeId, out Type? resolved));
        Assert.Equal(type, resolved);
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
        Write("Runtime/Runtime.iasmdef", """
            {
              "name": "Project.Runtime",
              "scope": "Runtime",
              "references": [],
              "defines": ["PROJECT_RUNTIME"],
              "nullable": true,
              "allowUnsafe": false
            }
            """);
        Write("Runtime/RuntimeType.cs", "public sealed class RuntimeType { }");
        Write("Editor/Editor.iasmdef", """
            {
              "name": "Project.Editor",
              "scope": "Editor",
              "references": ["Project.Runtime"],
              "defines": [],
              "nullable": true,
              "allowUnsafe": false
            }
            """);
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
    public void RuntimeAssemblyDefinitionCannotReferenceEditorAssembly()
    {
        Write("Editor/Editor.iasmdef", """
            { "name": "Project.Editor", "scope": "Editor", "references": [] }
            """);
        Write("Editor/Tool.cs", "public sealed class Tool { }");
        Write("Runtime/Runtime.iasmdef", """
            { "name": "Project.Runtime", "scope": "Runtime", "references": ["Project.Editor"] }
            """);
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

    private ScriptCompilationResult Compile()
    {
        m_manager.RequestCompile();
        Assert.True(m_manager.TryCompilePending(out Task<ScriptCompilationResult>? compilation));
        Assert.NotNull(compilation);
        return compilation.GetAwaiter().GetResult();
    }

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
                protected override void OnDisable() => disableCount++;
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

    private void WriteIdentityProbe(string stableTypeId)
        => Write("IdentityProbe.cs", $$"""
            using InnoEngine.Reflection;
            using InnoEngine.Scene;

            [StableTypeId("{{stableTypeId}}")]
            public sealed class IdentityProbe : GameBehavior
            {
            }
            """);

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

    private void Write(string relativePath, string content)
    {
        string path = Path.Combine(m_projectRoot, "Assets", relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
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
        => TypeCacheManager.current.types.Single(type => type.Name == "VersionedBehavior");

    private static int ReadVersion(Type type)
        => (int)type.GetProperty("version")!.GetValue(Activator.CreateInstance(type))!;

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

    private EditorInteractionRuntime CreateSettingsRuntime(out EditorSettings settings)
    {
        SettingsCaptureModule.current = null;
        var runtime = new EditorInteractionRuntime(m_projectRoot);
        runtime.Start();
        settings = SettingsCaptureModule.current
            ?? throw new InvalidOperationException("The Settings module was not discovered.");
        return runtime;
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
