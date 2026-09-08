using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;

using Inno.Assets;
using Inno.Assets.Pipeline;
using Inno.Extensibility.Modules;
using Inno.Extensibility.Reload;
using Inno.Core.Identity;
using Inno.Extensibility.Types;
using Inno.Core.Serialization;
using Inno.Editor.Core;
using Inno.Editor.Scene;
using Inno.Runtime;
using Inno.Scene;
using Inno.Scene.Components;
using Inno.Scene.Layers;
using Xunit;

namespace Inno.Editor.Interactions.Tests;

public sealed class SceneHistoryTests : IDisposable
{
    private readonly string m_projectRoot = Path.Combine(
        Path.GetTempPath(),
        "InnoSceneHistoryTests",
        Guid.NewGuid().ToString("N"));
    private readonly EngineHost m_host;
    private readonly RuntimeSession m_session;
    private readonly IDisposable m_executionScope;
    private readonly AssetPipeline m_assets;
    private readonly EditorInteractionRuntime m_runtime;
    private readonly SceneEdits m_edits;
    private readonly EditorReloadCoordinator m_reloads = new();

    public SceneHistoryTests()
    {
        Directory.CreateDirectory(Path.Combine(m_projectRoot, "Assets"));
        SceneHistoryProbe.Reset();
        m_host = new EngineHostBuilder()
            .UseMetadataCache(Path.Combine(m_projectRoot, "Library", "Assemblies"))
            .Build();
        m_session = m_host.CreateSession(new RuntimeSessionOptions
        {
            kind = RuntimeSessionKind.Edit,
            applicationId = "inno.tests.scene-history",
            persistentDataDirectory = Path.Combine(
                m_projectRoot,
                "Library",
                "PersistentData",
                "inno.tests.scene-history"),
            jobExecutionMode = RuntimeJobExecutionMode.SingleThread
        });
        m_executionScope = m_session.EnterExecutionScope();
        m_assets = new AssetPipeline(
            m_host.modules,
            m_host.types,
            m_host.serialization,
            new IdentityAllocator(),
            m_host.diagnostics,
            m_host.logs,
            AssetPipelineOptions.Create(
                Path.Combine(m_projectRoot, "Assets"),
                Path.Combine(m_projectRoot, "Library")) with
            {
                enableFileSystemWatcher = false
            });
        m_runtime = new EditorInteractionRuntime(
            new EditorContext(m_projectRoot),
            m_host.types,
            m_host.logs,
            [
                m_host.types,
                m_host.serialization,
                m_session,
                m_assets,
                m_reloads
            ]);
        m_runtime.Start();
        _ = m_runtime.panelCount;
        m_edits = Assert.IsType<SceneEdits>(SceneHistoryProbe.edits);
    }

    public void Dispose()
    {
        m_runtime.Dispose();
        m_assets.Dispose();
        m_executionScope.Dispose();
        m_session.Dispose();
        m_host.Dispose();
        if (Directory.Exists(m_projectRoot))
            Directory.Delete(m_projectRoot, recursive: true);
    }

    [Fact]
    public void PropertyUndoRestoresOnlyThePropertyOnTheExistingSceneGraph()
    {
        GameScene scene = CreateScene();
        GameObject gameObject = scene.CreateObject("Object");
        var component = gameObject.AddComponent<HistoryComponent>();
        component.value = 10;
        Guid sceneId = scene.identity.persistentId;
        Guid objectId = gameObject.identity.persistentId;
        Guid componentId = component.identity.persistentId;

        Assert.True(m_edits.ChangeProperty(
            component,
            nameof(HistoryComponent.value),
            () => component.value = 25,
            "Change Value",
            "tests:component-value"));
        Assert.Equal(25, component.value);

        EditorHistoryResult undo = m_runtime.interactions.history.Undo();
        Assert.True(undo.succeeded, undo.message);
        Assert.Equal(10, component.value);
        Assert.Same(scene, IdentityAllocator.current.Get<GameScene>(sceneId));
        Assert.Same(gameObject, IdentityAllocator.current.Get<GameObject>(objectId));
        Assert.Same(component, IdentityAllocator.current.Get<GameComponent>(componentId));

        Assert.True(m_runtime.interactions.history.Redo().succeeded);
        Assert.Equal(25, component.value);
    }

