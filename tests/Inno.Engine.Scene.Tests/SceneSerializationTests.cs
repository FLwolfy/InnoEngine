using System;
using System.Linq;
using System.Runtime.CompilerServices;

using Inno.Core.Mathematics;
using Inno.Core.Reflection;
using Inno.Core.Serialization;
using Inno.Core.Serialization.Converters;
using Inno.Engine.Scene;
using Inno.Engine.Scene.Assets;
using Inno.Engine.Scene.Components;
using Inno.Engine.Scene.Layers;

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
    public void MissingSceneTypeDiagnosticPreservesStableIdentity()
    {
        Guid stableTypeId = Guid.Parse("80f1fb70-95f7-4db2-b338-2567cf8bb2c1");

        var exception = new SceneTypeResolutionException(stableTypeId, "component");

        Assert.Equal(stableTypeId, exception.stableTypeId);
        Assert.Equal("component", exception.elementKind);
        Assert.Contains(stableTypeId.ToString("D"), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SceneRoundtrip_PreservesHierarchyOrderStateAndDirectReferences()
    {
        var source = new GameScene("Roundtrip");
        GameObject root = source.CreateObject("Root");
        GameObject child = source.CreateObject("Child");
        root.tag = "Player";
        child.tag = "Companion";
        root.layer = new GameLayer(3);
        child.layer = new GameLayer(7);
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
        Assert.Contains(
            TypeCacheManager.GetTypesWithAttribute<SerializationExtensionAttribute>(),
            static type => type.Name == "GameSceneConverter");
        SceneManager.LoadScene(source);
        Assert.True(SceneManager.UnloadScene(source));

        GameScene restored = SerializationManager.Deserialize<GameScene>(bytes);
        SceneManager.LoadScene(restored);

        Assert.Equal(sceneId, restored.identity.persistentId);
        Assert.Equal("Roundtrip", restored.name);
        Assert.Equal(2, restored.GetObjects().Count);
        GameObject restoredRoot = restored.GetObjects().Single(gameObject => gameObject.identity.persistentId == rootId);
        GameObject restoredChild = restored.GetObjects().Single(gameObject => gameObject.identity.persistentId == childId);
        Assert.Equal("Player", restoredRoot.tag);
        Assert.Equal("Companion", restoredChild.tag);
        Assert.Equal(new GameLayer(3), restoredRoot.layer);
        Assert.Equal(new GameLayer(7), restoredChild.layer);
        Assert.Same(restoredRoot, restored.FindObjectWithLayer(new GameLayer(3)));
        Assert.Same(restoredRoot, restored.FindObjectWithTag("Player"));
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
        sourceRoot.tag = "Spawn";
        sourceChild.tag = "Collectible";
        sourceRoot.layer = new GameLayer(4);
        sourceChild.layer = new GameLayer(5);
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
        Assert.Equal("Spawn", first.tag);
        Assert.Equal("Collectible", firstChild.tag);
        Assert.Equal(new GameLayer(4), first.layer);
        Assert.Equal(new GameLayer(5), firstChild.layer);
        Assert.Equal("Spawn", second.tag);
        Assert.Equal("Collectible", secondChild.tag);
        Assert.Equal(new GameLayer(4), second.layer);
        Assert.Equal(new GameLayer(5), secondChild.layer);
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
    public void SceneRoundtripPreservesManualComponentAndSystemOrder()
    {
        var source = new GameScene("Manual Order");
        GameObject gameObject = source.CreateObject("Object");
        _ = gameObject.AddComponent<OrderComponentA>();
        OrderComponentB componentB = gameObject.AddComponent<OrderComponentB>();
        gameObject.SetComponentIndex(componentB, 1);
        _ = source.AddSystem<OrderSerializationSystemA>();
        OrderSerializationSystemB systemB = source.AddSystem<OrderSerializationSystemB>();
        source.SetSystemIndex(systemB, 0);

        byte[] bytes = SerializationManager.Serialize(source);
        SceneManager.LoadScene(source);
        Assert.True(SceneManager.UnloadScene(source));
        GameScene restored = SerializationManager.Deserialize<GameScene>(bytes);

        Assert.Equal(
            [typeof(Transform), typeof(OrderComponentB), typeof(OrderComponentA)],
            Assert.Single(restored.GetObjects()).GetComponents().Select(static component => component.GetType()));
        Assert.Equal(
            [typeof(OrderSerializationSystemB), typeof(OrderSerializationSystemA)],
            restored.GetSystems().Select(static system => system.GetType()));
    }

    [Fact]
    public void SerializableLayerValues_RoundtripThroughComponentState()
    {
        var scene = new GameScene("GameLayer Values");
        LayerValueComponent component = scene.CreateObject("Object").AddComponent<LayerValueComponent>();
        component.layer = new GameLayer(9);
        component.mask = GameLayerMask.FromLayers([new GameLayer(2), new GameLayer(9)]);

        byte[] bytes = SerializationManager.Serialize(scene);
        SceneManager.LoadScene(scene);
        Assert.True(SceneManager.UnloadScene(scene));
        GameScene restored = SerializationManager.Deserialize<GameScene>(bytes);
        LayerValueComponent restoredComponent = Assert.Single(restored.GetObjects())
            .GetComponent<LayerValueComponent>();

        Assert.Equal(new GameLayer(9), restoredComponent.layer);
        Assert.True(restoredComponent.mask.Contains(new GameLayer(2)));
        Assert.True(restoredComponent.mask.Contains(new GameLayer(9)));
    }

    [Fact]
    public void LoadedSceneRestoreKeepsSceneIdentityAndRebuildsItsObjectGraph()
    {
        var scene = new GameScene("Before");
        GameObject original = scene.CreateObject("Original");
        Guid sceneId = scene.identity.persistentId;
        Guid objectId = original.identity.persistentId;
        byte[] before = SerializationManager.Serialize(scene);
        SceneManager.LoadScene(scene);

        scene.name = "After";
        _ = scene.CreateObject("Added");
        SerializationManager.Restore(scene, before);

        Assert.Same(scene, SceneManager.activeScene);
        Assert.Equal(sceneId, scene.identity.persistentId);
        Assert.Equal("Before", scene.name);
        GameObject restored = Assert.Single(scene.GetObjects());
        Assert.Equal(objectId, restored.identity.persistentId);
        Assert.Equal("Original", restored.name);
        Assert.True(scene.isLoaded);
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

    protected override void Reset()
    {
        resetCount++;
    }
}

[StableTypeId("f7bc9bf3-288a-45b3-9caa-26289d66c181")]
internal sealed class OrderSerializationSystemA : GameSystem;

[StableTypeId("6924359d-798d-4384-bdb2-1b0e34ab3fbb")]
internal sealed class OrderSerializationSystemB : GameSystem;

[StableTypeId("821c7b92-9aeb-40cb-924e-169004199ef0")]
internal sealed class LayerValueComponent : GameComponent
{
    [SerializableProperty]
    public GameLayer layer { get; set; }

    [SerializableProperty]
    public GameLayerMask mask { get; set; }
}
