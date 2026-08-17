using System;
using System.Linq;
using System.Runtime.CompilerServices;

using Inno.Core.Mathematics;
using Inno.Core.Reflection;
using Inno.Core.Serialization;
using Inno.Core.Serialization.Converters;
using Inno.Engine.Scene;
using Inno.Engine.Scene.Components;

using Xunit;

namespace Inno.Engine.Scene.Tests;

[Collection(SceneTestsCollection.NAME)]
public sealed class SceneSerializationTests : IDisposable
{
    public SceneSerializationTests(SceneTestsFixture _)
    {
    }

    public void Dispose()
    {
        SceneManager.UnloadAllScenes();
    }

    [Fact]
    public void SceneRoundtrip_PreservesHierarchyOrderStateAndDirectReferences()
    {
        Assert.Contains(
            TypeCache.GetTypesWithAttribute<SerializationExtensionAttribute>(),
            static type => type.Name == "GameSceneConverter");
        var source = new GameScene("Roundtrip");
        GameObject root = source.CreateObject("Root");
        GameObject child = source.CreateObject("Child");
        child.transform.SetParent(root.transform);
        child.transform.localPosition = new Vector3(1, 2, 3);
        child.SetActive(false);
        ReferenceComponent rootReference = root.AddComponent<ReferenceComponent>();
        ReferenceComponent childReference = child.AddComponent<ReferenceComponent>();
        rootReference.value = 10;
        childReference.value = 20;
        rootReference.targetObject = child;
        rootReference.targetComponent = childReference;
        childReference.targetObject = root;
        childReference.targetComponent = rootReference;

        Guid sceneId = source.identity.persistentId;
        Guid rootId = root.identity.persistentId;
        Guid childId = child.identity.persistentId;
        byte[] bytes = SerializationManager.Serialize(source);
        SceneManager.LoadScene(source);
        Assert.True(SceneManager.UnloadScene(source));

        GameScene restored = SerializationManager.Deserialize<GameScene>(bytes);
        SceneManager.LoadScene(restored);

        Assert.Equal(sceneId, restored.identity.persistentId);
        Assert.Equal("Roundtrip", restored.name);
        Assert.Equal(2, restored.GetObjects().Count);
        GameObject restoredRoot = restored.GetObjects().Single(gameObject => gameObject.identity.persistentId == rootId);
        GameObject restoredChild = restored.GetObjects().Single(gameObject => gameObject.identity.persistentId == childId);
        Assert.Equal(new[] { typeof(Transform), typeof(ReferenceComponent) },
            restoredRoot.GetComponents().Select(static component => component.GetType()));
        Assert.Same(restoredRoot.transform, restoredChild.transform.parent);
        Assert.Equal(new Vector3(1, 2, 3), restoredChild.transform.localPosition);
        Assert.False(restoredChild.activeSelf);
        Assert.False(restoredChild.activeInHierarchy);

        ReferenceComponent restoredRootReference = restoredRoot.GetComponent<ReferenceComponent>();
        ReferenceComponent restoredChildReference = restoredChild.GetComponent<ReferenceComponent>();
        Assert.Equal(10, restoredRootReference.value);
        Assert.Equal(20, restoredChildReference.value);
        Assert.Same(restoredChild, restoredRootReference.targetObject);
        Assert.Same(restoredChildReference, restoredRootReference.targetComponent);
        Assert.Same(restoredRoot, restoredChildReference.targetObject);
        Assert.Same(restoredRootReference, restoredChildReference.targetComponent);
        Assert.Equal(0, restoredRootReference.resetCount);
        Assert.Equal(0, restoredChildReference.resetCount);
    }