    [Fact]
    public void TransformManipulationTransactionUndoesAllLocalValuesAtomically()
    {
        GameObject gameObject = CreateScene().CreateObject("Manipulated");
        Transform transform = gameObject.transform;
        var position = new Inno.Core.Mathematics.Vector3(3f, -2f, 4f);
        Inno.Core.Mathematics.Quaternion rotation =
            Inno.Core.Mathematics.Quaternion.CreateFromYawPitchRoll(0.2f, 0.3f, 0.4f);
        var scale = new Inno.Core.Mathematics.Vector3(2f, 3f, 4f);

        using (EditorHistoryTransaction transaction =
               m_runtime.interactions.history.BeginTransaction("Move GameObject"))
        {
            Assert.True(m_edits.ChangeProperty(
                transform,
                nameof(Transform.localPosition),
                () => transform.localPosition = position,
                "Move GameObject"));
            Assert.True(m_edits.ChangeProperty(
                transform,
                nameof(Transform.localRotation),
                () => transform.localRotation = rotation,
                "Move GameObject"));
            Assert.True(m_edits.ChangeProperty(
                transform,
                nameof(Transform.localScale),
                () => transform.localScale = scale,
                "Move GameObject"));
            transaction.Commit();
        }

        Assert.Equal("Move GameObject", m_runtime.interactions.history.undoName);
        Assert.True(m_runtime.interactions.history.Undo().succeeded);
        Assert.Equal(Inno.Core.Mathematics.Vector3.ZERO, transform.localPosition);
        Assert.Equal(Inno.Core.Mathematics.Quaternion.identity, transform.localRotation);
        Assert.Equal(Inno.Core.Mathematics.Vector3.ONE, transform.localScale);
        Assert.True(m_runtime.interactions.history.Redo().succeeded);
        Assert.Equal(position, transform.localPosition);
        Assert.Equal(rotation, transform.localRotation);
        Assert.Equal(scale, transform.localScale);
    }

    [Fact]
    public void ComponentAddRemoveResetAndOrderPreserveLogicalIdentity()
    {
        GameScene scene = CreateScene();
        GameObject gameObject = scene.CreateObject("Object");
        var first = (HistoryComponent)m_edits.AddComponent(gameObject, typeof(HistoryComponent));
        Guid firstId = first.identity.persistentId;

        Assert.True(m_runtime.interactions.history.Undo().succeeded);
        Assert.Null(IdentityAllocator.current.Get<GameComponent>(firstId));
        Assert.True(m_runtime.interactions.history.Redo().succeeded);
        var restoredFirst = Assert.IsType<HistoryComponent>(IdentityAllocator.current.Get<GameComponent>(firstId));
        Assert.NotSame(first, restoredFirst);
        Assert.Equal(7, restoredFirst.value);

        var second = (HistoryComponent)m_edits.AddComponent(gameObject, typeof(HistoryComponent));
        m_edits.SetComponentIndex(second, 1);
        Assert.Equal(1, gameObject.GetComponentIndex(second));
        Assert.True(m_runtime.interactions.history.Undo().succeeded);
        Assert.Equal(2, gameObject.GetComponentIndex(second));

        restoredFirst.value = 99;
        m_edits.ResetComponent(restoredFirst);
        Assert.Equal(7, restoredFirst.value);
        Assert.True(m_runtime.interactions.history.Undo().succeeded);
        Assert.Equal(99, restoredFirst.value);
        Assert.Same(restoredFirst, IdentityAllocator.current.Get<GameComponent>(firstId));

        Assert.True(m_edits.RemoveComponent(restoredFirst));
        Assert.Null(IdentityAllocator.current.Get<GameComponent>(firstId));
        Assert.True(m_runtime.interactions.history.Undo().succeeded);
        var restoredAgain = Assert.IsType<HistoryComponent>(IdentityAllocator.current.Get<GameComponent>(firstId));
        Assert.Equal(99, restoredAgain.value);
    }

