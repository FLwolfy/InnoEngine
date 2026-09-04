using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

using Inno.Assets;
using Inno.Assets.Pipeline;
using Inno.Extensibility.Modules;
using Inno.Extensibility.Types;
using Inno.Core.Identity;
using Inno.Core.Serialization;
using Inno.Core.Settings;
using Inno.Editor.Core;
using Inno.Editor.Interactions;
using Inno.Editor.PlayMode;
using Inno.Editor.Scene;
using Inno.Editor.Scripting;
using Inno.Plugins.Authoring;
using Inno.Plugins;
using Inno.Runtime;
using Inno.Scene;
using Inno.Scene.Components;
using Inno.Scripting.Compiler;
using Inno.Scripting.Reload;
using Xunit;

namespace Inno.Editor.Scripting.Tests;

public sealed class ScriptingPipelineTests : IDisposable
{
    private readonly ScriptingFixture m_fixture = new();

    public void Dispose() => m_fixture.Dispose();

    [Fact]
    public void RuntimeAndEditorSourcesProduceSeparateDeterministicArtifacts()
    {
        m_fixture.Write("ProjectBehavior.cs", """
            using InnoEngine.Scene;

            public sealed class ProjectBehavior : GameBehavior
            {
            }
            """);
        m_fixture.Write("ProjectTools.editor.cs", """
            using InnoEditor.Core;

            public sealed class ProjectTools
            {
                public EditorContext? context { get; set; }
            }
            """);

        ScriptCompilationResult result = m_fixture.Compile();

        Assert.True(result.success, FormatDiagnostics(result));
        Assert.NotNull(result.outputDirectory);
        Assert.True(File.Exists(Path.Combine(result.outputDirectory!, "Inno.GameScripts.dll")));
        Assert.True(File.Exists(Path.Combine(result.outputDirectory!, "Inno.EditorScripts.dll")));
        Assert.NotEmpty(result.runtimeAssemblyPaths);
        Assert.Contains(result.activationRequests, static request => request.scope == AssemblyScope.Runtime);
        Assert.Contains(result.activationRequests, static request => request.scope == AssemblyScope.Editor);
    }

    [Fact]
    public void ScriptApiTypeMatchingItsImplementationNamespaceRemainsCallable()
    {
        m_fixture.Write("AssetLookupProbe.cs", """
            using InnoEngine.Assets;
            using InnoEngine.Rendering;

            namespace Inno.Rendering2D;

            internal static class AssetLookupProbe
            {
                internal static bool TryResolve(AssetPath path, out MaterialAsset? asset)
                    => Assets.TryLoad(path, out asset);
            }
            """);

        ScriptCompilationResult result = m_fixture.Compile();

        Assert.True(result.success, FormatDiagnostics(result));
    }

    [Fact]
    public void EditorPluginSettingsExtensionsCompileThroughLogicalNamespaces()
    {
        m_fixture.Write("SettingsProbe.editor.cs", """
            using InnoEditor.Settings;
            using InnoEngine.Serialization;
            using InnoEngine.Settings;

            [ProjectSettingPath("Project/Tests/Probe")]
            public sealed class SettingsProbeEditor : ProjectSettingEditor<SettingsProbe>
            {
                public override ProjectSettingId settingId => new("tests.scripting.settings-probe");

                protected override void OnDraw(SettingsProbe setting)
                {
                }
            }

            public sealed class SettingsProbe : ISerializable
            {
            }
            """);

        ScriptCompilationResult result = m_fixture.Compile();

        Assert.True(result.success, FormatDiagnostics(result));
    }

    [Fact]
    public void RuntimeDeploymentCompilationDoesNotCompileOrValidateEditorSources()
    {
        m_fixture.Write("RuntimeOnly.cs", "public sealed class RuntimeOnly { }");
        m_fixture.Write("BrokenTool.editor.cs", "this is deliberately invalid editor C#");

        ScriptCompilationResult result = m_fixture.CompileRuntimeDeployment();

        Assert.True(result.success, FormatDiagnostics(result));
        Assert.NotNull(result.outputDirectory);
        Assert.True(File.Exists(Path.Combine(result.outputDirectory!, "Inno.GameScripts.dll")));
        Assert.False(File.Exists(Path.Combine(result.outputDirectory!, "Inno.EditorScripts.dll")));
        Assert.DoesNotContain(
            result.activationRequests,
            static request => request.scope == AssemblyScope.Editor);
        Assert.DoesNotContain(
            result.compiledAssemblyNames.Concat(result.reusedAssemblyNames),
            static assemblyName => assemblyName.Contains("Editor", StringComparison.Ordinal));
    }

    [Fact]
    public void RuntimeDeploymentBindsToTargetPlayerAssembliesAndInvalidatesItsCache()
    {
        m_fixture.Write("TargetBoundBehavior.cs", """
            using InnoEngine.Scene;

            public sealed class TargetBoundBehavior : GameBehavior
            {
            }
            """);
        string targetRuntime = m_fixture.CreateDeploymentRuntime();

        ScriptCompilationResult first = m_fixture.CompileRuntimeDeployment(targetRuntime);

        Assert.True(first.success, FormatDiagnostics(first));
        File.Delete(Path.Combine(targetRuntime, "Inno.Scene.dll"));

        ScriptCompilationResult second = m_fixture.CompileRuntimeDeployment(targetRuntime);

        Assert.False(second.success);
        Assert.Contains(second.diagnostics, static diagnostic =>
            diagnostic.severity == ScriptDiagnosticSeverity.Error);
    }

    [Fact]
    public void CompilationWideUsingDirectiveIsRejected()
    {
        m_fixture.Write("ForbiddenUsing.cs", """
            global using InnoEngine.Scene;

            public sealed class ForbiddenUsingBehavior : GameBehavior
            {
            }
            """);

        ScriptCompilationResult result = m_fixture.Compile();

        Assert.False(result.success);
        Assert.Contains(result.diagnostics, static diagnostic => diagnostic.id == "INNO2003");
    }

    [Fact]
    public void RuntimeApiExposesOnlyGameBehaviorAsTheEnabledLifecycleBase()
    {
        m_fixture.Write("RemovedBehaviorBase.cs", """
            using InnoEngine.Scene;

            public sealed class RemovedBehaviorBase : Behavior
            {
            }
            """);

        ScriptCompilationResult result = m_fixture.Compile();

        Assert.False(result.success);
        Assert.Contains(result.diagnostics, static diagnostic => diagnostic.id == "CS0246");
    }

