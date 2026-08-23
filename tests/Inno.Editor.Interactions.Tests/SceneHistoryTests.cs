using System;
using System.IO;
using System.Linq;

using Inno.Core.Assemblies;
using Inno.Core.Identity;
using Inno.Core.Reflection;
using Inno.Core.Serialization;
using Inno.Editor.Scene;
using Inno.Engine.Scene;
using Inno.Engine.Scene.Components;
using Xunit;

namespace Inno.Editor.Interactions.Tests;

public sealed class SceneHistoryTests : IDisposable
{
    private readonly string m_projectRoot = Path.Combine(
        Path.GetTempPath(),
        "InnoSceneHistoryTests",
        Guid.NewGuid().ToString("N"));
    private readonly EditorInteractionRuntime m_runtime;
    private readonly EditorSceneWorkspace m_workspace;
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
        m_workspace = new EditorSceneWorkspace(m_runtime.interactions);
        m_edits = new SceneEdits(m_workspace, m_runtime.interactions);
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
        GameScene scene = m_workspace.CreateScene();
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
        GameScene scene = m_workspace.CreateScene();
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
        GameScene scene = m_workspace.CreateScene();
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
        Assert.True(m_runtime.interactions.history.Undo().succeeded);
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
    public void HierarchyUndoRestoresOnlyAffectedPlacements()
    {
        GameScene scene = m_workspace.CreateScene();
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
        GameScene scene = m_workspace.CreateScene();
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
        GameScene scene = m_workspace.CreateScene();
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
    public void GameObjectTagUndoAndRedoRefreshSceneQueries()
    {
        GameScene scene = m_workspace.CreateScene();
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
