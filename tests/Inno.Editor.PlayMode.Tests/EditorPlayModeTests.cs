using System;
using System.Collections.Generic;
using System.IO;

using Inno.Assets.Pipeline;
using Inno.Core.Identity;
using Inno.Core.Mathematics;
using Inno.Editor.Interactions;
using Inno.Editor.Scene;
using Inno.Editor.Scripting;
using Inno.Runtime;
using Inno.Scene;
using Inno.Scripting.Compiler;
using Xunit;

namespace Inno.Editor.PlayMode.Tests;

public sealed class EditorPlayModeTests : IDisposable
{
    private readonly string m_projectRoot = Path.Combine(
        Path.GetTempPath(),
        "InnoEditorPlayModeTests",
        Guid.NewGuid().ToString("N"));
    private readonly EngineHost m_engineHost;
    private readonly RuntimeSession m_editSession;
    private readonly AssetPipeline m_authoringAssets;
    private readonly IDisposable m_editScope;

    public EditorPlayModeTests()
    {
        Directory.CreateDirectory(Path.Combine(m_projectRoot, "Assets"));
        m_engineHost = new EngineHostBuilder()
            .UseMetadataCache(Path.Combine(m_projectRoot, "Library", "Assemblies"))
            .Build();
        m_editSession = m_engineHost.CreateSession(CreateSessionOptions(RuntimeSessionKind.Edit));
        m_editScope = m_editSession.EnterExecutionScope();
        m_authoringAssets = new AssetPipeline(
            m_engineHost.modules,
            m_engineHost.types,
            m_engineHost.serialization,
            new IdentityAllocator(),
            m_engineHost.diagnostics,
            m_engineHost.logs,
            AssetPipelineOptions.Create(
                Path.Combine(m_projectRoot, "Assets"),
                Path.Combine(m_projectRoot, "Library")) with
            {
                enableFileSystemWatcher = false
            });
    }

    public void Dispose()
    {
        m_authoringAssets.Dispose();
        m_editScope.Dispose();
        m_editSession.Dispose();
        m_engineHost.Dispose();
        if (Directory.Exists(m_projectRoot))
            Directory.Delete(m_projectRoot, recursive: true);
    }

    [Fact]
    public void EntryWaitsForCompilationAndExitRestoresEditingHistory()
    {
        var scripting = new FakeScriptCompilation(EditorScriptCompilationState.Compiling);
        var scenes = new FakeScenePlayMode();
        using var harness = new PlayModeHarness(m_projectRoot, m_engineHost, scripting, scenes);
        Assert.True(harness.playMode.EnterPlayMode());
        harness.Update();
        Assert.Equal(EditorPlayModeState.Compiling, harness.playMode.state);
        Assert.Equal(0, scenes.beginCount);

        scripting.state = EditorScriptCompilationState.Ready;
        harness.Update();
        Assert.Equal(EditorPlayModeState.Preparing, harness.playMode.state);
        harness.Update();
        Assert.True(
            harness.playMode.state == EditorPlayModeState.Playing,
            harness.playMode.lastFailure);
        Assert.Equal(1, scenes.beginCount);
        Assert.Equal(1, harness.history.beginCount);
        Assert.Equal(0, harness.history.disposeCount);

        Assert.True(harness.playMode.ExitPlayMode());
        harness.Update();
        Assert.Equal(EditorPlayModeState.Editing, harness.playMode.state);
        Assert.Equal(1, scenes.restoreCount);
        Assert.Equal(1, harness.history.disposeCount);
    }

    [Fact]
    public void CompilationFailureReturnsToEditWithoutReplacingScenes()
    {
        var scripting = new FakeScriptCompilation(EditorScriptCompilationState.Failed);
        var scenes = new FakeScenePlayMode();
        using var harness = new PlayModeHarness(m_projectRoot, m_engineHost, scripting, scenes);

        Assert.True(harness.playMode.EnterPlayMode());
        harness.Update();

        Assert.Equal(EditorPlayModeState.Failed, harness.playMode.state);
        Assert.Contains("valid script generation", harness.playMode.lastFailure, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, scenes.beginCount);
    }

