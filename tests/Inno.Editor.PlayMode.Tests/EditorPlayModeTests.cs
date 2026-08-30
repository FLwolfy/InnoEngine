using System;
using System.IO;

using Inno.Core.Assemblies;
using Inno.Core.Identity;
using Inno.Core.Reflection;
using Inno.Core.Serialization;
using Inno.Editor.Core;
using Inno.Editor.Interactions;
using Inno.Editor.Scene;
using Inno.Editor.Scripting;
using Inno.Engine.Scene;
using Xunit;

namespace Inno.Editor.PlayMode.Tests;

public sealed class EditorPlayModeTests : IDisposable
{
    private readonly string m_projectRoot = Path.Combine(
        Path.GetTempPath(),
        "InnoEditorPlayModeTests",
        Guid.NewGuid().ToString("N"));

    public EditorPlayModeTests()
    {
        Directory.CreateDirectory(Path.Combine(m_projectRoot, "Assets"));
        IdentityManager.Initialize();
        AssemblyManager.Initialize(new AssemblyManagerOptions
        {
            cacheDirectory = Path.Combine(m_projectRoot, "Library", "Assemblies")
        });
        TypeCacheManager.Initialize();
        SerializationManager.Initialize();
    }

    public void Dispose()
    {
        SceneManager.UnloadAllScenes();
        SerializationManager.Shutdown();
        TypeCacheManager.Shutdown();
        AssemblyManager.Shutdown();
        IdentityManager.Shutdown();
        if (Directory.Exists(m_projectRoot))
            Directory.Delete(m_projectRoot, recursive: true);
    }

    [Fact]
    public void EntryWaitsForCompilationAndExitRestoresEditingHistory()
    {
        var scripting = new FakeScriptCompilation(EditorScriptCompilationState.Compiling);
        var scenes = new FakeScenePlayMode();
        using var harness = new PlayModeHarness(m_projectRoot, scripting, scenes);
        harness.interactions.history.RecordApplied(
            "Edit Baseline",
            new EditorHistoryChange(
                "tests/edit-baseline",
                EditorHistoryPayload.FromBytes([1])));

        Assert.True(harness.playMode.EnterPlayMode());
        harness.Update();
        Assert.Equal(EditorPlayModeState.EnteringPlay, harness.playMode.state);
        Assert.Equal(0, scenes.beginCount);

        scripting.state = EditorScriptCompilationState.Ready;
        harness.Update();
        Assert.Equal(EditorPlayModeState.Playing, harness.playMode.state);
        Assert.Equal(1, scenes.beginCount);
        Assert.Null(harness.interactions.history.undoName);
        harness.interactions.history.RecordApplied(
            "Runtime Change",
            new EditorHistoryChange(
                "tests/runtime-change",
                EditorHistoryPayload.FromBytes([2])));

        Assert.True(harness.playMode.ExitPlayMode());
        harness.Update();
        Assert.Equal(EditorPlayModeState.Editing, harness.playMode.state);
        Assert.Equal(1, scenes.restoreCount);
        Assert.Equal("Edit Baseline", harness.interactions.history.undoName);
    }

    [Fact]
    public void CompilationFailureReturnsToEditWithoutReplacingScenes()
    {
        var scripting = new FakeScriptCompilation(EditorScriptCompilationState.Failed);
        var scenes = new FakeScenePlayMode();
        using var harness = new PlayModeHarness(m_projectRoot, scripting, scenes);

        Assert.True(harness.playMode.EnterPlayMode());
        harness.Update();

        Assert.Equal(EditorPlayModeState.Editing, harness.playMode.state);
        Assert.Contains("valid script generation", harness.playMode.lastFailure, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, scenes.beginCount);
    }

    [Fact]
    public void EnteringPlayCanBeCancelledBeforeScriptsBecomeReady()
    {
        var scripting = new FakeScriptCompilation(EditorScriptCompilationState.Compiling);
        var scenes = new FakeScenePlayMode();
        using var harness = new PlayModeHarness(m_projectRoot, scripting, scenes);

        Assert.True(harness.playMode.EnterPlayMode());
        Assert.True(harness.playMode.ExitPlayMode());
        harness.Update();

        Assert.Equal(EditorPlayModeState.Editing, harness.playMode.state);
        Assert.Equal(0, scenes.beginCount);
    }

    [Fact]
    public void HostLoopDispatchesGameLifecycleOnlyWhilePlaying()
    {
        var scripting = new FakeScriptCompilation(EditorScriptCompilationState.Ready);
        var scenes = new FakeScenePlayMode();
        using var harness = new PlayModeHarness(m_projectRoot, scripting, scenes);
        var scene = new GameScene("Runtime");
        CountingSystem system = scene.AddSystem<CountingSystem>();
        SceneManager.LoadScene(scene);

        harness.loop.FixedUpdate(0.02f);
        harness.loop.Update(0.016f);
        harness.loop.LateUpdate(0.016f);
        Assert.Equal(0, system.fixedCount + system.updateCount + system.lateCount);

        Assert.True(harness.playMode.EnterPlayMode());
        harness.Update();
        harness.loop.FixedUpdate(0.02f);
        harness.loop.Update(0.016f);
        harness.loop.LateUpdate(0.016f);

        Assert.Equal(1, system.fixedCount);
        Assert.Equal(1, system.updateCount);
        Assert.Equal(1, system.lateCount);
    }

