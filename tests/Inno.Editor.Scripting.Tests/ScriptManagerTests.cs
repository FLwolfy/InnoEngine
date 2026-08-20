using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

using Inno.Assets;
using Inno.Assets.Core;
using Inno.Assets.Loader;
using Inno.Assets.Types;
using Inno.Core.Assemblies;
using Inno.Core.Identity;
using Inno.Core.Reflection;
using Inno.Core.Serialization;
using Inno.Editor.Assets.AssetEditors;
using Inno.Editor.Core;
using Inno.Editor.Core.Commands;
using Inno.Editor.ImGui;
using Inno.Editor.ImGui.Widgets;
using Inno.Editor.Interactions;
using Inno.Editor.Scene.Inspection;
using Inno.Editor.Scene.Workspace;
using Inno.Editor.Scripting;
using Inno.Engine.Scene;
using Inno.Engine.Scene.Assets;

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
            autoCompile = false
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
    public void SceneWorkspaceSaveRenamesTheExistingSceneAsset()
    {
        var workspace = new EditorSceneWorkspace();
        GameScene scene = workspace.CreateScene();
        scene.name = "Original";
        string originalPath = workspace.SaveScene(scene, "Scenes");
        Assert.True(AssetManager.TryGetPersistentId(originalPath, out Guid persistentId));

        scene.name = "Renamed";
        Assert.True(workspace.IsDirty(scene));
        string renamedPath = workspace.SaveScene(scene, "Scenes");

        Assert.Equal("Scenes/Renamed.innoscene", renamedPath);
        Assert.False(AssetManager.TryGetFileSystemEntry(originalPath, out _));
        Assert.True(AssetManager.TryGetPersistentId(renamedPath, out Guid renamedId));
        Assert.Equal(persistentId, renamedId);
        Assert.False(workspace.IsDirty(scene));
    }

    [Fact]
    public void SceneWorkspaceOpenSceneLoadsAdditivelyAtTheBottom()
    {
        var workspace = new EditorSceneWorkspace();
        GameScene source = workspace.CreateScene();
        source.name = "Additive";
        string sourcePath = workspace.SaveScene(source, "Scenes");
        Assert.True(SceneManager.UnloadScene(source));
        GameScene existing = workspace.CreateScene();

        GameScene opened = workspace.OpenScene(sourcePath);

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
        Assert.False(workspace.CloseScene(first));
        Assert.True(first.isLoaded);
        Assert.False(first.isDestroyed);
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
    public void FailedCompilationDiscardsAnUnappliedSuccessfulCandidate()
    {
        WriteVersionedBehavior(1);
        ScriptCompilationResult successful = Compile();
        Assert.True(successful.success, FormatDiagnostics(successful));

        Write("VersionedBehavior.cs", "public sealed class VersionedBehavior : GameBehavior {");
        ScriptCompilationResult failed = Compile();

        Assert.False(failed.success);
        Assert.False(m_manager.ApplyPendingReload());
        Assert.DoesNotContain(TypeCacheManager.current.types, type => type.Name == "VersionedBehavior");
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
            using InnoEditor.Commands;
            using InnoEditor.Core;
            using InnoEditor.DragDrop;
            using InnoEditor.Menus;
            using InnoEditor.Panels;
            using InnoEngine.Assets;

            public sealed class TestSurface;
            public sealed class TestDynamicSurface;
            public sealed class TestDropSurface;

            [EditorAction("tests.interactions.execute", typeof(TestSurface))]
            [EditorMenu(typeof(TestSurface), "Tools/Create/Execute")]
            public sealed class ScriptAction : EditorAction
            {
                public override void Execute(EditorActionContext context)
                {
                }
            }

            [EditorMenuSource(typeof(TestDynamicSurface))]
            public sealed class ScriptMenuSource : EditorMenuSource
            {
                public override void Build(EditorMenuContext context, EditorMenuBuilder builder)
                    => builder.Add("Dynamic", "tests.interactions.execute");
            }

            [AssetEditor(typeof(TextAsset))]
            public sealed class ScriptAssetEditor : AssetEditor
            {
                public override bool CanOpen(AssetEditorContext context) => true;
            }

            [EditorDrop(typeof(TestDropSurface))]
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
                public override void Draw(EditorContext context)
                {
                }

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
    }

    [Fact]
    public void GameScriptsCannotAccessInteractionFacades()
    {
        Write("ForbiddenEditorAction.cs", """
            using InnoEditor.Commands;
            public sealed class ForbiddenEditorAction : EditorAction
            {
                public override void Execute(EditorActionContext context)
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
    public void AdditionalAttachableTypeInOneFileRequiresExplicitIdentityOrSeparateSource()
    {
        Write("PrimaryProbe.cs", """
            using InnoEngine.Scene;

            public sealed class PrimaryProbe : GameBehavior { }
            public sealed class SecondaryProbe : GameBehavior { }
            """);

        ScriptCompilationResult result = Compile();

        Assert.True(result.success, FormatDiagnostics(result));
        Assert.Contains(result.diagnostics, static diagnostic =>
            diagnostic.id == "INNO2001" &&
            diagnostic.severity == ScriptDiagnosticSeverity.Warning &&
            diagnostic.message.Contains("SecondaryProbe", StringComparison.Ordinal));
    }

    [Fact]
    public void AssemblyDefinitionsControlScopeReferencesAndGeneratedIdeProjects()
    {
        Write("Runtime/Runtime.innoasmdef", """
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
        Write("Editor/Editor.innoasmdef", """
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
        Write("Editor/Editor.innoasmdef", """
            { "name": "Project.Editor", "scope": "Editor", "references": [] }
            """);
        Write("Editor/Tool.cs", "public sealed class Tool { }");
        Write("Runtime/Runtime.innoasmdef", """
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
        => m_manager.CompileAsync().AsTask().GetAwaiter().GetResult();

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

    private static string FormatDiagnostics(ScriptCompilationResult result)
        => string.Join(
            Environment.NewLine,
            result.diagnostics.Select(diagnostic =>
                $"{diagnostic.id}: {diagnostic.filePath}({diagnostic.line},{diagnostic.column}) {diagnostic.message}"));
}
