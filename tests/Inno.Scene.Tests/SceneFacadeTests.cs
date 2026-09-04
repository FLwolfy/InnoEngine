using System;
using System.Linq;

using Inno.Scene;
using Inno.Runtime;
using Inno.Scene.Components;

using Xunit;

namespace Inno.Scene.Tests;

[Collection(SceneTestsCollection.NAME)]
public sealed class SceneFacadeTests : IDisposable
{
    private readonly IDisposable m_sceneScope;

    public SceneFacadeTests(SceneTestsFixture fixture)
    {
        m_sceneScope = fixture.world.EnterScope();
    }

    public void Dispose()
    {
        SceneManager.UnloadAllScenes();
        m_sceneScope.Dispose();
    }

    [Fact]
    public void CreateObject_StoresDataInSceneStore_AndReturnsFacade()
    {
        var scene = new GameScene("Test");

        GameObject gameObject = scene.CreateObject("Cube");

        Assert.True(gameObject.isRuntimeValid);
        Assert.Equal("Cube", gameObject.name);
        Assert.Equal(GameObject.defaultTag, gameObject.tag);
        Assert.True(gameObject.activeSelf);
        Assert.True(gameObject.HasComponent<Transform>());
        Assert.Single(scene.GetObjects());
    }

    [Fact]
    public void NameAndTagQueriesTrackObjectMetadataChangesInStorageOrder()
    {
        var scene = new GameScene("Queries");
        GameObject first = scene.CreateObject("First");
        GameObject second = scene.CreateObject("Second");
        first.tag = "Player";
        second.tag = "Player";

        Assert.Same(first, scene.FindObject("First"));
        Assert.Same(first, scene.FindObjectWithTag("Player"));
        Assert.Equal([first, second], scene.FindObjectsWithTag("Player"));

        first.name = "Renamed";
        first.tag = "Enemy";

        Assert.Null(scene.FindObject("First"));
        Assert.Same(first, scene.FindObject("Renamed"));
        Assert.Same(second, scene.FindObjectWithTag("Player"));
        Assert.Equal([second], scene.FindObjectsWithTag("Player"));
        Assert.Equal([first], scene.FindObjectsWithTag("Enemy"));
    }

    [Fact]
    public void OrderedSceneQueriesPreserveCreationOrderAfterDenseStorageRemoval()
    {
        var scene = new GameScene("Stable Order");
        GameObject removed = scene.CreateObject("Removed");
        GameObject second = scene.CreateObject("Second");
        GameObject third = scene.CreateObject("Third");
        removed.tag = "Ordered";
        second.tag = "Ordered";
        third.tag = "Ordered";

        Assert.True(scene.DestroyObject(removed));
        GameObject fourth = scene.CreateObject("Fourth");
        fourth.tag = "Ordered";

        Assert.Equal([second, third, fourth], scene.GetObjects());
        Assert.Same(second, scene.FindObjectWithTag("Ordered"));
        Assert.Equal([second, third, fourth], scene.FindObjectsWithTag("Ordered"));
    }

    [Fact]
    public void TagRejectsEmptyValuesAndTrimsSurroundingWhitespace()
    {
        var scene = new GameScene("Tags");
        GameObject gameObject = scene.CreateObject("Object");

        gameObject.tag = "  Player  ";

        Assert.Equal("Player", gameObject.tag);
        Assert.Throws<ArgumentException>(() => gameObject.tag = "   ");
        Assert.Throws<ArgumentException>(() => scene.FindObjectWithTag(string.Empty));
    }

    [Fact]
    public void GetObjects_ReturnsSceneOwnedObjects()
    {
        var scene = new GameScene("Test");
        GameObject a = scene.CreateObject("A");
        GameObject b = scene.CreateObject("B");

        Guid[] ids = [.. scene.GetObjects()
            .Select(static o => o.identity.persistentId)
            .OrderBy(static id => id)];

        Assert.Equal(new[] { a.identity.persistentId, b.identity.persistentId }.OrderBy(static id => id), ids);
    }

    [Fact]
    public void LoadedScenesCanBeReorderedWithoutChangingTheActiveScene()
    {
        var first = new GameScene("First");
        var second = new GameScene("Second");
        var third = new GameScene("Third");
        SceneManager.LoadSceneAdditive(first);
        SceneManager.LoadSceneAdditive(second);
        SceneManager.LoadSceneAdditive(third);

        SceneManager.SetSceneIndex(first, 2);

        Assert.Equal([second, third, first], SceneManager.loadedScenes);
        Assert.Equal(2, SceneManager.GetSceneIndex(first));
        Assert.Same(third, SceneManager.activeScene);
    }

    [Fact]
    public void ComponentsCanBeReorderedWhileTransformRemainsFirst()
    {
        var scene = new GameScene("Components");
        GameObject gameObject = scene.CreateObject("Object");
        OrderComponentA first = gameObject.AddComponent<OrderComponentA>();
        OrderComponentB second = gameObject.AddComponent<OrderComponentB>();

        gameObject.SetComponentIndex(second, 1);

        Assert.Equal(
            [typeof(Transform), typeof(OrderComponentB), typeof(OrderComponentA)],
            gameObject.GetComponents().Select(static component => component.GetType()));
        Assert.Equal(1, gameObject.GetComponentIndex(second));
        Assert.Throws<InvalidOperationException>(() =>
            gameObject.SetComponentIndex(gameObject.transform, 1));
    }

    [Fact]
    public void DestroyObject_InvalidatesFacade()
    {
        var scene = new GameScene("Test");
        GameObject gameObject = scene.CreateObject("Cube");

        Assert.True(scene.DestroyObject(gameObject));

        Assert.False(gameObject.isRuntimeValid);
        Assert.Empty(scene.GetObjects());
        Assert.Throws<InvalidOperationException>(() => gameObject.GetComponent<Transform>());
    }