    [Fact]
    public void PrefabInstantiation_RemapsIdentitiesAndIsolatesInternalReferences()
    {
        var sourceScene = new GameScene("PrefabSource");
        GameObject sourceRoot = sourceScene.CreateObject("Root");
        GameObject sourceChild = sourceScene.CreateObject("Child");
        sourceChild.transform.SetParent(sourceRoot.transform);
        ReferenceComponent sourceReference = sourceRoot.AddComponent<ReferenceComponent>();
        ReferenceComponent sourceChildReference = sourceChild.AddComponent<ReferenceComponent>();
        sourceReference.targetObject = sourceChild;
        sourceReference.targetComponent = sourceChildReference;
        byte[] bytes = SerializationManager.Serialize(sourceRoot);

        var targetScene = new GameScene("Target");
        SerializationContext context = SerializationContext.empty.With(targetScene);
        GameObject first = SerializationManager.Deserialize<GameObject>(bytes, context);
        GameObject second = SerializationManager.Deserialize<GameObject>(bytes, context);
        SceneManager.LoadScene(sourceScene);
        SceneManager.LoadSceneAdditive(targetScene);

        Assert.NotSame(first, second);
        Assert.NotEqual(first.identity.persistentId, second.identity.persistentId);
        GameObject firstChild = Assert.Single(first.transform.children).gameObject;
        GameObject secondChild = Assert.Single(second.transform.children).gameObject;
        ReferenceComponent firstReference = first.GetComponent<ReferenceComponent>();
        ReferenceComponent secondReference = second.GetComponent<ReferenceComponent>();
        Assert.Same(firstChild, firstReference.targetObject);
        Assert.Same(firstChild.GetComponent<ReferenceComponent>(), firstReference.targetComponent);
        Assert.Same(secondChild, secondReference.targetObject);
        Assert.Same(secondChild.GetComponent<ReferenceComponent>(), secondReference.targetComponent);
        Assert.NotSame(firstReference.targetObject, secondReference.targetObject);
        Assert.NotEqual(
            firstReference.targetComponent!.identity.persistentId,
            secondReference.targetComponent!.identity.persistentId);
    }

    [Fact]
    public void PrefabCapture_RejectsReferenceOutsideCapturedSubtreeWithDiagnosticContext()
    {
        var scene = new GameScene("Boundary");
        GameObject root = scene.CreateObject("Root");
        GameObject external = scene.CreateObject("External");
        root.AddComponent<ReferenceComponent>().targetObject = external;

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => SerializationManager.Serialize(root));

        Assert.Contains(typeof(ReferenceComponent).FullName!, exception.Message);
        Assert.Contains("targetObject", exception.Message);
        Assert.Contains(external.identity.persistentId.ToString(), exception.Message);
        Assert.Contains(root.identity.persistentId.ToString(), exception.Message);

        SceneManager.LoadScene(scene);
        Assert.True(SceneManager.UnloadScene(scene));
    }

    [Fact]
    public void SceneUnload_ReleasesCyclicSceneObjectGraphForGarbageCollection()
    {
        (WeakReference<GameScene> scene, WeakReference<GameObject> gameObject, WeakReference<GameComponent> component) =
            CreateAndUnloadCyclicGraph();

        for (int i = 0; i < 8; i++)
        {
            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
        }

        Assert.False(scene.TryGetTarget(out _));
        Assert.False(gameObject.TryGetTarget(out _));
        Assert.False(component.TryGetTarget(out _));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (
        WeakReference<GameScene> scene,
        WeakReference<GameObject> gameObject,
        WeakReference<GameComponent> component) CreateAndUnloadCyclicGraph()
    {
        var scene = new GameScene("GC");
        GameObject first = scene.CreateObject("First");
        GameObject second = scene.CreateObject("Second");
        second.transform.SetParent(first.transform);
        ReferenceComponent firstReference = first.AddComponent<ReferenceComponent>();
        ReferenceComponent secondReference = second.AddComponent<ReferenceComponent>();
        firstReference.targetObject = second;
        firstReference.targetComponent = secondReference;
        secondReference.targetObject = first;
        secondReference.targetComponent = firstReference;

        var sceneReference = new WeakReference<GameScene>(scene);
        var objectReference = new WeakReference<GameObject>(first);
        var componentReference = new WeakReference<GameComponent>(firstReference);
        SceneManager.LoadScene(scene);
        Assert.True(SceneManager.UnloadScene(scene));
        return (sceneReference, objectReference, componentReference);
    }
}

[StableTypeId("09d35197-7161-4fdf-8d60-95ecf3555c0c")]
internal sealed class ReferenceComponent : GameComponent
{
    [SerializableProperty] public int value { get; set; }
    [SerializableProperty] public GameObject? targetObject { get; set; }
    [SerializableProperty] public ReferenceComponent? targetComponent { get; set; }
    public int resetCount { get; private set; }

    private void Reset()
    {
        resetCount++;
    }
}