    [Fact]
    public void EnteringPlayCanBeCancelledBeforeScriptsBecomeReady()
    {
        var scripting = new FakeScriptCompilation(EditorScriptCompilationState.Compiling);
        var scenes = new FakeScenePlayMode();
        using var harness = new PlayModeHarness(m_projectRoot, m_engineHost, scripting, scenes);

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
        CountingSystem? system = null;
        var scenes = new FakeScenePlayMode(world =>
        {
            var scene = new GameScene("Runtime");
            system = scene.AddSystem<CountingSystem>();
            world.LoadScene(scene);
        });
        using var harness = new PlayModeHarness(m_projectRoot, m_engineHost, scripting, scenes);

        harness.Simulate(0.016f);
        Assert.Null(system);

        Assert.True(harness.playMode.EnterPlayMode());
        harness.Update();
        harness.Update();
        harness.Simulate(0.02f);

        Assert.NotNull(system);
        Assert.Equal(1, system.fixedCount);
        Assert.Equal(1, system.updateCount);
        Assert.Equal(1, system.lateCount);
    }

    [Fact]
    public void SimulationFailureRequestsAndCompletesSafeEditRestoration()
    {
        var scripting = new FakeScriptCompilation(EditorScriptCompilationState.Ready);
        var scenes = new FakeScenePlayMode(world =>
        {
            var scene = new GameScene("Runtime Failure");
            _ = scene.AddSystem<ThrowingSystem>();
            world.LoadScene(scene);
        });
        using var harness = new PlayModeHarness(m_projectRoot, m_engineHost, scripting, scenes);
        Assert.True(harness.playMode.EnterPlayMode());
        harness.Update();
        harness.Update();

        harness.Simulate(0.016f);
        Assert.Equal(EditorPlayModeState.Stopping, harness.playMode.state);
        harness.Update();

        Assert.Equal(EditorPlayModeState.Editing, harness.playMode.state);
        Assert.Equal(1, scenes.restoreCount);
        Assert.Contains("update failed", harness.playMode.lastFailure, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SceneSessionRestoresGraphIdentitySelectionAndEditValues()
    {
        var selection = new FakeSelectionCoordinator();
        using EditorSceneWorkspaceHost workspaceHost = EditorSceneWorkspaceFactory.Create(
            m_editSession,
            m_authoringAssets,
            m_engineHost.types,
            m_engineHost.serialization,
            m_engineHost.logs,
            selection);
        IEditorSceneWorkspace workspace = workspaceHost.workspace;
        var editScene = new GameScene("Edit Scene");
        GameObject editObject = editScene.CreateObject("Edit Object");
        Guid sceneId = editScene.identity.persistentId;
        Guid objectId = editObject.identity.persistentId;
        SceneManager.LoadScene(editScene);
        selection.SetSelection(editObject);

        using RuntimeSession runtimeSession = m_engineHost.CreateSession(
            CreateSessionOptions(RuntimeSessionKind.Play));
        using IDisposable session = workspaceHost.playMode.BeginPlayMode(runtimeSession);
        GameScene runtimeScene;
        GameObject runtimeObject;
        using (runtimeSession.EnterExecutionScope())
        {
            runtimeScene = Assert.Single(SceneManager.loadedScenes);
            runtimeObject = Assert.Single(runtimeScene.GetObjects());
        }
        Assert.NotSame(editScene, runtimeScene);
        Assert.NotSame(editObject, runtimeObject);
        Assert.Equal(sceneId, runtimeScene.identity.persistentId);
        Assert.Equal(objectId, runtimeObject.identity.persistentId);
        Assert.Same(runtimeScene, Assert.Single(workspace.scenes));
        Assert.Same(runtimeScene, workspace.activeScene);
        Assert.Same(runtimeObject, selection.selectedTarget);
        Assert.False(workspace.canPersist);
        using (runtimeSession.EnterExecutionScope())
        {
            runtimeScene.name = "Runtime Scene";
            runtimeObject.name = "Runtime Object";
            _ = runtimeScene.CreateObject("Runtime Only");
        }
        Assert.False(workspace.IsDirty(runtimeScene));
        Assert.Throws<InvalidOperationException>(() => workspace.Save(runtimeScene, string.Empty));

        session.Dispose();

        GameScene restoredScene = Assert.Single(SceneManager.loadedScenes);
        GameObject restoredObject = Assert.Single(restoredScene.GetObjects());
        Assert.Same(editScene, restoredScene);
        Assert.Same(editObject, restoredObject);
        Assert.Equal("Edit Scene", restoredScene.name);
        Assert.Equal("Edit Object", restoredObject.name);
        Assert.Same(editObject, selection.selectedTarget);
        Assert.Same(editScene, Assert.Single(workspace.scenes));
        Assert.Same(editScene, workspace.activeScene);
        Assert.True(workspace.canPersist);
    }

    [Fact]
    public void GamePresentationSwitchesFromEditToPlayAndBackWithoutSharingObjects()
    {
        using EditorSceneWorkspaceHost workspaceHost = EditorSceneWorkspaceFactory.Create(
            m_editSession,
            m_authoringAssets,
            m_engineHost.types,
            m_engineHost.serialization,
            m_engineHost.logs);
        IEditorGameScenePresentation presentation = workspaceHost.gamePresentation;
        var editScene = new GameScene("Edit Presentation");
        GameObject editObject = editScene.CreateObject("Edit Object");
        SceneManager.LoadScene(editScene);

        EditorScenePresentationSnapshot editing = presentation.Capture();
        Assert.Same(editScene, Assert.Single(editing.scenes));
        Assert.Same(editScene, editing.activeScene);

        using RuntimeSession runtimeSession = m_engineHost.CreateSession(
            CreateSessionOptions(RuntimeSessionKind.Play));
        IDisposable playLease = workspaceHost.playMode.BeginPlayMode(runtimeSession);
        EditorScenePresentationSnapshot playing = presentation.Capture();
        GameScene runtimeScene = Assert.Single(playing.scenes);
        GameObject runtimeObject = Assert.Single(runtimeScene.GetObjects());

        Assert.NotSame(editScene, runtimeScene);
        Assert.NotSame(editObject, runtimeObject);
        Assert.Same(runtimeScene, playing.activeScene);
        runtimeObject.name = "Runtime Object";
        runtimeObject.transform.localRotation = Quaternion.FromEulerAnglesXYZDegrees(
            new Vector3(0f, 0f, 90f));
        Assert.Equal(
            "Runtime Object",
            Assert.Single(Assert.Single(presentation.Capture().scenes).GetObjects()).name);
        Assert.Equal(
            90f,
            Assert.Single(Assert.Single(presentation.Capture().scenes).GetObjects())
                .transform.localRotation.ToEulerAnglesXYZDegrees().z,
            precision: 3);
        Assert.Equal("Edit Object", editObject.name);
        Assert.Equal(Quaternion.identity, editObject.transform.localRotation);

        playLease.Dispose();

        EditorScenePresentationSnapshot restored = presentation.Capture();
        Assert.Same(editScene, Assert.Single(restored.scenes));
        Assert.Same(editScene, restored.activeScene);
        Assert.Equal("Edit Object", Assert.Single(editScene.GetObjects()).name);
    }

    [Fact]
    public void RejectedPlayWorldDoesNotReplaceTheEditPresentation()
    {
        using EditorSceneWorkspaceHost workspaceHost = EditorSceneWorkspaceFactory.Create(
            m_editSession,
            m_authoringAssets,
            m_engineHost.types,
            m_engineHost.serialization,
            m_engineHost.logs);
        IEditorGameScenePresentation presentation = workspaceHost.gamePresentation;
        var editScene = new GameScene("Edit Presentation");
        SceneManager.LoadScene(editScene);

        using RuntimeSession runtimeSession = m_engineHost.CreateSession(
            CreateSessionOptions(RuntimeSessionKind.Play));
        using (runtimeSession.EnterExecutionScope())
            runtimeSession.scenes.LoadScene(new GameScene("Unexpected Existing Runtime Scene"));

        Assert.Throws<InvalidOperationException>(
            () => workspaceHost.playMode.BeginPlayMode(runtimeSession));

        EditorScenePresentationSnapshot snapshot = presentation.Capture();
        Assert.Same(editScene, Assert.Single(snapshot.scenes));
        Assert.Same(editScene, snapshot.activeScene);
        Assert.True(workspaceHost.workspace.canPersist);
    }

    [Fact]
    public void ScenePresentationSnapshotDefensivelyCopiesAndValidatesItsActiveScene()
    {
        var firstScene = new GameScene("First");
        var secondScene = new GameScene("Second");
        var source = new List<GameScene> { firstScene };
        var snapshot = new EditorScenePresentationSnapshot(source, firstScene);

        source.Add(secondScene);

        Assert.Same(firstScene, Assert.Single(snapshot.scenes));
        Assert.Same(firstScene, snapshot.activeScene);
        Assert.Throws<ArgumentException>(
            () => new EditorScenePresentationSnapshot(source, new GameScene("Outside")));
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
        private readonly EditorPlayModeController m_controller;
        private readonly FakeHistoryIsolation m_history = new();

        internal PlayModeHarness(
            string projectRoot,
            EngineHost engineHost,
            IEditorScriptCompilation scripting,
            IEditorScenePlayMode scenes)
        {
            m_controller = new EditorPlayModeController(
                engineHost,
                new RuntimeSessionOptions
                {
                    kind = RuntimeSessionKind.Play,
                    applicationId = "inno.tests.play",
                    persistentDataDirectory = Path.Combine(
                        projectRoot,
                        "PersistentData",
                        "inno.tests.play"),
                    jobExecutionMode = RuntimeJobExecutionMode.SingleThread
                },
                scenes,
                scripting,
                m_history,
                engineHost.logs);
        }

        internal IEditorPlayMode playMode => m_controller;
        internal FakeHistoryIsolation history => m_history;

        internal void Update() => m_controller.AdvanceTransition();

        internal void Simulate(float deltaTime) => m_controller.Tick(deltaTime);

        public void Dispose()
        {
            m_controller.Dispose();
        }
    }

    private sealed class FakeScriptCompilation(EditorScriptCompilationState initialState)
        : IEditorScriptCompilation
    {
        private EditorScriptCompilationState m_state = initialState;
        private FakeCompilationTicket? m_ticket;

        public IScriptCompilationTicket RequestCompilation()
        {
            m_ticket = new FakeCompilationTicket();
            m_ticket.SetState(m_state);
            return m_ticket;
        }

        public IScriptCompilationTicket? currentTicket => m_ticket;

        public EditorScriptCompilationState state
        {
            get => m_state;
            set
            {
                m_state = value;
                m_ticket?.SetState(value);
            }
        }

        public string status => "Test script status.";
        public ScriptCompilationResult? lastCompilation => null;
    }

    private sealed class FakeCompilationTicket : IScriptCompilationTicket
    {
        public long requestId => 1;

        public ScriptCompilationTicketState state { get; private set; }

        public string status => "Test compilation ticket.";

        public ScriptCompilationResult? result => null;

        public bool isCompleted
            => state is ScriptCompilationTicketState.Succeeded
                or ScriptCompilationTicketState.Failed
                or ScriptCompilationTicketState.Canceled
                or ScriptCompilationTicketState.Superseded;

        internal void SetState(EditorScriptCompilationState value)
        {
            state = value switch
            {
                EditorScriptCompilationState.Ready => ScriptCompilationTicketState.Succeeded,
                EditorScriptCompilationState.Failed => ScriptCompilationTicketState.Failed,
                _ => ScriptCompilationTicketState.Compiling
            };
        }
    }

    private sealed class FakeScenePlayMode(Action<SceneWorld>? populate = null) : IEditorScenePlayMode
    {
        internal int beginCount { get; private set; }
        internal int restoreCount { get; private set; }

        public IDisposable BeginPlayMode(RuntimeSession runtimeSession)
        {
            beginCount++;
            populate?.Invoke(runtimeSession.scenes);
            return new Session(this);
        }

        private sealed class Session(FakeScenePlayMode owner) : IDisposable
        {
            private bool m_restored;

            public void Dispose()
            {
                if (m_restored)
                    return;
                m_restored = true;
                owner.restoreCount++;
            }
        }
    }

    private sealed class FakeSelectionCoordinator : IEditorSelectionCoordinator
    {
        public object? selectedTarget { get; private set; }

        public void SetSelection(object? target) => selectedTarget = target;
    }

    private sealed class FakeHistoryIsolation : IEditorHistoryIsolation
    {
        internal int beginCount { get; private set; }

        internal int disposeCount { get; private set; }

        public IDisposable BeginHistoryIsolation()
        {
            beginCount++;
            return new Lease(this);
        }

        private sealed class Lease(FakeHistoryIsolation owner) : IDisposable
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

    private RuntimeSessionOptions CreateSessionOptions(RuntimeSessionKind kind)
    {
        string applicationId = kind == RuntimeSessionKind.Edit
            ? "inno.tests.edit"
            : "inno.tests.play";
        return new RuntimeSessionOptions
        {
            kind = kind,
            applicationId = applicationId,
            persistentDataDirectory = Path.Combine(
                m_projectRoot,
                "PersistentData",
                applicationId),
            jobExecutionMode = RuntimeJobExecutionMode.SingleThread
        };
    }
}
