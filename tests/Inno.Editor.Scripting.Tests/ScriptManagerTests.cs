using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

using Inno.Assets.Loader;
using Inno.Assets.Types;
using Inno.Core.Assemblies;
using Inno.Core.Identity;
using Inno.Core.Reflection;
using Inno.Core.Serialization;
using Inno.Editor.Core;
using Inno.Editor.ImGui;
using Inno.Editor.Inspection;
using Inno.Editor.Scripting;
using Inno.Engine.Scene;

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
        _ = Assembly.Load(typeof(AssetImporter).Assembly.GetName());
        _ = Assembly.Load(typeof(TextAsset).Assembly.GetName());
        _ = Assembly.Load(typeof(EditorContext).Assembly.GetName());
        _ = Assembly.Load(typeof(ImGuiWidget).Assembly.GetName());
        _ = Assembly.Load(typeof(IPropertyDrawer).Assembly.GetName());
        IdentityManager.Initialize();
        AssemblyManager.Initialize(new AssemblyManagerOptions
        {
            cacheDirectory = Path.Combine(m_projectRoot, "Library", "Assemblies")
        });
        SerializationManager.Initialize();
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
        SerializationManager.Shutdown();
        AssemblyManager.Shutdown();
        IdentityManager.Shutdown();
        if (Directory.Exists(m_projectRoot))
            Directory.Delete(m_projectRoot, recursive: true);
    }

    [Fact]
    public async Task RuntimeAndEditorScriptsCompileWithProfileGlobalUsings()
    {
        Write("ProjectBehavior.CS", """
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

                protected override AssetImportResult<TextAsset> Import(AssetImportContext context)
                    => new(new TextAsset(context.ReadUtf8Text()), default);
            }
            """);
        Write("ProjectTools.EDITOR.CS", """
            [PropertyDrawer(typeof(ProjectBehavior))]
            public sealed class ProjectBehaviorDrawer : IPropertyDrawer
            {
                public void Draw(PropertyDrawContext context)
                {
                }
            }
            """);

        ScriptCompilationResult result = await m_manager.CompileAsync();

        Assert.True(result.success, FormatDiagnostics(result));
        Assert.NotNull(result.outputDirectory);
        Assert.True(File.Exists(Path.Combine(result.outputDirectory!, "Inno.GameScripts.dll")));
        Assert.True(File.Exists(Path.Combine(result.outputDirectory!, "Inno.GameScripts.pdb")));
        Assert.True(File.Exists(Path.Combine(result.outputDirectory!, "Inno.EditorScripts.dll")));
        Assert.True(m_manager.ApplyPendingReload());
        Type behavior = TypeCache.current.types.Single(type => type.Name == "ProjectBehavior");
        Type drawer = TypeCache.current.types.Single(type => type.Name == "ProjectBehaviorDrawer");
        Assert.Equal(AssemblyGroup.Game, behavior.Assembly.GetInnoAssemblyGroup());
        Assert.Equal(AssemblyGroup.Editor, drawer.Assembly.GetInnoAssemblyGroup());
    }

    [Fact]
    public async Task SuccessfulRecompileReplacesTheActiveTypeAndFailureKeepsIt()
    {
        WriteVersionedBehavior(1);
        ScriptCompilationResult firstCompilation = await m_manager.CompileAsync();
        Assert.True(firstCompilation.success, FormatDiagnostics(firstCompilation));
        Assert.True(m_manager.ApplyPendingReload());
        Type first = ResolveVersionedBehavior();
        Assert.Equal(1, ReadVersion(first));
        Assert.True(TypeCache.TryGetRuntimeTypeId(first, out int firstRuntimeId));

        WriteVersionedBehavior(2);
        ScriptCompilationResult secondCompilation = await m_manager.CompileAsync();
        Assert.True(secondCompilation.success, FormatDiagnostics(secondCompilation));
        Assert.True(m_manager.ApplyPendingReload());
        Type second = ResolveVersionedBehavior();
        Assert.NotSame(first, second);
        Assert.Equal(2, ReadVersion(second));
        Assert.True(TypeCache.TryGetRuntimeTypeId(second, out int secondRuntimeId));
        Assert.NotEqual(firstRuntimeId, secondRuntimeId);

        Write("VersionedBehavior.cs", "public sealed class VersionedBehavior : GameBehavior {");
        ScriptCompilationResult failed = await m_manager.CompileAsync();
        Assert.False(failed.success);
        Assert.Contains(failed.diagnostics, diagnostic =>
            diagnostic.severity == ScriptDiagnosticSeverity.Error &&
            diagnostic.filePath?.EndsWith("VersionedBehavior.cs", StringComparison.Ordinal) == true &&
            diagnostic.line > 0);
        Assert.False(m_manager.ApplyPendingReload());
        Assert.Same(second, ResolveVersionedBehavior());
    }

    [Fact]
    public async Task FailedCompilationDiscardsAnUnappliedSuccessfulCandidate()
    {
        WriteVersionedBehavior(1);
        ScriptCompilationResult successful = await m_manager.CompileAsync();
        Assert.True(successful.success, FormatDiagnostics(successful));

        Write("VersionedBehavior.cs", "public sealed class VersionedBehavior : GameBehavior {");
        ScriptCompilationResult failed = await m_manager.CompileAsync();

        Assert.False(failed.success);
        Assert.False(m_manager.ApplyPendingReload());
        Assert.DoesNotContain(TypeCache.current.types, type => type.Name == "VersionedBehavior");
    }

    [Fact]
    public async Task GameScriptsCannotUseEditorProfileButEditorScriptsCan()
    {
        const string source = "public sealed class EditorApiProbe { public EditorContext? context { get; set; } }";
        Write("EditorApiProbe.cs", source);

        ScriptCompilationResult runtimeResult = await m_manager.CompileAsync();

        Assert.False(runtimeResult.success);
        Assert.Contains(runtimeResult.diagnostics, diagnostic => diagnostic.id == "CS0246");
        File.Delete(Path.Combine(m_projectRoot, "Assets", "EditorApiProbe.cs"));
        Write("EditorApiProbe.editor.cs", source);

        ScriptCompilationResult editorResult = await m_manager.CompileAsync();

        Assert.True(editorResult.success, FormatDiagnostics(editorResult));
    }

    [Fact]
    public async Task LocalPluginMetadataProvidesScriptGlobalUsings()
    {
        string pluginDirectory = Path.Combine(m_projectRoot, "Assets", "Plugins");
        Directory.CreateDirectory(pluginDirectory);
        File.Copy(
            Path.Combine(AppContext.BaseDirectory, "Plugins", "Inno.Editor.Scripting.TestPlugin.dll"),
            Path.Combine(pluginDirectory, "ProjectPlugin.dll"));
        Write("PluginConsumer.cs", """
            public sealed class PluginConsumer
            {
                public int value => PluginValue.value;
            }
            """);

        ScriptCompilationResult result = await m_manager.CompileAsync();

        Assert.True(result.success, FormatDiagnostics(result));
        Assert.True(m_manager.ApplyPendingReload());
        Type consumerType = TypeCache.current.types.Single(type => type.Name == "PluginConsumer");
        object consumer = Activator.CreateInstance(consumerType)!;
        Assert.Equal(42, consumerType.GetProperty("value")!.GetValue(consumer));
    }

    [Fact]
    public void GenerateProjectFilesMirrorsRuntimeClassificationAndReferences()
    {
        Write("Runtime.cs", "public sealed class RuntimeScript : GameBehavior { }");
        Write("Tools.editor.cs", "public sealed class EditorScript { }");

        m_manager.GenerateProjectFiles();

        string gameProject = File.ReadAllText(Path.Combine(m_projectRoot, "Inno.GameScripts.csproj"));
        string editorProject = File.ReadAllText(Path.Combine(m_projectRoot, "Inno.EditorScripts.csproj"));
        Assert.Contains("Assets/**/*.cs", gameProject);
        Assert.Contains("Exclude=\"Assets/**/*.editor.cs\"", gameProject);
        Assert.Contains("Assets/**/*.editor.cs", editorProject);
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
    public async Task RuntimeReloadReplacesLiveBehaviorAndPreservesLifecycleState()
    {
        WriteMigratingBehavior(1);
        ScriptCompilationResult firstCompilation = await m_manager.CompileAsync();
        Assert.True(firstCompilation.success, FormatDiagnostics(firstCompilation));
        Assert.True(m_manager.ApplyPendingReload());
        Type previousType = TypeCache.current.types.Single(type => type.Name == "MigratingBehavior");
        var scene = new GameScene("Hot Reload");
        GameObject gameObject = scene.CreateObject("Actor");
        GameComponent previous = gameObject.AddComponent(previousType);
        Type previousSystemType = TypeCache.current.types.Single(type => type.Name == "MigratingSystem");
        GameSystem previousSystem = scene.AddSystem(previousSystemType);
        SetProperty(previous, "value", 37);
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
        ScriptCompilationResult secondCompilation = await m_manager.CompileAsync();
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

    private void WriteVersionedBehavior(int version)
        => Write("VersionedBehavior.cs", $$"""
            [StableTypeId("4bd6efba-f60e-4d7a-a508-f79a2278317a")]
            public sealed class VersionedBehavior : GameBehavior
            {
                public int version => {{version}};
            }
            """);

    private void WriteMigratingBehavior(int generation)
        => Write("MigratingBehavior.cs", $$"""
            [StableTypeId("c14f7138-0c5c-4e69-8376-cec8edc3056c")]
            public sealed class MigratingBehavior : GameBehavior
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
                protected override void Update(float deltaTime) => updateCount++;
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
                protected override void OnUpdate(float deltaTime) => updateCount++;
            }
            """);

    private void Write(string relativePath, string content)
    {
        string path = Path.Combine(m_projectRoot, "Assets", relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private static Type ResolveVersionedBehavior()
        => TypeCache.current.types.Single(type => type.Name == "VersionedBehavior");

    private static int ReadVersion(Type type)
        => (int)type.GetProperty("version")!.GetValue(Activator.CreateInstance(type))!;

    private static int GetProperty(object target, string propertyName)
        => (int)target.GetType().GetProperty(propertyName)!.GetValue(target)!;

    private static void SetProperty(object target, string propertyName, int value)
        => target.GetType().GetProperty(propertyName)!.SetValue(target, value);

    private static string FormatDiagnostics(ScriptCompilationResult result)
        => string.Join(
            Environment.NewLine,
            result.diagnostics.Select(diagnostic =>
                $"{diagnostic.id}: {diagnostic.filePath}({diagnostic.line},{diagnostic.column}) {diagnostic.message}"));
}