    [Fact]
    public void DestroyObject_UnlinksTransformHierarchy()
    {
        var scene = new GameScene("Test");
        GameObject parent = scene.CreateObject("Parent");
        GameObject child = scene.CreateObject("Child");
        child.GetComponent<Transform>().SetParent(parent.GetComponent<Transform>());
        Assert.True(scene.DestroyObject(parent));

        Assert.False(parent.isRuntimeValid);
        Assert.False(child.isRuntimeValid);
        Assert.Empty(scene.GetObjects());
    }

    [Fact]
    public void GameBehaviorLifecycle_DispatchesLifecycleCallbacks()
    {
        var scene = new GameScene("Test");
        GameObject gameObject = scene.CreateObject("Actor");
        TestBehaviour behaviour = gameObject.AddComponent<TestBehaviour>();
        SceneManager.LoadScene(scene);

        SceneManager.FixedUpdate(0.02f);
        SceneManager.Update(0.016f);
        SceneManager.LateUpdate(0.016f);

        Assert.Equal(1, behaviour.awakeCount);
        Assert.Equal(1, behaviour.startCount);
        Assert.Equal(1, behaviour.enableCount);
        Assert.Equal(1, behaviour.fixedUpdateCount);
        Assert.Equal(1, behaviour.updateCount);
        Assert.Equal(1, behaviour.lateUpdateCount);

        behaviour.enabled = false;
        Assert.Equal(1, behaviour.disableCount);
        SceneManager.Update(0.016f);

        Assert.Equal(1, behaviour.disableCount);
    }

    [Fact]
    public void GameBehaviorLifecycle_ReindexesAfterStructuralChanges()
    {
        var scene = new GameScene("Dynamic Behaviors");
        SceneManager.LoadScene(scene);
        SceneManager.Update(0.016f);

        GameObject gameObject = scene.CreateObject("Dynamic Actor");
        TestBehaviour behaviour = gameObject.AddComponent<TestBehaviour>();
        SceneManager.Update(0.016f);

        Assert.Equal(1, behaviour.awakeCount);
        Assert.Equal(1, behaviour.startCount);
        Assert.Equal(1, behaviour.enableCount);
        Assert.Equal(1, behaviour.updateCount);

        Assert.True(gameObject.RemoveComponent(behaviour));
        SceneManager.Update(0.016f);

        Assert.Equal(1, behaviour.updateCount);
        Assert.Equal(1, behaviour.disableCount);
    }

    [Fact]
    public void GameBehaviorLifecycle_StartsLateOnlyBehaviorWithoutDispatchingUpdate()
    {
        var scene = new GameScene("Phase-indexed Behaviors");
        GameObject gameObject = scene.CreateObject("Late Actor");
        LateOnlyBehaviour behaviour = gameObject.AddComponent<LateOnlyBehaviour>();
        SceneManager.LoadScene(scene);

        SceneManager.Update(0.016f);
        SceneManager.LateUpdate(0.016f);

        Assert.Equal(1, behaviour.startCount);
        Assert.Equal(1, behaviour.lateUpdateCount);
    }

    [Fact]
    public void ResetComponent_UsesVirtualDispatchAndExplicitBaseCall()
    {
        var scene = new GameScene("Test");
        GameObject gameObject = scene.CreateObject("Actor");

        DerivedResetComponent component = gameObject.AddComponent<DerivedResetComponent>();

        Assert.Equal(1, component.baseResetCount);
        Assert.Equal(1, component.derivedResetCount);

        gameObject.ResetComponent(component);

        Assert.Equal(2, component.baseResetCount);
        Assert.Equal(2, component.derivedResetCount);
    }

    [Fact]
    public void SceneManager_LoadScene_ReplacesActiveScene()
    {
        var first = new GameScene("First");
        var second = new GameScene("Second");

        SceneManager.LoadScene(first);
        SceneManager.LoadScene(second);

        Assert.False(first.isLoaded);
        Assert.True(second.isLoaded);
        Assert.Same(second, SceneManager.activeScene);
    }

    private sealed class TestBehaviour : GameBehavior
    {
        public int awakeCount;
        public int startCount;
        public int updateCount;
        public int fixedUpdateCount;
        public int lateUpdateCount;
        public int enableCount;
        public int disableCount;

        protected override void Awake() => awakeCount++;
        protected override void Start() => startCount++;
        protected override void Update() => updateCount++;
        protected override void FixedUpdate() => fixedUpdateCount++;
        protected override void LateUpdate() => lateUpdateCount++;
        protected override void OnEnable() => enableCount++;
        protected override void OnDisable() => disableCount++;

        protected override void Reset()
        {
            awakeCount = 0;
            startCount = 0;
            updateCount = 0;
            fixedUpdateCount = 0;
            lateUpdateCount = 0;
            enableCount = 0;
            disableCount = 0;
        }
    }

    private sealed class LateOnlyBehaviour : GameBehavior
    {
        public int startCount;
        public int lateUpdateCount;

        protected override void Start() => startCount++;

        protected override void LateUpdate() => lateUpdateCount++;
    }

    private class BaseResetComponent : GameComponent
    {
        public int baseResetCount;

        protected override void Reset()
        {
            baseResetCount++;
        }
    }

    private sealed class DerivedResetComponent : BaseResetComponent
    {
        public int derivedResetCount;

        protected override void Reset()
        {
            base.Reset();
            derivedResetCount++;
        }
    }
}

internal sealed class OrderComponentA : GameComponent;

internal sealed class OrderComponentB : GameComponent;
