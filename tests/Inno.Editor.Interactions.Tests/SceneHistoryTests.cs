using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;

using Inno.Core.Assemblies;
using Inno.Core.Identity;
using Inno.Core.Reflection;
using Inno.Core.Serialization;
using Inno.Editor.Scene;
using Inno.Engine.Scene;
using Inno.Engine.Scene.Assets;
using Inno.Engine.Scene.Components;
using Inno.Engine.Scene.Layers;
using Xunit;

namespace Inno.Editor.Interactions.Tests;

public sealed class SceneHistoryTests : IDisposable
{
    private readonly string m_projectRoot = Path.Combine(
        Path.GetTempPath(),
        "InnoSceneHistoryTests",
        Guid.NewGuid().ToString("N"));
    private readonly EditorInteractionRuntime m_runtime;
    private readonly object m_workspace;
    private readonly SceneEdits m_edits;

    public SceneHistoryTests()
    {
        Directory.CreateDirectory(Path.Combine(m_projectRoot, "Assets"));
        IdentityManager.Initialize();
        AssemblyManager.Initialize(new AssemblyManagerOptions
        {
            cacheDirectory = Path.Combine(m_projectRoot, "Library", "Assemblies")
        });
        TypeCacheManager.Initialize();
        SerializationManager.Initialize();
        m_runtime = new EditorInteractionRuntime(m_projectRoot);
        m_runtime.Start();
        Type workspaceType = typeof(SceneEdits).Assembly.GetType(
            "Inno.Editor.Scene.EditorSceneWorkspace",
            throwOnError: true)!;
        m_workspace = Activator.CreateInstance(
            workspaceType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            args: [m_runtime.interactions],
            culture: null)!;
        m_edits = (SceneEdits)Activator.CreateInstance(
            typeof(SceneEdits),
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            args: [m_workspace, m_runtime.interactions],
            culture: null)!;
    }

    public void Dispose()
    {
        m_runtime.Dispose();
        SceneManager.UnloadAllScenes();
        SerializationManager.Shutdown();
        TypeCacheManager.Shutdown();
        AssemblyManager.Shutdown();
        IdentityManager.Shutdown();
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

        Assert.True(m_runtime.interactions.history.Undo().succeeded);
        Assert.Equal(10, component.value);
        Assert.Same(scene, IdentityManager.Get<GameScene>(sceneId));
        Assert.Same(gameObject, IdentityManager.Get<GameObject>(objectId));
        Assert.Same(component, IdentityManager.Get<GameComponent>(componentId));

        Assert.True(m_runtime.interactions.history.Redo().succeeded);
        Assert.Equal(25, component.value);
    }

    [Fact]
    public void ComponentAddRemoveResetAndOrderPreserveLogicalIdentity()
    {
        GameScene scene = CreateScene();
        GameObject gameObject = scene.CreateObject("Object");
        var first = (HistoryComponent)m_edits.AddComponent(gameObject, typeof(HistoryComponent));
        Guid firstId = first.identity.persistentId;

        Assert.True(m_runtime.interactions.history.Undo().succeeded);
        Assert.Null(IdentityManager.Get<GameComponent>(firstId));
        Assert.True(m_runtime.interactions.history.Redo().succeeded);
        var restoredFirst = Assert.IsType<HistoryComponent>(IdentityManager.Get<GameComponent>(firstId));
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
        Assert.Same(restoredFirst, IdentityManager.Get<GameComponent>(firstId));

        Assert.True(m_edits.RemoveComponent(restoredFirst));
        Assert.Null(IdentityManager.Get<GameComponent>(firstId));
        Assert.True(m_runtime.interactions.history.Undo().succeeded);
        var restoredAgain = Assert.IsType<HistoryComponent>(IdentityManager.Get<GameComponent>(firstId));
        Assert.Equal(99, restoredAgain.value);
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
        var restoredComponent = Assert.IsType<HistoryComponent>(IdentityManager.Get<GameComponent>(componentId));
        Assert.Same(restoredComponent, references.targetComponent);
        Assert.Equal(41, restoredComponent.value);

        Assert.True(m_edits.DeleteGameObject(targetObject));
        Assert.True(m_runtime.interactions.history.Undo().succeeded);
        GameObject restoredRoot = Assert.IsType<GameObject>(IdentityManager.Get<GameObject>(rootId));
        GameObject restoredChild = Assert.IsType<GameObject>(IdentityManager.Get<GameObject>(childId));
        Assert.Same(restoredRoot.transform, restoredChild.transform.parent);
        Assert.Same(restoredChild, references.targetObject);
        Assert.Equal(componentId, references.targetComponent?.identity.persistentId);

        Assert.True(m_runtime.interactions.history.Redo().succeeded);
        Assert.Null(IdentityManager.Get<GameObject>(rootId));
    }