    [Fact]
    public void SimulationFailureRequestsAndCompletesSafeEditRestoration()
    {
        var scripting = new FakeScriptCompilation(EditorScriptCompilationState.Ready);
        var scenes = new FakeScenePlayMode();
        using var harness = new PlayModeHarness(m_projectRoot, scripting, scenes);
        var scene = new GameScene("Runtime Failure");
        _ = scene.AddSystem<ThrowingSystem>();
        SceneManager.LoadScene(scene);
        Assert.True(harness.playMode.EnterPlayMode());
        harness.Update();

        harness.loop.Update(0.016f);
        Assert.Equal(EditorPlayModeState.ExitingPlay, harness.playMode.state);
        harness.Update();

        Assert.Equal(EditorPlayModeState.Editing, harness.playMode.state);
        Assert.Equal(1, scenes.restoreCount);
        Assert.Contains("update failed", harness.playMode.lastFailure, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SceneSessionRestoresGraphIdentitySelectionAndEditValues()
    {
        var selection = new FakeSelectionCoordinator();
        var workspace = new EditorSceneWorkspace(selection);
        var editScene = new GameScene("Edit Scene");
        GameObject editObject = editScene.CreateObject("Edit Object");
        Guid sceneId = editScene.identity.persistentId;
        Guid objectId = editObject.identity.persistentId;
        SceneManager.LoadScene(editScene);
        selection.SetSelection(editObject);

        IEditorScenePlayModeSession session = ((IEditorScenePlayMode)workspace).BeginPlayMode();
        GameScene runtimeScene = Assert.Single(SceneManager.loadedScenes);
        GameObject runtimeObject = Assert.Single(runtimeScene.GetObjects());
        Assert.NotSame(editScene, runtimeScene);
        Assert.NotSame(editObject, runtimeObject);
        Assert.Equal(sceneId, runtimeScene.identity.persistentId);
        Assert.Equal(objectId, runtimeObject.identity.persistentId);
        Assert.False(workspace.canPersist);
        runtimeScene.name = "Runtime Scene";
        runtimeObject.name = "Runtime Object";
        _ = runtimeScene.CreateObject("Runtime Only");
        Assert.Throws<InvalidOperationException>(() => workspace.Save(runtimeScene, string.Empty));

        session.Restore();

        GameScene restoredScene = Assert.Single(SceneManager.loadedScenes);
        GameObject restoredObject = Assert.Single(restoredScene.GetObjects());
        Assert.Equal(sceneId, restoredScene.identity.persistentId);
        Assert.Equal(objectId, restoredObject.identity.persistentId);
        Assert.Equal("Edit Scene", restoredScene.name);
        Assert.Equal("Edit Object", restoredObject.name);
        Assert.Same(restoredObject, selection.selectedTarget);
        Assert.True(workspace.canPersist);
        session.Dispose();
    }

    public sealed class CountingSystem : GameSystem
    {
        public int fixedCount { get; private set; }
        public int updateCount { get; private set; }
        public int lateCount { get; private set; }

        protected override void OnFixedUpdate() => fixedCount++;
        protected override void OnUpdate() => updateCount++;
        protected override void OnLateUpdate() => lateCount++;
    }

    public sealed class ThrowingSystem : GameSystem
    {
        protected override void OnUpdate()
            => throw new InvalidOperationException("Injected simulation failure.");
    }

    private sealed class PlayModeHarness : IDisposable
    {
        private readonly EditorContext m_context;
        private readonly EditorInteractionRuntime m_runtime;
        private readonly EditorPlayModeModule m_module;
        private bool m_started = true;

        internal PlayModeHarness(
            string projectRoot,
            IEditorScriptCompilation scripting,
            IEditorScenePlayMode scenes)
        {
            m_context = new EditorContext(projectRoot);
            m_runtime = new EditorInteractionRuntime(m_context);
            loop = new EditorPlayModeLoop();
            m_module = new EditorPlayModeModule(loop, scenes, scripting, m_runtime.interactions);
            m_module.Start(m_context);
        }

        internal EditorPlayModeLoop loop { get; }
        internal IEditorPlayMode playMode => m_module;
        internal EditorInteractions interactions => m_runtime.interactions;

        internal void Update() => m_module.Update(m_context);

        public void Dispose()
        {
            if (m_started)
            {
                m_module.Stop(m_context);
                m_started = false;
            }
            ((IDisposable)m_module).Dispose();
            m_runtime.Dispose();
            SceneManager.UnloadAllScenes();
        }
    }

    private sealed class FakeScriptCompilation(EditorScriptCompilationState initialState)
        : IEditorScriptCompilation
    {
        public EditorScriptCompilationState state { get; set; } = initialState;
        public string status => "Test script status.";
        public ScriptCompilationResult? lastCompilation => null;
    }

    private sealed class FakeScenePlayMode : IEditorScenePlayMode
    {
        internal int beginCount { get; private set; }
        internal int restoreCount { get; private set; }

        public IEditorScenePlayModeSession BeginPlayMode()
        {
            beginCount++;
            return new Session(this);
        }

        private sealed class Session(FakeScenePlayMode owner) : IEditorScenePlayModeSession
        {
            private bool m_restored;

            public void Restore()
            {
                if (m_restored)
                    return;
                m_restored = true;
                owner.restoreCount++;
            }

            public void Dispose() => Restore();
        }
    }

    private sealed class FakeSelectionCoordinator : IEditorSelectionCoordinator
    {
        public object? selectedTarget { get; private set; }

        public void SetSelection(object? target) => selectedTarget = target;
    }
}