    [Fact]
    public void RuntimeSourceCannotReferenceEditorApiButEditorSourceCan()
    {
        const string source = """
            using InnoEditor.Core;

            public sealed class EditorApiProbe
            {
                public EditorContext? context { get; set; }
            }
            """;
        m_fixture.Write("EditorApiProbe.cs", source);
        ScriptCompilationResult runtimeResult = m_fixture.Compile();
        Assert.False(runtimeResult.success);

        m_fixture.Move("EditorApiProbe.cs", "EditorApiProbe.editor.cs");
        ScriptCompilationResult editorResult = m_fixture.Compile();

        Assert.True(editorResult.success, FormatDiagnostics(editorResult));
    }

    [Fact]
    public void AdditionalAttachableTypeWithoutCanonicalSourceFailsCompilation()
    {
        m_fixture.Write("PrimaryProbe.cs", """
            using InnoEngine.Scene;

            public sealed class PrimaryProbe : GameBehavior
            {
            }

            public sealed class SecondaryProbe : GameBehavior
            {
            }
            """);

        ScriptCompilationResult result = m_fixture.Compile();

        Assert.False(result.success);
        Assert.Contains(result.diagnostics, static diagnostic =>
            diagnostic.id == "INNO2001" &&
            diagnostic.severity == ScriptDiagnosticSeverity.Error &&
            diagnostic.message.Contains("SecondaryProbe", StringComparison.Ordinal));
    }

    [Fact]
    public void CachedCompilationReplaysWarningsAndReusesTheGeneration()
    {
        m_fixture.Write(
            "CachedWarningProbe.cs",
            "public sealed class CachedWarningProbe { private int unused; }");

        ScriptCompilationResult first = m_fixture.Compile();
        ScriptCompilationResult second = m_fixture.Compile();

        Assert.True(first.success, FormatDiagnostics(first));
        Assert.True(second.success, FormatDiagnostics(second));
        ScriptDiagnostic firstWarning = Assert.Single(first.diagnostics, static diagnostic =>
            diagnostic.id == "CS0169" &&
            diagnostic.severity == ScriptDiagnosticSeverity.Warning);
        Assert.Contains(second.diagnostics, diagnostic => diagnostic == firstWarning);
        Assert.Equal(first.outputDirectory, second.outputDirectory);
        Assert.NotEmpty(second.reusedAssemblyNames);
    }

    [Fact]
    public void CompilerProgressIsMonotonicAndIncludesStageTimings()
    {
        m_fixture.Write("ProgressProbe.cs", "public sealed class ProgressProbe { }");
        var progress = new ProgressRecorder();

        ScriptCompilationResult result = m_fixture.Compile(progress);

        Assert.True(result.success, FormatDiagnostics(result));
        Assert.NotEmpty(progress.values);
        Assert.All(progress.values, static value => Assert.InRange(value.fraction, 0f, 1f));
        Assert.True(progress.values.Zip(progress.values.Skip(1), static (left, right) =>
            right.fraction >= left.fraction).All(static value => value));
        Assert.NotEmpty(result.stageTimings);
        Assert.All(result.stageTimings, static timing => Assert.True(timing.elapsed >= TimeSpan.Zero));
    }

    [Fact]
    public void PreCanceledCompilationDoesNotPublishAnArtifactGeneration()
    {
        m_fixture.Write("CanceledProbe.cs", "public sealed class CanceledProbe { }");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            m_fixture.Compile(cancellationToken: cancellation.Token));