    [Fact]
    public void SceneHistoryCompensationUsesTheObservedPostconditionAfterRemovalThrows()
    {
        GameScene scene = CreateScene();
        GameObject retained = scene.CreateObject("Retained");

        bool lost = RemoveWithCompensation(
            retained,
            static () => throw new InvalidOperationException("before removal"),
            "Retained object");

        Assert.False(lost);
        Assert.True(retained.isRuntimeValid);

        GameObject unregisteredOnly = scene.CreateObject("Unregistered only");
        bool incomplete = RemoveWithCompensation(
            unregisteredOnly,
            () => IdentityManager.Unregister(unregisteredOnly),
            "Unregistered-only object");

        Assert.False(incomplete);
        Assert.False(unregisteredOnly.isDestroyed);
        Assert.True(scene.DestroyObject(unregisteredOnly));

        GameObject removed = scene.CreateObject("Removed");
        bool preserved = RemoveWithCompensation(
            removed,
            () =>
            {
                _ = scene.DestroyObject(removed);
                throw new InvalidOperationException("after removal");
            },
            "Removed object");

        Assert.True(preserved);
        Assert.False(removed.isRuntimeValid);
        Assert.Null(IdentityManager.Get<GameObject>(removed.identity.persistentId));
    }

    [Fact]
    public void ElementRestoreRejectsIgnoredPropertiesAndRemovesPartialElements()
    {
        GameScene scene = CreateScene();
        GameObject sourceObject = scene.CreateObject("Source");
        var source = sourceObject.AddComponent<HistoryReferenceComponent>();
        byte[] incompatibleState = ScenePropertySerialization.CaptureProperties(source);
        GameObject owner = scene.CreateObject("Owner");
        TypeRef componentType = TypeCacheManager.GetTypeRef(typeof(HistoryComponent));
        TypeRef systemType = TypeCacheManager.GetTypeRef(typeof(HistorySystem));
        Guid componentId = Guid.NewGuid();
        Guid systemId = Guid.NewGuid();

        InvalidOperationException componentFailure = Assert.Throws<InvalidOperationException>(() =>
            SceneElementSerialization.RestoreComponent(
                owner,
                componentType,
                componentId,
                componentIndex: owner.GetComponents().Count,
                incompatibleState));
        InvalidOperationException systemFailure = Assert.Throws<InvalidOperationException>(() =>
            SceneElementSerialization.RestoreSystem(
                scene,
                systemType,
                systemId,
                systemIndex: scene.GetSystems().Count,
                incompatibleState));

        Assert.Contains("incomplete", componentFailure.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("incomplete", systemFailure.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Null(IdentityManager.Get<GameComponent>(componentId));
        Assert.Null(IdentityManager.Get<GameSystem>(systemId));
    }

    [Fact]
    public void ElementRestoreRemovesPartialElementWhenRestoreCallbackFails()
    {
        GameScene scene = CreateScene();
        GameObject sourceOwner = scene.CreateObject("Source");
        var source = sourceOwner.AddComponent<RestoreCleanupFailureComponent>();
        byte[] state = ScenePropertySerialization.CaptureProperties(source);
        GameObject targetOwner = scene.CreateObject("Target");
        TypeRef typeRef = TypeCacheManager.GetTypeRef(typeof(RestoreCleanupFailureComponent));
        Guid persistentId = Guid.NewGuid();

        _ = Assert.ThrowsAny<Exception>(() =>
            SceneElementSerialization.RestoreComponent(
                targetOwner,
                typeRef,
                persistentId,
                componentIndex: targetOwner.GetComponents().Count,
                state));

        Assert.Null(IdentityManager.Get<GameComponent>(persistentId));
    }

    [Fact]
    public void ElementRestoreReportsCleanupFailureAfterThePartialElementWasRemoved()
    {
        GameScene scene = CreateScene();
        GameObject partialElement = scene.CreateObject("Partial");
        Guid persistentId = partialElement.identity.persistentId;
        var restoreFailure = new InvalidOperationException("Injected restore failure.");

        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(() =>
            RethrowElementRestoreAfterCleanup(
                restoreFailure,
                partialElement,
                () =>
                {
                    Assert.True(scene.DestroyObject(partialElement));
                    throw new InvalidOperationException("Injected cleanup callback failure.");
                },
                "element"));

        Assert.Contains("cleanup", failure.Message, StringComparison.OrdinalIgnoreCase);
        AggregateException aggregate = Assert.IsType<AggregateException>(failure.InnerException);
        Assert.Contains(restoreFailure, aggregate.InnerExceptions);
        Assert.Null(IdentityManager.Get<GameObject>(persistentId));
    }

    [Fact]
    public void ElementRestoreUsesTheObservedCleanupPostconditionWhenRemovalReportsFalse()
    {
        GameScene scene = CreateScene();
        GameObject partialElement = scene.CreateObject("Partial");
        Guid persistentId = partialElement.identity.persistentId;
        var restoreFailure = new InvalidOperationException("Injected restore failure.");

        Exception failure = Assert.Throws<InvalidOperationException>(() =>
            RethrowElementRestoreAfterCleanup(
                restoreFailure,
                partialElement,
                () =>
                {
                    Assert.True(scene.DestroyObject(partialElement));
                    return false;
                },
                "element"));

        Assert.Same(restoreFailure, failure);
        Assert.Null(IdentityManager.Get<GameObject>(persistentId));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ElementRestoreRejectsAnyCleanupResultThatLeavesThePartialElementActive(
        bool reportedRemoved)
    {
        GameScene scene = CreateScene();
        GameObject partialElement = scene.CreateObject("Partial");
        Guid persistentId = partialElement.identity.persistentId;

        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(() =>
            RethrowElementRestoreAfterCleanup(
                new InvalidOperationException("Injected restore failure."),
                partialElement,
                () => reportedRemoved,
                "element"));

        Assert.Contains("cleanup", failure.Message, StringComparison.OrdinalIgnoreCase);
        Assert.IsType<AggregateException>(failure.InnerException);
        Assert.Same(partialElement, IdentityManager.Get<GameObject>(persistentId));
        Assert.True(scene.DestroyObject(partialElement));
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
        byte[] state = SceneSubtreeSerialization.Capture(root);
        Guid rootId = root.identity.persistentId;
        Guid childId = child.identity.persistentId;

        Assert.True(scene.DestroyObject(root));
        SubtreeRestoreFailureComponent.throwOnDestroy = true;
        try
        {
            _ = Assert.ThrowsAny<Exception>(() =>
                SceneSubtreeSerialization.Restore(scene, state, parent: null, siblingIndex: 0));
        }
        finally
        {
            SubtreeRestoreFailureComponent.throwOnDestroy = false;
        }

        Assert.Equal([retained], scene.GetObjects());
        Assert.Null(IdentityManager.Get<GameObject>(rootId));
        Assert.Null(IdentityManager.Get<GameObject>(childId));
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
        Assert.Null(IdentityManager.Get<GameObject>(rootId));
        Assert.Null(IdentityManager.Get<GameObject>(childId));
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
            () => child.transform.SetParent(secondParent.transform)));
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
        var restored = Assert.IsType<HistorySystem>(IdentityManager.Get<GameSystem>(firstId));
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
        Assert.Null(IdentityManager.Get<GameScene>(sceneId));
        Assert.True(m_runtime.interactions.history.Undo().succeeded);
        GameScene restored = Assert.IsType<GameScene>(IdentityManager.Get<GameScene>(sceneId));
        Assert.True(restored.isLoaded);
        Assert.Equal("Persistent", Assert.Single(restored.GetObjects()).name);
        Assert.True(m_runtime.interactions.history.Redo().succeeded);
        Assert.Empty(SceneManager.loadedScenes);
        Assert.Null(IdentityManager.Get<GameScene>(sceneId));
    }

    [Fact]
    public void SceneHistoryRemainsUsableAfterHostTypeCacheRefresh()
    {
        GameScene scene = CreateScene();
        GameObject gameObject = scene.CreateObject("Before");
        m_edits.RenameGameObject(gameObject, "After");

        TypeCacheManager.Rebuild();
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
    {
        MethodInfo method = m_workspace.GetType().GetMethod(
            "CreateScene",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;
        return (GameScene)method.Invoke(m_workspace, parameters: null)!;
    }

    private static bool RemoveWithCompensation(
        EngineObject target,
        Func<bool> remove,
        string description)
    {
        Type compensationType = typeof(SceneEdits).Assembly.GetType(
            "Inno.Editor.Scene.SceneHistoryCompensation",
            throwOnError: true)!;
        MethodInfo method = compensationType.GetMethod(
            "Remove",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!;
        object result = method.Invoke(null, [target, remove, description])!;
        return (bool)result.GetType().GetProperty(
            "statePreserved",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(result)!;
    }

    private static void RethrowElementRestoreAfterCleanup(
        Exception restoreFailure,
        EngineObject element,
        Func<bool> remove,
        string kind)
    {
        MethodInfo method = typeof(SceneElementSerialization).GetMethod(
            "RethrowAfterCleanup",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!;
        try
        {
            _ = method.Invoke(null, [restoreFailure, element, remove, kind]);
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
        }
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
