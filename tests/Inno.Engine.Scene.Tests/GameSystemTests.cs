using System;
using System.Collections.Generic;
using System.Linq;

using Inno.Core.Reflection;
using Inno.Core.Serialization;
using Inno.Engine.Scene;

using Xunit;

namespace Inno.Engine.Scene.Tests;

[Collection(SceneTestsCollection.NAME)]
public sealed class GameSystemTests : IDisposable
{
    public GameSystemTests(SceneTestsFixture _)
    {
    }

    public void Dispose()
    {
        SceneManager.UnloadAllScenes();
    }

    [Fact]
    public void AddResetUniquenessAndMultiplePolicyUsePublicSceneApi()
    {
        var scene = new GameScene("Systems");

        ResettableSystem system = scene.AddSystem<ResettableSystem>();

        Assert.Equal(1, system.resetCount);
        Assert.Throws<InvalidOperationException>(() => scene.AddSystem<ResettableSystem>());
        scene.ResetSystem(system);
        Assert.Equal(2, system.resetCount);
        Assert.Equal(system, Assert.Single(scene.GetSystems()));
        Assert.NotNull(scene.AddSystem<MultipleSystem>());
        Assert.NotNull(scene.AddSystem<MultipleSystem>());
    }

    [Fact]
    public void LifecycleMatchesBehaviorRulesAndEditModeRemovalDoesNotDestroy()
    {
        var editScene = new GameScene("Edit");
        LifecycleSystem editSystem = editScene.AddSystem<LifecycleSystem>();

        Assert.True(editScene.RemoveSystem(editSystem));
        Assert.Equal(0, editSystem.awakeCount);
        Assert.Equal(0, editSystem.destroyCount);

        var runtimeScene = new GameScene("Runtime");
        LifecycleSystem runtimeSystem = runtimeScene.AddSystem<LifecycleSystem>();
        SceneManager.LoadScene(runtimeScene);
        SceneManager.FixedUpdate(0.02f);
        SceneManager.Update(0.016f);
        SceneManager.LateUpdate(0.016f);

        Assert.Equal(1, runtimeSystem.awakeCount);
        Assert.Equal(1, runtimeSystem.startCount);
        Assert.Equal(1, runtimeSystem.enableCount);
        Assert.Equal(1, runtimeSystem.fixedCount);
        Assert.Equal(1, runtimeSystem.updateCount);
        Assert.Equal(1, runtimeSystem.lateCount);

        runtimeSystem.enabled = false;
        SceneManager.Update(0.016f);
        Assert.Equal(1, runtimeSystem.disableCount);
        Assert.True(runtimeScene.RemoveSystem(runtimeSystem));
        Assert.Equal(1, runtimeSystem.destroyCount);
    }

    [Fact]
    public void SceneRoundtripPreservesSystemsStateIdentityOrderAndGraphReferences()
    {
        var source = new GameScene("System Serialization");
        SystemReferenceComponent component = source.CreateObject("Object")
            .AddComponent<SystemReferenceComponent>();
        StateSystem system = source.AddSystem<StateSystem>();
        system.value = 42;
        system.component = component;
        component.system = system;
        Guid systemId = system.identity.persistentId;

        byte[] bytes = SerializationManager.Serialize(source);
        SceneManager.LoadScene(source);
        SceneManager.UnloadScene(source);
        GameScene restored = SerializationManager.Deserialize<GameScene>(bytes);

        StateSystem restoredSystem = Assert.IsType<StateSystem>(Assert.Single(restored.GetSystems()));
        SystemReferenceComponent restoredComponent = Assert.Single(restored.GetObjects())
            .GetComponent<SystemReferenceComponent>();
        Assert.Equal(systemId, restoredSystem.identity.persistentId);
        Assert.Equal(42, restoredSystem.value);
        Assert.Equal(0, restoredSystem.resetCount);
        Assert.Same(restoredComponent, restoredSystem.component);
        Assert.Same(restoredSystem, restoredComponent.system);
    }

    [Fact]
    public void ManualSystemOrderIsIndependentFromExecutionPriorityAndBreaksPriorityTies()
    {
        var scene = new GameScene("System Order");
        var execution = new List<string>();
        var highA = new OrderedSystem("High A", 10, execution);
        var low = new OrderedSystem("Low", -10, execution);
        var highB = new OrderedSystem("High B", 10, execution);
        scene.AddSystem(highA);
        scene.AddSystem(low);
        scene.AddSystem(highB);

        scene.SetSystemIndex(highB, 0);

        Assert.Equal([highB, highA, low], scene.GetSystems());
        Assert.Equal(0, scene.GetSystemIndex(highB));
        SceneManager.LoadScene(scene);
        SceneManager.Update(0.016f);
        Assert.Equal(["Low", "High B", "High A"], execution);
    }

    private sealed class ResettableSystem : GameSystem
    {
        internal int resetCount;

        protected override void Reset() => resetCount++;
    }

    [AllowMultipleSystem]
    private sealed class MultipleSystem : GameSystem;

    private sealed class LifecycleSystem : GameSystem
    {
        internal int awakeCount;
        internal int startCount;
        internal int enableCount;
        internal int disableCount;
        internal int destroyCount;
        internal int fixedCount;
        internal int updateCount;
        internal int lateCount;

        protected override void Awake() => awakeCount++;
        protected override void Start() => startCount++;
        protected override void OnEnable() => enableCount++;
        protected override void OnDisable() => disableCount++;
        protected override void OnDestroy() => destroyCount++;
        protected override void OnFixedUpdate() => fixedCount++;
        protected override void OnUpdate() => updateCount++;
        protected override void OnLateUpdate() => lateCount++;
    }

    [AllowMultipleSystem]
    private sealed class OrderedSystem(
        string label,
        int executionOrder,
        ICollection<string> execution) : GameSystem
    {
        public override int order => executionOrder;

        protected override void OnUpdate() => execution.Add(label);
    }
}

[StableTypeId("b469170b-a1f2-4176-b3ad-252d1cb53b90")]
internal sealed class StateSystem : GameSystem
{
    [SerializableProperty]
    public int value { get; set; }

    [SerializableProperty]
    public SystemReferenceComponent? component { get; set; }

    public int resetCount { get; private set; }

    protected override void Reset() => resetCount++;
}

[StableTypeId("75344398-ed18-46ae-808b-e0cb45d93850")]
internal sealed class SystemReferenceComponent : GameComponent
{
    [SerializableProperty]
    public StateSystem? system { get; set; }
}