        string artifactRoot = Path.Combine(
            m_fixture.projectRoot,
            "Library",
            "Artifacts",
            "ScriptAssemblies");
        Assert.True(
            !Directory.Exists(artifactRoot) ||
            !Directory.EnumerateDirectories(artifactRoot).Any());
    }

    [Fact]
    public void GenerateProjectFilesUsesTheSameRuntimeAndEditorClassification()
    {
        m_fixture.Write(
            "Runtime.cs",
            "using InnoEngine.Scene; public sealed class RuntimeScript : GameBehavior { }");
        m_fixture.Write("Tools.editor.cs", "public sealed class EditorScript { }");
        m_fixture.Rescan();

        m_fixture.compiler.GenerateProjectFiles();

        string gameProject = File.ReadAllText(
            Path.Combine(m_fixture.projectRoot, "Inno.GameScripts.csproj"));
        string editorProject = File.ReadAllText(
            Path.Combine(m_fixture.projectRoot, "Inno.EditorScripts.csproj"));
        Assert.Contains("Compile Include=\"Assets/Runtime.cs\"", gameProject);
        Assert.DoesNotContain("Tools.editor.cs", gameProject);
        Assert.Contains("Compile Include=\"Assets/Tools.editor.cs\"", editorProject);
        Assert.Contains("Inno.GameScripts.csproj", editorProject);
        Assert.DoesNotContain("<Compile Include=\"Library", gameProject);
        Assert.True(File.Exists(Path.Combine(m_fixture.projectRoot, "InnoProject.sln")));
    }

    [Fact]
    public void SampleDirectoriesRemainBrowsableButAreExcludedFromCompilationAndIdeProjects()
    {
        m_fixture.Write(
            "Runtime.cs",
            "using InnoEngine.Scene; public sealed class RuntimeScript : GameBehavior { }");
        m_fixture.Write("~Examples/Broken.cs", "this source must never compile");
        m_fixture.Rescan();

        Assert.True(m_fixture.assets.TryGetFileSystemEntry(
            AssetPath.Project("~Examples"),
            out AssetFileEntry sample));
        Assert.True(sample.isSample);
        Assert.True(m_fixture.assets.TryGetFileSystemEntry(
            AssetPath.Project("~Examples/Broken.cs"),
            out AssetFileEntry sampleSource));
        Assert.True(sampleSource.isSampleContent);
        Assert.False(m_fixture.assets.TryGetInfo(
            AssetPath.Project("~Examples/Broken.cs"),
            out _));

        ScriptCompilationResult compilation = m_fixture.Compile();
        Assert.True(compilation.success, FormatDiagnostics(compilation));

        m_fixture.compiler.GenerateProjectFiles();
        string gameProject = File.ReadAllText(
            Path.Combine(m_fixture.projectRoot, "Inno.GameScripts.csproj"));
        Assert.Contains("Compile Include=\"Assets/Runtime.cs\"", gameProject);
        Assert.DoesNotContain("~Examples", gameProject);
    }

    [Fact]
    public void ImportSampleCreatesAnImmediatelyCompilableWritableCopy()
    {
        m_fixture.Write(
            "Template/StarterBehavior.cs",
            "using InnoEngine.Scene; public sealed class StarterBehavior : GameBehavior { }");
        m_fixture.Rescan();
        Assert.True(m_fixture.assets.TryGetInfo(
            AssetPath.Project("Template/StarterBehavior.cs"),
            out AssetInfo? templateInfo));
        Guid sourcePersistentId = Assert.IsType<AssetInfo>(templateInfo).persistentId;
        string sourceRoot = Path.Combine(m_fixture.projectRoot, "Assets", "Template");
        string sampleRoot = Path.Combine(m_fixture.projectRoot, "Assets", "~Starter");
        string sourceFileMeta = Path.Combine(sourceRoot, "StarterBehavior.cs.imeta");
        byte[] sourceFileMetaBytes = File.ReadAllBytes(sourceFileMeta);
        Directory.Move(sourceRoot, sampleRoot);
        File.Move(sourceRoot + ".imeta", sampleRoot + ".imeta");
        m_fixture.Rescan();
        Assert.Equal(
            sourceFileMetaBytes,
            File.ReadAllBytes(Path.Combine(sampleRoot, "StarterBehavior.cs.imeta")));

        AssetPath imported = m_fixture.assets.ImportSample(AssetPath.Project("~Starter"));

        Assert.Equal(AssetPath.Project("Starter"), imported);
        Assert.True(File.Exists(Path.Combine(
            m_fixture.projectRoot,
            "Assets",
            "Starter",
            "StarterBehavior.cs")));
        Assert.True(m_fixture.assets.TryGetInfo(
            AssetPath.Project("Starter/StarterBehavior.cs"),
            out AssetInfo? importedInfo));
        Assert.Equal(sourcePersistentId, Assert.IsType<AssetInfo>(importedInfo).persistentId);
        Assert.True(m_fixture.assets.TryGetFileSystemEntry(imported, out AssetFileEntry importedDirectory));
        Assert.False(importedDirectory.isReadOnly);
        Assert.False(importedDirectory.isSampleContent);
        ScriptCompilationResult compilation = m_fixture.Compile();
        Assert.True(compilation.success, FormatDiagnostics(compilation));
    }

    [Fact]
    public void IdeProjectionHidesPluginProjectsAndReferencesTheirCompiledArtifacts()
    {
        using var fixture = new ScriptingFixture(WriteProjectionPlugin);
        fixture.Write("UsesProjectionPlugin.cs", """
            using ProjectionPlugin;

            public sealed class UsesProjectionPlugin
            {
                public ProjectionRuntime? runtime { get; set; }
            }
            """);
        string staleProject = Path.Combine(fixture.projectRoot, "Inno.Plugin.Stale.csproj");
        File.WriteAllText(staleProject, "stale");

        ScriptCompilationResult result = fixture.Compile();
        fixture.compiler.GenerateProjectFiles(result);

        Assert.True(result.success, FormatDiagnostics(result));
        Assert.False(File.Exists(staleProject));
        Assert.Empty(Directory.EnumerateFiles(
            fixture.projectRoot,
            "Inno.Plugin.*.csproj",
            SearchOption.TopDirectoryOnly));
        string solution = File.ReadAllText(Path.Combine(fixture.projectRoot, "InnoProject.sln"));
        Assert.DoesNotContain("Inno.Plugin.", solution, StringComparison.Ordinal);
        string gameProject = File.ReadAllText(
            Path.Combine(fixture.projectRoot, "Inno.GameScripts.csproj"));
        Assert.Contains("Reference Include=\"Inno.Plugin.TestsProjection\"", gameProject);
        Assert.Contains("Inno.Plugin.TestsProjection.dll", gameProject);
    }

    [Fact]
    public void SourceArtifactsAreCatalogedButRuntimeExportContainsNoSourceFiles()
    {
        m_fixture.Write(
            "Scripts/Tracked.cs",
            "using InnoEngine.Scene; public sealed class Tracked : GameBehavior { }");

        ScriptCompilationResult result = m_fixture.Compile();
        string contentRoot = Path.Combine(m_fixture.projectRoot, "RuntimeContent");
        AssetRuntimeContentInfo exported = m_fixture.assets.ExportRuntimeArtifacts(contentRoot);

        Assert.True(result.success, FormatDiagnostics(result));
        Assert.True(m_fixture.assets.TryGetInfo(
            AssetPath.Project("Scripts/Tracked.cs"),
            out AssetInfo? info));
        Assert.NotNull(info);
        Assert.Equal("inno.editor.csharp-script", info.importerId);
        Assert.True(m_fixture.assets.TryGetArtifact(
            info.persistentId,
            "source",
            out AssetArtifactInfo? source));
        Assert.NotNull(source);
        Assert.True(File.Exists(source.absolutePath));
        Assert.Equal(0, exported.assetCount);
        Assert.DoesNotContain(
            Directory.EnumerateFiles(contentRoot, "*", SearchOption.AllDirectories),
            static path => path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SuccessfulReloadActivatesCandidateAndFailedCompilationRetainsIt()
    {
        m_fixture.WriteVersionedBehavior(1);
        using ScriptReloadHost reload = m_fixture.CreateReloadHost();
        reload.Start();

        ScriptCompilationResult firstResult = m_fixture.CompilePending(reload);
        Assert.True(firstResult.success, FormatDiagnostics(firstResult));
        Assert.True(reload.ApplyPendingReload());
        Type first = m_fixture.ResolveActiveType("VersionedBehavior");
        Assert.Equal(1, ReadVersion(first));

        m_fixture.WriteVersionedBehavior(2);
        ScriptCompilationResult secondResult = m_fixture.CompilePending(reload);
        Assert.True(secondResult.success, FormatDiagnostics(secondResult));
        Assert.True(reload.ApplyPendingReload());
        Type second = m_fixture.ResolveActiveType("VersionedBehavior");
        Assert.NotSame(first, second);
        Assert.Equal(2, ReadVersion(second));

        m_fixture.Write(
            "VersionedBehavior.cs",
            "using InnoEngine.Scene; public sealed class VersionedBehavior : GameBehavior {");
        ScriptCompilationResult failed = m_fixture.CompilePending(reload);

        Assert.False(failed.success);
        Assert.False(reload.ApplyPendingReload());
        Assert.Same(second, m_fixture.ResolveActiveType("VersionedBehavior"));
    }

    [Fact]
    public void SceneReloadParticipantSurvivesCollectionAndMigratesLiveScriptComponents()
    {
        m_fixture.Write("ReloadableBehavior.cs", """
            using InnoEngine.Scene;
            using InnoEngine.Serialization;

            public sealed class ReloadableBehavior : GameBehavior
            {
                [SerializableProperty]
                public int retained { get; set; } = 7;
            }
            """);
        var reloads = new EditorReloadCoordinator();
        using ScriptReloadHost reload = m_fixture.CreateReloadHost(reloads);
        reload.Start();
        ScriptCompilationResult firstResult = m_fixture.CompilePending(reload);
        Assert.True(firstResult.success, FormatDiagnostics(firstResult));
        Assert.True(reload.ApplyPendingReload());

        using EditorInteractionRuntime runtime = m_fixture.CreateEditorRuntime(reloads);
        runtime.Start();
        _ = Assert.IsAssignableFrom<IEditorSceneWorkspace>(SceneReloadProbe.workspace);
        GameScene scene = m_fixture.editorSession.scenes.LoadNewSceneAdditive("Reload Test");
        GameObject gameObject = scene.CreateObject("Reload Target");
        Type previousType = m_fixture.ResolveActiveType("ReloadableBehavior");
        GameComponent previous = gameObject.AddComponent(previousType);
        previousType.GetProperty("retained")!.SetValue(previous, 41);

        ForceFullCollection();
        m_fixture.Write("ReloadableBehavior.cs", """
            using InnoEngine.Scene;
            using InnoEngine.Serialization;

            public sealed class ReloadableBehavior : GameBehavior
            {
                [SerializableProperty]
                public int retained { get; set; } = 7;

                [SerializableProperty]
                public int added { get; set; } = 2;
            }
            """);
        ScriptCompilationResult secondResult = m_fixture.CompilePending(reload);
        Assert.True(secondResult.success, FormatDiagnostics(secondResult));

        Assert.True(reload.ApplyPendingReload());

        Type currentType = m_fixture.ResolveActiveType("ReloadableBehavior");
        GameComponent current = Assert.Single(gameObject.GetComponents().Where(component =>
            string.Equals(component.GetType().Name, "ReloadableBehavior", StringComparison.Ordinal)));
        Assert.NotSame(previousType, currentType);
        Assert.Same(currentType, current.GetType());
        Assert.Equal(41, currentType.GetProperty("retained")!.GetValue(current));
        Assert.Equal(2, currentType.GetProperty("added")!.GetValue(current));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RemovedOrBrokenUpdatedPluginCommitsUnavailableGenerationAndRecovers(
        bool updateInPlace)
    {
        using var fixture = new ScriptingFixture(WriteUnavailableGenerationPlugin);
        fixture.Write("DependentBehavior.cs", """
            using InnoEngine.Reflection;
            using InnoEngine.Scene;
            using InnoEngine.Serialization;
            using UnavailabilityPlugin;

            [StableTypeId("69e8df54-51f2-40fd-9b47-c4ebdf18052e")]
            public sealed class DependentBehavior : GameBehavior
            {
                [SerializableProperty]
                public int retained { get; set; } = 11;

                public PluginBehavior? plugin { get; set; }
            }
            """);
        var reloads = new EditorReloadCoordinator();
        using ScriptReloadHost reload = fixture.CreateReloadHost(reloads);
        reload.Start();
        ScriptCompilationResult initial = fixture.CompilePending(reload);
        Assert.True(initial.success, FormatDiagnostics(initial));
        Assert.True(reload.ApplyPendingReload());

        RuntimeSession session = fixture.CreateEditorSession();
        using IDisposable executionScope = session.EnterExecutionScope();
        var sceneReload = new SceneReloadService(session.scenes, fixture.host.serialization);
        using IDisposable registration = reloads.Register(new TestSceneReloadParticipant(sceneReload));
        GameScene scene = session.scenes.LoadNewScene("Plugin Availability");
        GameObject pluginOwner = scene.CreateObject("Plugin Owner");
        GameObject scriptOwner = scene.CreateObject("Script Owner");
        Type pluginType = fixture.ResolveActiveType("PluginBehavior");
        Type scriptType = fixture.ResolveActiveType("DependentBehavior");
        GameComponent pluginComponent = pluginOwner.AddComponent(pluginType);
        GameComponent scriptComponent = scriptOwner.AddComponent(scriptType);
        pluginType.GetProperty("retained")!.SetValue(pluginComponent, 47);
        scriptType.GetProperty("retained")!.SetValue(scriptComponent, 73);
        Guid pluginComponentId = pluginComponent.identity.persistentId;
        Guid scriptComponentId = scriptComponent.identity.persistentId;

        string installedPlugin = Path.Combine(fixture.projectRoot, "Plugins", "UnavailabilityPlugin");
        string detachedPlugin = Path.Combine(fixture.projectRoot, "UnavailabilityPlugin.detached");
        string pluginSource = Path.Combine(installedPlugin, "Assets", "PluginBehavior.cs");
        string validPluginSource = File.ReadAllText(pluginSource);
        if (updateInPlace)
            File.WriteAllText(pluginSource, "this Plugin update is deliberately invalid C#");
        else
            Directory.Move(installedPlugin, detachedPlugin);
        Assert.True(fixture.RefreshPlugins());

        ScriptCompilationResult unavailable = fixture.CompilePendingPluginReload(reload);
        Assert.False(unavailable.success);
        Assert.Contains(unavailable.diagnostics, static diagnostic =>
            diagnostic.severity == ScriptDiagnosticSeverity.Error);
        Assert.True(reload.ApplyPendingReload());
        Assert.DoesNotContain(fixture.host.modules.modules, static module =>
            module.domain is AssemblyDomain.InnoPlugin or AssemblyDomain.InnoScripting);
        Assert.Equal(updateInPlace ? 1 : 0, fixture.activePlugins.Count);

        MissingGameComponent missingPlugin = Assert.IsType<MissingGameComponent>(
            pluginOwner.GetComponents().Single(component => component is not Transform));
        MissingGameComponent missingScript = Assert.IsType<MissingGameComponent>(
            scriptOwner.GetComponents().Single(component => component is not Transform));
        Assert.Equal(pluginComponentId, missingPlugin.identity.persistentId);
        Assert.Equal(scriptComponentId, missingScript.identity.persistentId);
        Assert.Equal("UnavailabilityPlugin.PluginBehavior", missingPlugin.missingTypeName);
        Assert.Equal("DependentBehavior", missingScript.missingTypeName);

        if (updateInPlace)
            File.WriteAllText(pluginSource, validPluginSource);
        else
            Directory.Move(detachedPlugin, installedPlugin);
        Assert.True(fixture.RefreshPlugins());
        ScriptCompilationResult recoveredCompilation = fixture.CompilePendingPluginReload(reload);
        Assert.True(recoveredCompilation.success, FormatDiagnostics(recoveredCompilation));
        Assert.True(reload.ApplyPendingReload());

        GameComponent recoveredPlugin = pluginOwner.GetComponents().Single(component => component is not Transform);
        GameComponent recoveredScript = scriptOwner.GetComponents().Single(component => component is not Transform);
        Assert.IsNotType<MissingGameComponent>(recoveredPlugin);
        Assert.IsNotType<MissingGameComponent>(recoveredScript);
        Assert.Equal(pluginComponentId, recoveredPlugin.identity.persistentId);
        Assert.Equal(scriptComponentId, recoveredScript.identity.persistentId);
        Assert.Equal(47, recoveredPlugin.GetType().GetProperty("retained")!.GetValue(recoveredPlugin));
        Assert.Equal(73, recoveredScript.GetType().GetProperty("retained")!.GetValue(recoveredScript));
    }

    [Fact]
    public void AuthoringRuntimeSessionDoesNotPinRetiredPluginAndScriptContexts()
    {
        using var fixture = new ScriptingFixture(WriteUnavailableGenerationPlugin);
        var reloads = new EditorReloadCoordinator();
        using ScriptReloadHost reload = fixture.CreateReloadHost(reloads);
        reload.Start();
        ScriptCompilationResult initial = fixture.CompilePending(reload);
        Assert.True(initial.success, FormatDiagnostics(initial));
        Assert.True(reload.ApplyPendingReload());

        _ = fixture.CreateEditorSession();
        ScriptCompilationResult replacement = fixture.CompilePendingPluginReload(reload);
        Assert.True(replacement.success, FormatDiagnostics(replacement));
        Assert.True(reload.ApplyPendingReload());

        Exception? failure = null;
        while (!reload.AdvanceUnloadVerification(out failure))
        {
        }

        Assert.Null(failure);
    }

    [Fact]
    public void PluginRemovalUnloadsTheCommittedMissingGenerationWithoutASecondReload()
    {
        using var fixture = new ScriptingFixture(WriteUnavailableGenerationPlugin);
        var reloads = new EditorReloadCoordinator();
        using ScriptReloadHost reload = fixture.CreateReloadHost(reloads);
        reload.Start();
        ScriptCompilationResult initial = fixture.CompilePending(reload);
        Assert.True(initial.success, FormatDiagnostics(initial));
        Assert.True(reload.ApplyPendingReload());

        RuntimeSession session = fixture.CreateEditorSession();
        using IDisposable executionScope = session.EnterExecutionScope();
        var sceneReload = new SceneReloadService(session.scenes, fixture.host.serialization);
        using IDisposable registration = reloads.Register(new TestSceneReloadParticipant(sceneReload));
        MissingGenerationExpectation expectation = CommitPluginRemoval(fixture, reload, session);

        Exception? failure = null;
        while (!reload.AdvanceUnloadVerification(out failure))
            Thread.Yield();

        Assert.Null(failure);
        Assert.False(expectation.retiredComponent.IsAlive, "The retired Plugin component remained strongly reachable.");
        Assert.False(expectation.retiredType.IsAlive, "The retired Plugin runtime Type remained strongly reachable.");
        Assert.DoesNotContain(fixture.host.modules.modules, static module =>
            module.domain == AssemblyDomain.InnoPlugin);
        MissingGameComponent missing = Assert.IsType<MissingGameComponent>(
            expectation.owner.GetComponents().Single(component => component is not Transform));
        Assert.Equal(expectation.componentId, missing.identity.persistentId);
        Assert.Equal("UnavailabilityPlugin.PluginBehavior", missing.missingTypeName);
    }

    [Fact]
    public void PluginReloadQuiescesPlaySessionBeforeRetiringItsAssemblyGeneration()
    {
        using var fixture = new ScriptingFixture(WriteUnavailableGenerationPlugin);
        var reloads = new EditorReloadCoordinator();
        using ScriptReloadHost reload = fixture.CreateReloadHost(reloads);
        reload.Start();
        ScriptCompilationResult initial = fixture.CompilePending(reload);
        Assert.True(initial.success, FormatDiagnostics(initial));
        Assert.True(reload.ApplyPendingReload());

        var scenes = new PluginPlayScene(fixture);
        var history = new CountingHistoryIsolation();
        using var playMode = new EditorPlayModeController(
            fixture.host,
            new RuntimeSessionOptions
            {
                kind = RuntimeSessionKind.Play,
                applicationId = "tests.scripting.play-reload",
                persistentDataDirectory = Path.Combine(
                    fixture.projectRoot,
                    "Persistent",
                    "tests.scripting.play-reload"),
                jobExecutionMode = RuntimeJobExecutionMode.SingleThread
            },
            scenes,
            new ReadyScriptCompilation(),
            history,
            fixture.host.logs);
        using IDisposable registration = reloads.Register(playMode);
        Assert.True(playMode.EnterPlayMode());
        playMode.AdvanceTransition();
        playMode.AdvanceTransition();
        Assert.Equal(EditorPlayModeState.Playing, playMode.state);

        ScriptCompilationResult replacement = fixture.CompilePendingPluginReload(reload);
        Assert.True(replacement.success, FormatDiagnostics(replacement));
        Assert.True(reload.ApplyPendingReload());

        Assert.Equal(EditorPlayModeState.Editing, playMode.state);
        Assert.Equal(1, scenes.restoreCount);
        Assert.Equal(1, history.disposeCount);
        Exception? failure = null;
        while (!reload.AdvanceUnloadVerification(out failure))
        {
        }
        Assert.Null(failure);
    }

    [Fact]
    public void NewCompilationTicketSupersedesTheExactPreviousRequest()
    {
        using EditorInteractionRuntime runtime = m_fixture.CreateEditorRuntime();
        runtime.Start();
        _ = runtime.panelCount;
        IEditorScriptCompilation scripting = Assert.IsAssignableFrom<IEditorScriptCompilation>(
            ScriptingCompilationProbe.compilation);

        IScriptCompilationTicket first = scripting.RequestCompilation();
        IScriptCompilationTicket second = scripting.RequestCompilation();

        Assert.Equal(ScriptCompilationTicketState.Superseded, first.state);
        Assert.True(first.isCompleted);
        Assert.True(second.requestId > first.requestId);
        Assert.Same(second, scripting.currentTicket);
        Assert.Equal(ScriptCompilationTicketState.Queued, second.state);
    }

    [Fact]
    public void FailedResultHasNoActivationOrRuntimeArtifacts()
    {
        ScriptCompilationResult result = ScriptCompilationResult.Failure(new ScriptDiagnostic(
            "INNO-TEST",
            ScriptDiagnosticSeverity.Error,
            "Injected compilation failure.",
            filePath: null,
            line: 0,
            column: 0));

        Assert.False(result.success);
        Assert.Null(result.outputDirectory);
        Assert.Empty(result.activationRequests);
        Assert.Empty(result.runtimeAssemblyPaths);
        Assert.Empty(result.compiledAssemblyNames);
        Assert.Empty(result.reusedAssemblyNames);
    }

    private static int ReadVersion(Type type)
        => (int)type.GetProperty("version")!.GetValue(Activator.CreateInstance(type))!;

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static MissingGenerationExpectation CommitPluginRemoval(
        ScriptingFixture fixture,
        ScriptReloadHost reload,
        RuntimeSession session)
    {
        GameScene scene = session.scenes.LoadNewScene("Plugin Removal");
        GameObject owner = scene.CreateObject("Plugin Owner");
        Type pluginType = fixture.ResolveActiveType("PluginBehavior");
        GameComponent component = owner.AddComponent(pluginType);
        Guid componentId = component.identity.persistentId;
        string installedPlugin = Path.Combine(fixture.projectRoot, "Plugins", "UnavailabilityPlugin");
        string detachedPlugin = Path.Combine(fixture.projectRoot, "UnavailabilityPlugin.detached");
        Directory.Move(installedPlugin, detachedPlugin);
        Assert.True(fixture.RefreshPlugins());
        ScriptCompilationResult unavailable = fixture.CompilePendingPluginReload(reload);
        Assert.True(unavailable.success, FormatDiagnostics(unavailable));
        Assert.DoesNotContain(unavailable.activationRequests, static request =>
            request.domain == AssemblyDomain.InnoPlugin);
        Assert.DoesNotContain(unavailable.activationRequests.SelectMany(static request => request.upstreamModuleNames),
            static moduleName => string.Equals(
                moduleName,
                "Plugin.tests.unavailability",
                StringComparison.Ordinal));
        Assert.True(reload.ApplyPendingReload());
        Assert.DoesNotContain(fixture.host.modules.modules, static module =>
            module.domain == AssemblyDomain.InnoPlugin ||
            module.upstreamModuleNames.Contains("Plugin.tests.unavailability", StringComparer.Ordinal));
        return new MissingGenerationExpectation(
            owner,
            componentId,
            new WeakReference(component),
            new WeakReference(pluginType));
    }

    private static void ForceFullCollection()
    {
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
    }

    private readonly record struct MissingGenerationExpectation(
        GameObject owner,
        Guid componentId,
        WeakReference retiredComponent,
        WeakReference retiredType);

    private static void WriteProjectionPlugin(
        string projectRoot,
        SerializationRegistry serialization)
    {
        string pluginRoot = Path.Combine(projectRoot, "Plugins", "ProjectionPlugin");
        string assetRoot = Path.Combine(pluginRoot, "Assets");
        Directory.CreateDirectory(assetRoot);
        File.WriteAllBytes(
            Path.Combine(pluginRoot, "Plugin.inno"),
            serialization.Serialize(new PluginManifest
            {
                pluginId = "tests.projection",
                displayName = "Projection Plugin"
            }));
        string sourcePath = Path.Combine(assetRoot, "ProjectionRuntime.cs");
        File.WriteAllText(sourcePath, """
            using InnoEngine.Scene;

            namespace ProjectionPlugin;

            public sealed class ProjectionRuntime : GameBehavior
            {
            }
            """);
        File.WriteAllBytes(
            sourcePath + ".imeta",
            serialization.Serialize(new ScriptingAssetSourceMeta
            {
                persistentId = Guid.NewGuid(),
                sourceKind = (int)AssetSourceKind.File,
                importerId = "inno.editor.csharp-script"
            }));
    }

    private static void WriteUnavailableGenerationPlugin(
        string projectRoot,
        SerializationRegistry serialization)
    {
        string pluginRoot = Path.Combine(projectRoot, "Plugins", "UnavailabilityPlugin");
        string assetRoot = Path.Combine(pluginRoot, "Assets");
        Directory.CreateDirectory(assetRoot);
        File.WriteAllBytes(
            Path.Combine(pluginRoot, "Plugin.inno"),
            serialization.Serialize(new PluginManifest
            {
                pluginId = "tests.unavailability",
                displayName = "Unavailability Test Plugin"
            }));
        string sourcePath = Path.Combine(assetRoot, "PluginBehavior.cs");
        File.WriteAllText(sourcePath, """
            using InnoEngine.Reflection;
            using InnoEngine.Scene;
            using InnoEngine.Serialization;

            namespace UnavailabilityPlugin;

            [StableTypeId("a7b5f773-184f-4bd1-bf93-e4b1088d9d68")]
            public sealed class PluginBehavior : GameBehavior
            {
                [SerializableProperty]
                public int retained { get; set; } = 5;
            }
            """);
        File.WriteAllBytes(
            sourcePath + ".imeta",
            serialization.Serialize(new ScriptingAssetSourceMeta
            {
                persistentId = Guid.Parse("37f9751f-a7de-43c7-94ac-c8e854762d49"),
                sourceKind = (int)AssetSourceKind.File,
                importerId = "inno.editor.csharp-script"
            }));
    }

    private static string FormatDiagnostics(ScriptCompilationResult result)
        => string.Join(Environment.NewLine, result.diagnostics.Select(static diagnostic =>
            $"{diagnostic.id}: {diagnostic.message}"));

    private sealed class ProgressRecorder : IProgress<ScriptCompilationProgress>
    {
        internal List<ScriptCompilationProgress> values { get; } = [];

        public void Report(ScriptCompilationProgress value) => values.Add(value);
    }

    private sealed class PluginPlayScene(ScriptingFixture fixture) : IEditorScenePlayMode
    {
        internal int restoreCount { get; private set; }

        public IDisposable BeginPlayMode(RuntimeSession runtimeSession)
        {
            var scene = new GameScene("Plugin Play Reload");
            GameObject owner = scene.CreateObject("Plugin Component Owner");
            _ = owner.AddComponent(fixture.ResolveActiveType("PluginBehavior"));
            runtimeSession.scenes.LoadScene(scene);
            return new RestoreLease(this);
        }

        private sealed class RestoreLease(PluginPlayScene owner) : IDisposable
        {
            private bool m_disposed;

            public void Dispose()
            {
                if (m_disposed)
                    return;
                m_disposed = true;
                owner.restoreCount++;
            }
        }
    }

    private sealed class CountingHistoryIsolation : IEditorHistoryIsolation
    {
        internal int disposeCount { get; private set; }

        public IDisposable BeginHistoryIsolation()
            => new HistoryLease(this);

        private sealed class HistoryLease(CountingHistoryIsolation owner) : IDisposable
        {
            private bool m_disposed;

            public void Dispose()
            {
                if (m_disposed)
                    return;
                m_disposed = true;
                owner.disposeCount++;
            }
        }
    }

    private sealed class ReadyScriptCompilation : IEditorScriptCompilation
    {
        private readonly ReadyCompilationTicket m_ticket = new();

        public IScriptCompilationTicket RequestCompilation()
            => m_ticket;

        public IScriptCompilationTicket? currentTicket => m_ticket;

        public EditorScriptCompilationState state => EditorScriptCompilationState.Ready;

        public string status => "The test script generation is ready.";

        public ScriptCompilationResult? lastCompilation => null;

        private sealed class ReadyCompilationTicket : IScriptCompilationTicket
        {
            public long requestId => 1;

            public ScriptCompilationTicketState state => ScriptCompilationTicketState.Succeeded;

            public string status => "The test compilation ticket succeeded.";

            public ScriptCompilationResult? result => null;

            public bool isCompleted => true;
        }
    }

    private sealed class TestSceneReloadParticipant(SceneReloadService reload) : IEditorReloadParticipant
    {
        public IEditorReloadTransaction Capture(AssemblyReloadContext context)
            => new TestSceneReloadTransaction(reload.Capture(
                context.GetContext<TypeCacheReloadContext>()));

        public void RefreshDiagnostics()
        {
        }
    }

    private sealed class TestSceneReloadTransaction(ISceneReloadStateTransfer transfer)
        : IEditorReloadTransaction
    {
        public void PrepareForActivation()
            => transfer.PrepareForActivation();

        public void Apply()
            => transfer.Apply();

        public void Complete()
            => transfer.Complete();

        public void RollbackStructure()
            => transfer.RollbackStructure();

        public void RestorePreviousState()
            => transfer.RestorePreviousState();
    }
}

internal sealed class ScriptingFixture : IDisposable
{
    private readonly IdentityAllocator m_identities = new();
    private readonly IDisposable m_diagnosticScope;
    private readonly IDisposable m_identityScope;
    private readonly ProjectSettingsStore m_settings;
    private readonly PluginEnvironment m_plugins;
    private RuntimeSession? m_editorSession;
    private bool m_disposed;

    internal ScriptingFixture(
        Action<string, SerializationRegistry>? configureProject = null)
    {
        projectRoot = Path.Combine(
            Path.GetTempPath(),
            "InnoScriptingPipelineTests",
            Guid.NewGuid().ToString("N"));
        string assetRoot = Path.Combine(projectRoot, "Assets");
        string pluginRoot = Path.Combine(projectRoot, "Plugins");
        string libraryRoot = Path.Combine(projectRoot, "Library");
        Directory.CreateDirectory(assetRoot);
        Directory.CreateDirectory(pluginRoot);
        Directory.CreateDirectory(libraryRoot);
        m_identityScope = m_identities.EnterScope();
        host = new EngineHostBuilder()
            .UseMetadataCache(Path.Combine(libraryRoot, "Assemblies"))
            .Build();
        m_diagnosticScope = host.diagnostics.EnterScope();
        configureProject?.Invoke(projectRoot, host.serialization);
        m_settings = new ProjectSettingsStore(
            Path.Combine(projectRoot, "Settings.Project.inno"),
            host.types,
            host.serialization,
            new ProjectId("tests.scripting"));
        var pluginSources = new PluginSourceService(host.serialization, pluginRoot, libraryRoot);
        PluginScanResult scan = pluginSources.Scan();
        AssetPipelineOptions options = AssetPipelineOptions.Create(assetRoot, libraryRoot);
        assets = new AssetPipeline(
            host.modules,
            host.types,
            host.serialization,
            m_identities,
            host.diagnostics,
            host.logs,
            options with
            {
                enableFileSystemWatcher = false,
                sourceMounts =
                [
                    .. options.sourceMounts!,
                    .. PluginSourceService.GetActivatableMounts(scan)
                ]
            });
        m_plugins = new PluginEnvironment(
            assets,
            m_settings,
            host.serialization,
            pluginRoot,
            libraryRoot,
            scan);
        compiler = new ScriptCompiler(
            new ScriptCompilerOptions { projectRootDirectory = projectRoot },
            assets,
            m_plugins);
    }

    internal string projectRoot { get; }

    internal EngineHost host { get; }

    internal AssetPipeline assets { get; }

    internal ScriptCompiler compiler { get; }

    internal IReadOnlyList<PluginCandidate> activePlugins => m_plugins.activePlugins;

    internal RuntimeSession editorSession
        => m_editorSession ?? throw new InvalidOperationException("The Editor runtime session has not been created.");

    internal void Write(string relativePath, string source)
    {
        string path = Path.Combine(projectRoot, "Assets", relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, source);
    }

    internal void Move(string sourceRelativePath, string destinationRelativePath)
    {
        string source = Path.Combine(projectRoot, "Assets", sourceRelativePath);
        string destination = Path.Combine(projectRoot, "Assets", destinationRelativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Move(source, destination);
        string sourceMetadata = source + ".imeta";
        if (File.Exists(sourceMetadata))
            File.Move(sourceMetadata, destination + ".imeta");
    }

    internal void WriteVersionedBehavior(int version)
        => Write("VersionedBehavior.cs", $$"""
            using InnoEngine.Scene;

            public sealed class VersionedBehavior : GameBehavior
            {
                public int version => {{version}};
            }
            """);

    internal void Rescan() => assets.Rescan();

    internal ScriptCompilationResult Compile(
        IProgress<ScriptCompilationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        Rescan();
        return compiler.CompileAuthoringGenerationAsync(progress, cancellationToken).GetAwaiter().GetResult();
    }

    internal ScriptCompilationResult CompileRuntimeDeployment(
        string? targetRuntimeDirectory = null,
        IProgress<ScriptCompilationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        Rescan();
        return compiler.CompileRuntimeDeploymentAsync(
                targetRuntimeDirectory ?? AppContext.BaseDirectory,
                progress,
                cancellationToken)
            .GetAwaiter()
            .GetResult();
    }

    internal string CreateDeploymentRuntime()
    {
        string directory = Path.Combine(projectRoot, "TargetRuntime");
        Directory.CreateDirectory(directory);
        foreach (string source in Directory.EnumerateFiles(
                     AppContext.BaseDirectory,
                     "Inno.*.dll",
                     SearchOption.TopDirectoryOnly))
        {
            File.Copy(source, Path.Combine(directory, Path.GetFileName(source)), overwrite: true);
        }
        return directory;
    }

    internal ScriptReloadHost CreateReloadHost(EditorReloadCoordinator? reloads = null)
        => new(
            new ScriptReloadOptions
            {
                autoCompile = false,
                debounceMilliseconds = 0,
                compilationWarningTimeout = Timeout.InfiniteTimeSpan
            },
            compiler,
            assets,
            m_plugins,
            host.modules,
            m_settings,
            reloads ?? new EditorReloadCoordinator());

    internal ScriptCompilationResult CompilePending(ScriptReloadHost reload)
    {
        Rescan();
        reload.RecompileScripting();
        Assert.True(reload.TryCompilePending(out Task<ScriptCompilationResult>? compilation));
        return Assert.IsAssignableFrom<Task<ScriptCompilationResult>>(compilation)
            .GetAwaiter()
            .GetResult();
    }

    internal ScriptCompilationResult CompilePendingPluginReload(ScriptReloadHost reload)
    {
        reload.ReloadPlugins();
        Assert.True(reload.TryCompilePending(out Task<ScriptCompilationResult>? compilation));
        return Assert.IsAssignableFrom<Task<ScriptCompilationResult>>(compilation)
            .GetAwaiter()
            .GetResult();
    }

    internal bool RefreshPlugins()
        => m_plugins.Refresh();

    internal Type ResolveActiveType(string name)
    {
        TypeCacheSnapshot snapshot = host.types.current;
        return snapshot.types
            .Select(typeRef => typeRef.Resolve(snapshot))
            .Single(type => string.Equals(type.Name, name, StringComparison.Ordinal));
    }

    internal EditorInteractionRuntime CreateEditorRuntime(EditorReloadCoordinator? reloads = null)
    {
        ScriptingCompilationProbe.Reset();
        SceneReloadProbe.Reset();
        reloads ??= new EditorReloadCoordinator();
        RuntimeSession editorSession = CreateEditorSession();
        return new EditorInteractionRuntime(
            new EditorContext(projectRoot),
            host.types,
            host.logs,
            [
                host.types,
                host.serialization,
                host.modules,
                assets,
                m_plugins,
                m_settings,
                compiler,
                editorSession,
                reloads
            ]);
    }

    internal RuntimeSession CreateEditorSession()
    {
        m_editorSession ??= host.CreateSession(new RuntimeSessionOptions
        {
            kind = RuntimeSessionKind.Edit,
            applicationId = "tests.editor",
            persistentDataDirectory = Path.Combine(projectRoot, "Persistent", "tests.editor")
        });
        return m_editorSession;
    }

    public void Dispose()
    {
        if (m_disposed)
            return;
        m_disposed = true;
        m_editorSession?.Dispose();
        m_plugins.Dispose();
        assets.Dispose();
        m_settings.Dispose();
        m_diagnosticScope.Dispose();
        host.Dispose();
        m_identityScope.Dispose();
        if (Directory.Exists(projectRoot))
            Directory.Delete(projectRoot, recursive: true);
    }
}

internal sealed class ScriptingAssetSourceMeta : ISerializable
{
    [SerializableProperty]
    public Guid persistentId { get; set; }

    [SerializableProperty]
    public int sourceKind { get; set; }

    [SerializableProperty]
    public string importerId { get; set; } = string.Empty;

    [SerializableProperty]
    public byte[] importerSettingsBytes { get; set; } = [];
}

[EditorModule("tests.scripting-compilation-probe", order: 101)]
public sealed class ScriptingCompilationProbe : EditorModule
{
    public ScriptingCompilationProbe(IEditorScriptCompilation scripting)
    {
        compilation = scripting;
    }

    public static IEditorScriptCompilation? compilation { get; private set; }

    public static void Reset() => compilation = null;
}

[EditorModule("tests.scene-reload-probe", order: 102)]
public sealed class SceneReloadProbe : EditorModule
{
    public SceneReloadProbe(IEditorSceneWorkspace sceneWorkspace)
    {
        workspace = sceneWorkspace;
    }

    public static IEditorSceneWorkspace? workspace { get; private set; }

    public static void Reset() => workspace = null;
}