    [Fact]
    public void RemovingComponentRestoresSerializedAssetReferences()
    {
        string sourcePath = Path.Combine(m_projectRoot, "Assets", "Text", "shared.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(sourcePath)!);
        File.WriteAllText(sourcePath, "shared");
        AssetPath assetPath = AssetPath.Project("Text/shared.txt");
        Assert.True(m_assets.Import(assetPath));
        TextAsset asset = m_assets.Load<TextAsset>(assetPath);

        GameObject owner = CreateScene().CreateObject("Audio Source Owner");
        var component = owner.AddComponent<HistoryAssetReferenceComponent>();
        component.asset = asset;
        Guid componentId = component.identity.persistentId;

        Assert.True(m_edits.RemoveComponent(component));
        EditorHistoryResult undo = m_runtime.interactions.history.Undo();

        Assert.True(undo.succeeded, undo.message);
        var restored = Assert.IsType<HistoryAssetReferenceComponent>(
            IdentityAllocator.current.Get<GameComponent>(componentId));
        Assert.Same(asset, restored.asset);
        Assert.True(m_runtime.interactions.history.Redo().succeeded);
        Assert.Null(IdentityAllocator.current.Get<GameComponent>(componentId));
    }

    [Fact]
    public void UndoMissingPluginComponentRecoversItsAssetAndKeepsTheRedoBranch()
    {
        AssetPath path = AssetPath.Project("shared.txt");
        string sourcePath = Path.Combine(m_projectRoot, "Assets", "shared.txt");
        File.WriteAllText(sourcePath, "shared recovery content");
        m_assets.Import(path);
        TextAsset asset = m_assets.Load<TextAsset>(path);
        Guid assetId = asset.identity.persistentId;
        byte[] sourceIdentity = File.ReadAllBytes(sourcePath + ".imeta");
        ReloadHistoryModule(install: true);
        GameObject owner = CreateScene().CreateObject("Recovery Owner");
        Guid componentId = AddAndRemoveReloadableComponent(owner, asset, rejectRestore: false);
        File.Delete(sourcePath);
        m_assets.Rescan();
        ReloadHistoryModule(install: false);
        m_host.modules.generations.Wait();

        EditorHistoryResult undo = m_runtime.interactions.history.Undo();
        Assert.True(undo.succeeded, undo.message);
        MissingGameComponent missing = Assert.IsType<MissingGameComponent>(m_session.scenes.Find<GameComponent>(componentId));
        int missingRuntimeId = Assert.IsType<int>(missing.identity.runtimeId);
        Assert.Equal(componentId, missing.identity.persistentId);

        File.WriteAllText(sourcePath, "shared recovery content");
        File.WriteAllBytes(sourcePath + ".imeta", sourceIdentity);
        m_assets.Rescan();
        ReloadHistoryModule(install: true);
        AssertRecoveredComponent(componentId, assetId, missingRuntimeId);
        EditorHistoryResult redo = m_runtime.interactions.history.Redo();
        Assert.True(redo.succeeded, redo.message);
        Assert.Null(m_session.scenes.Find<GameComponent>(componentId));
        Assert.True(m_runtime.interactions.history.Undo().succeeded);
        AssertRecoveredComponent(componentId, assetId, missingRuntimeId);
        ReloadHistoryModule(install: false);
        m_host.modules.generations.Wait();
    }

    [Fact]
    public void FailedMissingRecoveryPreservesPlaceholderAndHistoryForRetry()
    {
        ReloadHistoryModule(install: true);
        GameObject owner = CreateScene().CreateObject("Rejected Recovery");
        Guid componentId = AddAndRemoveReloadableComponent(owner, null, rejectRestore: true);
        ReloadHistoryModule(install: false);
        m_host.modules.generations.Wait();
        Assert.True(m_runtime.interactions.history.Undo().succeeded);
        MissingGameComponent missing = Assert.IsType<MissingGameComponent>(m_session.scenes.Find<GameComponent>(componentId));

        AssertRejectedHistoryModule();
        m_host.modules.generations.Wait();
        Assert.Same(missing, m_session.scenes.Find<GameComponent>(componentId));
        Assert.False(missing.missingType.IsValid(m_host.types));
        Assert.True(m_runtime.interactions.history.Redo().succeeded);
        Assert.Null(m_session.scenes.Find<GameComponent>(componentId));
        Assert.True(m_runtime.interactions.history.Undo().succeeded);
        Assert.IsType<MissingGameComponent>(m_session.scenes.Find<GameComponent>(componentId));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private Guid AddAndRemoveReloadableComponent(GameObject owner, TextAsset? asset, bool rejectRestore)
    {
        Type type = new TypeRef(Guid.Parse("9f67d41e-082b-46d5-aaf0-dfc76c693182")).Resolve(m_host.types);
        GameComponent component = owner.AddComponent(type);
        type.GetProperty("value")!.SetValue(component, 73);
        type.GetProperty("asset")!.SetValue(component, asset);
        type.GetProperty("rejectRestore")!.SetValue(component, rejectRestore);
        Guid id = component.identity.persistentId;
        Assert.True(m_edits.RemoveComponent(component));
        return id;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void AssertRejectedHistoryModule()
        => Assert.ThrowsAny<Exception>(() => ReloadHistoryModule(install: true));

    [Fact]
    public void MissingComponentEditsPreserveOriginalStateAndRecoveryDoesNotDirtySavedScene()
    {
        ReloadHistoryModule(install: true);
        GameScene scene = CreateScene();
        GameObject owner = scene.CreateObject("Editable Missing");
        Guid id = AddAndRemoveReloadableComponent(owner, null, rejectRestore: false);
        owner.AddComponent<HistoryComponent>();
        ReloadHistoryModule(install: false);
        m_host.modules.generations.Wait();
        Assert.True(m_runtime.interactions.history.Undo().succeeded);
        MissingGameComponent missing = Assert.IsType<MissingGameComponent>(m_session.scenes.Find<GameComponent>(id));
        int initialIndex = owner.GetComponentIndex(missing);
        m_edits.SetComponentIndex(missing, owner.GetComponents().Count - 1);
        Assert.True(m_runtime.interactions.history.Undo().succeeded);
        Assert.Equal(initialIndex, owner.GetComponentIndex(missing));
        Assert.True(m_runtime.interactions.history.Redo().succeeded);
        Assert.True(m_edits.RemoveComponent(missing));
        Assert.True(m_runtime.interactions.history.Undo().succeeded);
        Assert.IsType<MissingGameComponent>(m_session.scenes.Find<GameComponent>(id));
        IEditorSceneWorkspace documents = Assert.IsAssignableFrom<IEditorSceneWorkspace>(SceneHistoryProbe.documents);
        documents.Save(scene, "Scenes");
        Assert.False(documents.IsDirty(scene));
        ReloadHistoryModule(install: true);
        AssertRecoveredValue(id);
        Assert.False(documents.IsDirty(scene));
        ReloadHistoryModule(install: false);
        m_host.modules.generations.Wait();
        Assert.False(documents.IsDirty(scene));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void AssertRecoveredValue(Guid id)
    {
        GameComponent component = Assert.IsAssignableFrom<GameComponent>(m_session.scenes.Find<GameComponent>(id));
        Assert.IsNotType<MissingGameComponent>(component);
        Assert.Equal(73, component.GetType().GetProperty("value")!.GetValue(component));
    }

    [Fact]
    public void MissingSystemUndoAndRemovalKeepItsLogicalTypeForRecovery()
    {
        ReloadHistoryModule(install: true);
        GameScene scene = CreateScene();
        Guid id = AddAndRemoveReloadableSystem(scene);
        ReloadHistoryModule(install: false);
        m_host.modules.generations.Wait();
        Assert.True(m_runtime.interactions.history.Undo().succeeded);
        MissingGameSystem missing = Assert.IsType<MissingGameSystem>(m_session.scenes.Find<GameSystem>(id));
        Assert.True(m_edits.RemoveSystem(scene, missing));
        Assert.True(m_runtime.interactions.history.Undo().succeeded);
        ReloadHistoryModule(install: true);
        AssertRecoveredSystem(id);
        Assert.True(m_runtime.interactions.history.Redo().succeeded);
        Assert.Null(m_session.scenes.Find<GameSystem>(id));
        ReloadHistoryModule(install: false);
        m_host.modules.generations.Wait();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private Guid AddAndRemoveReloadableSystem(GameScene scene)
    {
        Type type = new TypeRef(Guid.Parse("92dd722d-f4ce-468d-8f6d-056cf5b1f79b")).Resolve(m_host.types);
        GameSystem system = scene.AddSystem(type);
        type.GetProperty("value")!.SetValue(system, 92);
        Guid id = system.identity.persistentId;
        Assert.True(m_edits.RemoveSystem(scene, system));
        return id;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void AssertRecoveredSystem(Guid id)
    {
        GameSystem system = Assert.IsAssignableFrom<GameSystem>(m_session.scenes.Find<GameSystem>(id));
        Assert.IsNotType<MissingGameSystem>(system);
        Assert.Equal(92, system.GetType().GetProperty("value")!.GetValue(system));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void AssertRecoveredComponent(Guid componentId, Guid assetId, int missingRuntimeId)
    {
        GameComponent component = Assert.IsAssignableFrom<GameComponent>(m_session.scenes.Find<GameComponent>(componentId));
        Assert.IsNotType<MissingGameComponent>(component);
        Assert.NotEqual(missingRuntimeId, component.identity.runtimeId);
        Assert.Equal(73, component.GetType().GetProperty("value")!.GetValue(component));
        TextAsset asset = Assert.IsType<TextAsset>(component.GetType().GetProperty("asset")!.GetValue(component));
        Assert.False(asset.isMissing);
        Assert.Equal(assetId, asset.identity.persistentId);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void ReloadHistoryModule(bool install)
    {
        AssemblyLoadRequest request = CreateHistoryRequest();
        using AssemblyReloadSession reload = install
            ? m_host.modules.BeginReload([request])
            : m_host.modules.BeginReload([], [request.moduleName]);
        _ = m_reloads.Execute(reload);
    }

    private static AssemblyLoadRequest CreateHistoryRequest()
        => new()
        {
            moduleName = "SceneHistoryRecovery",
            mainAssemblyPath = Path.Combine(AppContext.BaseDirectory, "Modules", "SceneReload", "Inno.Scene.Reload.TestModule.dll"),
            domain = AssemblyDomain.InnoPlugin,
            scope = AssemblyScope.Runtime
        };

    [Fact]
    public void ExternalResolversAreRestoredBeforeDomainPropertyCompensation()
    {
        bool externalCandidateActive = false;
        var participant = new ExternalRecoveryOrder(() => externalCandidateActive);
        using IDisposable registration = m_reloads.Register(participant);
        using AssemblyReloadSession reload = m_host.modules.BeginReload([CreateHistoryRequest()]);
        Assert.Throws<InvalidOperationException>(() => m_reloads.Execute(reload,
            new ExternalContentChange(() => externalCandidateActive = true, () => externalCandidateActive = false)));
        Assert.True(participant.restored);
        Assert.False(externalCandidateActive);
        reload.Dispose();
        m_host.modules.generations.Wait();
        Assert.Equal(GenerationState.Ready, m_host.modules.generations.state);
    }

    private sealed class ExternalContentChange(Action activate, Action restore) : IGenerationChange
    {
        public void PrepareForActivation() { }
        public void Apply() => activate();
        public void Complete() { }
        public void RollbackStructure() { }
        public void RestorePreviousState() => restore();
    }

    private sealed class ExternalRecoveryOrder(Func<bool> externalCandidateActive) : IEditorReloadParticipant, IGenerationChange
    {
        internal bool restored { get; private set; }
        public IGenerationChange Capture(AssemblyReloadContext context) => this;
        public void RefreshDiagnostics() { }
        public void PrepareForActivation() => Assert.False(externalCandidateActive());
        public void Apply()
        {
            Assert.True(externalCandidateActive());
            throw new InvalidOperationException("Rejected domain candidate.");
        }
        public void RollbackStructure() => Assert.True(externalCandidateActive());
        public void RestorePreviousState()
        {
            Assert.False(externalCandidateActive());
            restored = true;
        }
        public void Complete() => Assert.Fail("Failed candidate cannot commit.");
    }

    [Fact]
    public void RemovingAnElementAndSubtreeRestoresExternalSceneReferences()
    {
        GameScene scene = CreateScene();
        GameObject targetObject = scene.CreateObject("Target");
        GameObject targetChild = scene.CreateObject("Child");
        targetChild.transform.SetParent(targetObject.transform);
        var targetComponent = targetChild.AddComponent<HistoryComponent>();
        targetComponent.value = 41;
        GameObject referenceObject = scene.CreateObject("References");
        var references = referenceObject.AddComponent<HistoryReferenceComponent>();
        references.targetComponent = targetComponent;
        references.targetObject = targetChild;
        Guid componentId = targetComponent.identity.persistentId;
        Guid rootId = targetObject.identity.persistentId;
        Guid childId = targetChild.identity.persistentId;

        Assert.True(m_edits.RemoveComponent(targetComponent));
        EditorHistoryResult restoreComponentResult = m_runtime.interactions.history.Undo();
        Assert.True(restoreComponentResult.succeeded, restoreComponentResult.message);
        var restoredComponent = Assert.IsType<HistoryComponent>(IdentityAllocator.current.Get<GameComponent>(componentId));
        Assert.Same(restoredComponent, references.targetComponent);
        Assert.Equal(41, restoredComponent.value);

        Assert.True(m_edits.DeleteGameObject(targetObject));
        Assert.True(m_runtime.interactions.history.Undo().succeeded);
        GameObject restoredRoot = Assert.IsType<GameObject>(IdentityAllocator.current.Get<GameObject>(rootId));
        GameObject restoredChild = Assert.IsType<GameObject>(IdentityAllocator.current.Get<GameObject>(childId));
        Assert.Same(restoredRoot.transform, restoredChild.transform.parent);
        Assert.Same(restoredChild, references.targetObject);
        Assert.Equal(componentId, references.targetComponent?.identity.persistentId);

        Assert.True(m_runtime.interactions.history.Redo().succeeded);
        Assert.Null(IdentityAllocator.current.Get<GameObject>(rootId));
    }

    [Fact]
    public void ElementRestoreRejectsAnotherLogicalTypesPayloadBeforeAttachingElements()
    {
        GameScene scene = CreateScene();
        GameObject sourceObject = scene.CreateObject("Source");
        var source = sourceObject.AddComponent<HistoryReferenceComponent>();
        byte[] incompatibleState = SceneElementSerialization.CaptureState(
            source,
            m_host.serialization,
            m_assets);
        GameObject owner = scene.CreateObject("Owner");
        TypeRef componentType = m_host.types.GetTypeRef(typeof(HistoryComponent));
        TypeRef systemType = m_host.types.GetTypeRef(typeof(HistorySystem));
        Guid componentId = Guid.NewGuid();
        Guid systemId = Guid.NewGuid();

        InvalidDataException componentFailure = Assert.Throws<InvalidDataException>(() =>
            SceneElementSerialization.RestoreComponent(
                owner,
                componentType,
                componentId,
                componentIndex: owner.GetComponents().Count,
                incompatibleState,
                m_host.serialization,
                m_assets));
        InvalidDataException systemFailure = Assert.Throws<InvalidDataException>(() =>
            SceneElementSerialization.RestoreSystem(
                scene,
                systemType,
                systemId,
                systemIndex: scene.GetSystems().Count,
                incompatibleState,
                m_host.serialization,
                m_assets));

        Assert.Contains("type", componentFailure.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("type", systemFailure.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Null(IdentityAllocator.current.Get<GameComponent>(componentId));
        Assert.Null(IdentityAllocator.current.Get<GameSystem>(systemId));
    }

    [Fact]
    public void ElementRestoreRemovesPartialElementWhenRestoreCallbackFails()
    {
        GameScene scene = CreateScene();
        GameObject sourceOwner = scene.CreateObject("Source");
        var source = sourceOwner.AddComponent<RestoreCleanupFailureComponent>();
        byte[] state = SceneElementSerialization.CaptureState(
            source,
            m_host.serialization,
            m_assets);
        GameObject targetOwner = scene.CreateObject("Target");
        TypeRef typeRef = m_host.types.GetTypeRef(typeof(RestoreCleanupFailureComponent));
        Guid persistentId = Guid.NewGuid();

        _ = Assert.ThrowsAny<Exception>(() =>
            SceneElementSerialization.RestoreComponent(
                targetOwner,
                typeRef,
                persistentId,
                componentIndex: targetOwner.GetComponents().Count,
                state,
                m_host.serialization,
                m_assets));

        Assert.Null(IdentityAllocator.current.Get<GameComponent>(persistentId));
    }

    [Fact]
    public void FailedSubtreeRestoreRemovesEveryCreatedObjectDespiteDestructionCallbackFailures()
    {
        GameScene scene = CreateScene();
        GameObject retained = scene.CreateObject("Retained");
        GameObject root = scene.CreateObject("Root");
        GameObject child = scene.CreateObject("Child");
        child.transform.SetParent(root.transform);
        _ = child.AddComponent<SubtreeRestoreFailureComponent>();
        byte[] state = SceneSubtreeSerialization.Capture(
            root,
            m_host.serialization,
            m_assets);
        Guid rootId = root.identity.persistentId;
        Guid childId = child.identity.persistentId;

        Assert.True(scene.DestroyObject(root));
        SubtreeRestoreFailureComponent.throwOnDestroy = true;
        try
        {
            _ = Assert.ThrowsAny<Exception>(() =>
                SceneSubtreeSerialization.Restore(
                    scene,
                    state,
                    m_host.serialization,
                    m_assets,
                    parent: null,
                    siblingIndex: 0));
        }
        finally
        {
            SubtreeRestoreFailureComponent.throwOnDestroy = false;
        }

        Assert.Equal([retained], scene.GetObjects());
        Assert.Null(IdentityAllocator.current.Get<GameObject>(rootId));
        Assert.Null(IdentityAllocator.current.Get<GameObject>(childId));
    }

    [Fact]
    public void FailedSubtreeHistoryRestoreReportsPreservedOnlyAfterExactSceneRecovery()
    {
        GameScene scene = CreateScene();
        GameObject retained = scene.CreateObject("Retained");
        GameObject root = scene.CreateObject("Root");
        GameObject child = scene.CreateObject("Child");
        child.transform.SetParent(root.transform);
        _ = child.AddComponent<SubtreeRestoreFailureComponent>();
        Guid rootId = root.identity.persistentId;
        Guid childId = child.identity.persistentId;

        Assert.True(m_edits.DeleteGameObject(root));
        SubtreeRestoreFailureComponent.throwOnDestroy = true;
        EditorHistoryResult result;
        try
        {
            result = m_runtime.interactions.history.Undo();
        }
        finally
        {
            SubtreeRestoreFailureComponent.throwOnDestroy = false;
        }

        Assert.False(result.succeeded);
        Assert.True(result.statePreserved, result.message);
        Assert.True(m_runtime.interactions.history.canUndo);
        Assert.Equal([retained], scene.GetObjects());
        Assert.Null(IdentityAllocator.current.Get<GameObject>(rootId));
        Assert.Null(IdentityAllocator.current.Get<GameObject>(childId));
    }

    [Fact]
    public void HierarchyUndoRestoresOnlyAffectedPlacements()
    {
        GameScene scene = CreateScene();
        GameObject firstParent = scene.CreateObject("First");
        GameObject secondParent = scene.CreateObject("Second");
        GameObject child = scene.CreateObject("Child");
        child.transform.SetParent(firstParent.transform);

        Assert.True(m_edits.ChangeHierarchy(
            child,
            _ => child.transform.SetParent(secondParent.transform)));
        Assert.Same(secondParent.transform, child.transform.parent);

        Assert.True(m_runtime.interactions.history.Undo().succeeded);
        Assert.Same(firstParent.transform, child.transform.parent);
        Assert.True(m_runtime.interactions.history.Redo().succeeded);
        Assert.Same(secondParent.transform, child.transform.parent);
    }

    [Fact]
    public void SystemHistoryPreservesIdentityStateAndExplicitOrder()
    {
        GameScene scene = CreateScene();
        var first = (HistorySystem)m_edits.AddSystem(scene, typeof(HistorySystem));
        var second = (HistorySystem)m_edits.AddSystem(scene, typeof(HistorySystem));
        first.value = 30;
        Guid firstId = first.identity.persistentId;

        m_edits.SetSystemIndex(scene, second, 0);
        Assert.Equal(0, scene.GetSystemIndex(second));
        Assert.True(m_runtime.interactions.history.Undo().succeeded);
        Assert.Equal(1, scene.GetSystemIndex(second));

        m_edits.ResetSystem(scene, first);
        Assert.Equal(11, first.value);
        Assert.True(m_runtime.interactions.history.Undo().succeeded);
        Assert.Equal(30, first.value);

        Assert.True(m_edits.RemoveSystem(scene, first));
        Assert.True(m_runtime.interactions.history.Undo().succeeded);
        var restored = Assert.IsType<HistorySystem>(IdentityAllocator.current.Get<GameSystem>(firstId));
        Assert.Equal(30, restored.value);
    }

    [Fact]
    public void SceneDocumentUndoRecreatesTheSameLogicalScene()
    {
        GameScene scene = m_edits.CreateScene();
        scene.CreateObject("Persistent");
        Guid sceneId = scene.identity.persistentId;

        Assert.True(m_edits.CloseScene(scene));
        Assert.Empty(SceneManager.loadedScenes);
        Assert.Null(IdentityAllocator.current.Get<GameScene>(sceneId));
        Assert.True(
            m_runtime.interactions.history.canUndo,
            m_runtime.interactions.history.undoUnavailableReason);
        EditorHistoryResult undo = m_runtime.interactions.history.Undo();
        Assert.True(undo.succeeded, undo.message);
        GameScene restored = Assert.IsType<GameScene>(IdentityAllocator.current.Get<GameScene>(sceneId));
        Assert.True(restored.isLoaded);
        Assert.Equal("Persistent", Assert.Single(restored.GetObjects()).name);
        Assert.True(m_runtime.interactions.history.Redo().succeeded);
        Assert.Empty(SceneManager.loadedScenes);
        Assert.Null(IdentityAllocator.current.Get<GameScene>(sceneId));
    }

    [Fact]
    public void SceneHistoryRemainsUsableAfterHostTypeCacheRefresh()
    {
        GameScene scene = CreateScene();
        GameObject gameObject = scene.CreateObject("Before");
        m_edits.RenameGameObject(gameObject, "After");

        m_host.types.Rebuild();
        _ = m_runtime.panelCount;

        Assert.True(m_runtime.interactions.history.Undo().succeeded);
        Assert.Equal("Before", gameObject.name);
        Assert.True(m_runtime.interactions.history.Redo().succeeded);
        Assert.Equal("After", gameObject.name);
    }

    [Fact]
    public void SceneRenameUsesTheSameScalarHistoryContractAsGameObjectRename()
    {
        GameScene scene = CreateScene();
        GameObject gameObject = scene.CreateObject("Object Before");

        m_edits.RenameScene(scene, "Scene After");
        m_edits.RenameGameObject(gameObject, "Object After");

        Assert.Equal("Scene After", scene.name);
        Assert.Equal("Object After", gameObject.name);
        Assert.True(m_runtime.interactions.history.Undo().succeeded);
        Assert.Equal("Object Before", gameObject.name);
        Assert.True(m_runtime.interactions.history.Undo().succeeded);
        Assert.StartsWith("Untitled", scene.name, StringComparison.Ordinal);
        Assert.True(m_runtime.interactions.history.Redo().succeeded);
        Assert.Equal("Scene After", scene.name);
        Assert.True(m_runtime.interactions.history.Redo().succeeded);
        Assert.Equal("Object After", gameObject.name);
    }

    [Fact]
    public void GameObjectTagUndoAndRedoRefreshSceneQueries()
    {
        GameScene scene = CreateScene();
        GameObject gameObject = scene.CreateObject("Tagged");

        m_edits.SetGameObjectTag(gameObject, "Player");

        Assert.Equal("Player", gameObject.tag);
        Assert.Same(gameObject, scene.FindObjectWithTag("Player"));
        Assert.True(m_runtime.interactions.history.Undo().succeeded);
        Assert.Equal(GameObject.defaultTag, gameObject.tag);
        Assert.Null(scene.FindObjectWithTag("Player"));
        Assert.True(m_runtime.interactions.history.Redo().succeeded);
        Assert.Equal("Player", gameObject.tag);
        Assert.Same(gameObject, scene.FindObjectWithTag("Player"));
    }

    [Fact]
    public void GameObjectLayerUndoAndRedoRefreshSceneQueries()
    {
        GameScene scene = CreateScene();
        GameObject gameObject = scene.CreateObject("Layered");
        var gameplay = new GameLayer(6);

        m_edits.SetGameObjectLayer(gameObject, gameplay);

        Assert.Equal(gameplay, gameObject.layer);
        Assert.Same(gameObject, scene.FindObjectWithLayer(gameplay));
        Assert.True(m_runtime.interactions.history.Undo().succeeded);
        Assert.Equal(GameLayer.defaultLayer, gameObject.layer);
        Assert.Null(scene.FindObjectWithLayer(gameplay));
        Assert.True(m_runtime.interactions.history.Redo().succeeded);
        Assert.Equal(gameplay, gameObject.layer);
        Assert.Same(gameObject, scene.FindObjectWithLayer(gameplay));
    }

    private GameScene CreateScene()
        => m_session.scenes.LoadNewSceneAdditive();
}

[EditorModule("tests.scene-history-probe", order: 220)]
public sealed class SceneHistoryProbe : EditorModule
{
    public SceneHistoryProbe(SceneEdits sceneEdits, IEditorSceneWorkspace workspace)
    {
        edits = sceneEdits;
        documents = workspace;
    }

    public static SceneEdits? edits { get; private set; }

    public static IEditorSceneWorkspace? documents { get; private set; }

    public static void Reset()
    {
        edits = null;
        documents = null;
    }
}

[StableTypeId("267e77d8-1112-4cf9-a9f1-01d9a1e59bbc")]
[AllowMultipleComponent]
internal sealed class HistoryComponent : GameBehavior
{
    [SerializableProperty]
    public int value { get; set; }

    protected override void Reset()
    {
        value = 7;
    }
}

[StableTypeId("b269c970-8afe-46a0-a4a0-f44f737d059a")]
internal sealed class HistoryReferenceComponent : GameComponent
{
    [SerializableProperty]
    public GameObject? targetObject { get; set; }

    [SerializableProperty]
    public GameComponent? targetComponent { get; set; }
}

[StableTypeId("edbd3940-a94c-499e-8f03-b0e232a39b21")]
[AllowMultipleComponent]
internal sealed class HistoryAssetReferenceComponent : GameComponent
{
    [SerializableProperty]
    public TextAsset? asset { get; set; }
}

[StableTypeId("f00d5950-1136-4393-b246-644b69948f90")]
[AllowMultipleSystem]
internal sealed class HistorySystem : GameSystem
{
    [SerializableProperty]
    public int value { get; set; }

    protected override void Reset()
    {
        value = 11;
    }
}

[StableTypeId("71cc27da-2c47-47f2-a037-c9bc29615358")]
[AllowMultipleComponent]
internal sealed class RestoreCleanupFailureComponent : GameBehavior
{
    [SerializableProperty]
    public int value { get; set; } = 5;

    [OnSerializableRestored]
    private void OnRestored()
        => throw new InvalidOperationException("Injected restore callback failure.");
}

[StableTypeId("8503e97b-1845-4ab2-a116-1ece04934a4b")]
[AllowMultipleComponent]
internal sealed class SubtreeRestoreFailureComponent : GameBehavior
{
    internal static bool throwOnDestroy;

    [SerializableProperty]
    public int value { get; set; } = 5;

    [OnSerializableRestored]
    private void OnRestored()
        => throw new InvalidOperationException("Injected subtree restore callback failure.");

    protected override void OnDestroy()
    {
        if (throwOnDestroy)
            throw new InvalidOperationException("Injected subtree destruction callback failure.");
    }
}
