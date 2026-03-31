using System;
using System.Collections.Generic;
using System.Linq;

using Inno.Core.ECS;

using Xunit;

namespace Inno.Core.ECS.Tests;

public sealed class WorldTests
{
    [Fact]
    public void CreateEntity_WithParentGuid_PreservesParent()
    {
        var world = new World();
        Guid parent = Guid.NewGuid();

        Entity entity = world.CreateEntity(parent);

        Assert.Equal(parent, entity.parentGuid);
    }

    [Fact]
    public void AddAndRemoveComponent_FlushPending_UpdatesViews()
    {
        var world = new World();
        Entity entity = world.CreateEntity();

        world.AddComponent<TestPositionComponent>(entity);
        Assert.Empty(world.ViewComponents<TestPositionComponent>());

        world.FlushPending();
        Assert.Single(world.ViewComponents<TestPositionComponent>());

        Assert.True(world.RemoveComponent<TestPositionComponent>(entity));
        world.FlushPending();
        Assert.Empty(world.ViewComponents<TestPositionComponent>());
    }

    [Fact]
    public void AddComponent_ForUnknownEntity_Throws()
    {
        var world = new World();
        var outside = new Entity(Guid.NewGuid());

        Assert.Throws<InvalidOperationException>(() => world.AddComponent<TestPositionComponent>(outside));
    }

    [Fact]
    public void AddComponent_WithNullEntity_Throws()
    {
        var world = new World();
        Assert.Throws<ArgumentNullException>(() => world.AddComponent<TestPositionComponent>(null!));
    }

    [Fact]
    public void ViewComponents_WithEntityFilter_ReturnsOnlyRequestedEntity()
    {
        var world = new World();
        Entity a = world.CreateEntity();
        Entity b = world.CreateEntity();

        world.AddComponent<TestPositionComponent>(a);
        world.AddComponent<TestPositionComponent>(b);
        world.FlushPending();

        IReadOnlyList<TestPositionComponent> aComponents = world.ViewComponents<TestPositionComponent>(a.id);
        IReadOnlyList<TestPositionComponent> bComponents = world.ViewComponents<TestPositionComponent>(b.id);

        Assert.Single(aComponents);
        Assert.Single(bComponents);
        Assert.NotEqual(aComponents[0].entityId, bComponents[0].entityId);
        Assert.Equal(a.id, aComponents[0].entityId);
        Assert.Equal(b.id, bComponents[0].entityId);
    }

    [Fact]
    public void ViewComponentsFast_MatchesSnapshotResults()
    {
        var world = new World();
        Entity a = world.CreateEntity();
        Entity b = world.CreateEntity();

        world.AddComponent<TestPositionComponent>(a);
        world.AddComponent<TestPositionComponent>(b);
        world.FlushPending();

        IReadOnlyList<Guid> snapshot = [.. world.ViewComponents<TestPositionComponent>().Select(static c => c.entityId).OrderBy(static id => id)];
        IReadOnlyList<Guid> fast = [.. world.ViewComponentsFast<TestPositionComponent>().Select(static c => c.entityId).OrderBy(static id => id)];

        Assert.Equal(snapshot, fast);
    }

    [Fact]
    public void ViewComponents_WithUnknownEntityFilter_ReturnsEmpty()
    {
        var world = new World();
        world.CreateEntity();

        Assert.Empty(world.ViewComponents<TestPositionComponent>(Guid.NewGuid()));
    }

    [Fact]
    public void ViewComponents_Snapshot_IsStableAfterWorldMutation()
    {
        var world = new World();
        Entity a = world.CreateEntity();
        Entity b = world.CreateEntity();

        world.AddComponent<TestPositionComponent>(a);
        world.FlushPending();
        IReadOnlyList<TestPositionComponent> snapshot = world.ViewComponents<TestPositionComponent>();

        world.AddComponent<TestPositionComponent>(b);
        world.FlushPending();

        Assert.Single(snapshot);
        Assert.Equal(a.id, snapshot[0].entityId);
    }

    [Fact]
    public void ViewEntities_ReturnsIntersectionOfAllTypes()
    {
        var world = new World();
        Entity onlyPos = world.CreateEntity();
        Entity onlyVel = world.CreateEntity();
        Entity both = world.CreateEntity();

        world.AddComponent<TestPositionComponent>(onlyPos);
        world.AddComponent<TestVelocityComponent>(onlyVel);
        world.AddComponent<TestPositionComponent>(both);
        world.AddComponent<TestVelocityComponent>(both);
        world.FlushPending();

        IReadOnlyList<Entity> entities = world.ViewEntities([typeof(TestPositionComponent), typeof(TestVelocityComponent)]);

        Assert.Single(entities);
        Assert.Equal(both.id, entities[0].id);
    }

    [Fact]
    public void ViewEntitiesFast_ReturnsIntersectionOfAllTypes()
    {
        var world = new World();
        Entity onlyPos = world.CreateEntity();
        Entity bothA = world.CreateEntity();
        Entity bothB = world.CreateEntity();

        world.AddComponent<TestPositionComponent>(onlyPos);
        world.AddComponent<TestPositionComponent>(bothA);
        world.AddComponent<TestVelocityComponent>(bothA);
        world.AddComponent<TestPositionComponent>(bothB);
        world.AddComponent<TestVelocityComponent>(bothB);
        world.FlushPending();

        IReadOnlyList<Guid> fast = [.. world.ViewEntitiesFast([typeof(TestPositionComponent), typeof(TestVelocityComponent)]).Select(static e => e.id).OrderBy(static id => id)];
        IReadOnlyList<Guid> expected = new[] { bothA.id, bothB.id }.OrderBy(static id => id).ToArray();

        Assert.Equal(expected, fast);
    }

    [Fact]
    public void ViewEntities_Snapshot_IsStableAfterWorldMutation()
    {
        var world = new World();
        Entity both = world.CreateEntity();
        Entity bothLater = world.CreateEntity();
        world.AddComponent<TestPositionComponent>(both);
        world.AddComponent<TestVelocityComponent>(both);
        world.FlushPending();

        IReadOnlyList<Entity> snapshot = world.ViewEntities([typeof(TestPositionComponent), typeof(TestVelocityComponent)]);

        world.AddComponent<TestPositionComponent>(bothLater);
        world.AddComponent<TestVelocityComponent>(bothLater);
        world.FlushPending();

        Assert.Single(snapshot);
        Assert.Equal(both.id, snapshot[0].id);
    }

    [Fact]
    public void ViewEntities_WithDuplicateTypes_DeduplicatesInput()
    {
        var world = new World();
        Entity both = world.CreateEntity();

        world.AddComponent<TestPositionComponent>(both);
        world.AddComponent<TestVelocityComponent>(both);
        world.FlushPending();

        IReadOnlyList<Entity> entities = world.ViewEntities(
            [typeof(TestPositionComponent), typeof(TestPositionComponent), typeof(TestVelocityComponent)]);

        Assert.Single(entities);
        Assert.Equal(both.id, entities[0].id);
    }

    [Fact]
    public void ViewEntities_WithEmptyTypes_ThrowsArgumentException()
    {
        var world = new World();

        Assert.Throws<ArgumentException>(() => world.ViewEntities(Array.Empty<Type>()));
        Assert.Throws<ArgumentException>(() => world.ViewEntitiesFast(Array.Empty<Type>()).ToArray());
    }

    [Fact]
    public void ViewEntities_WithNonComponentType_ThrowsArgumentException()
    {
        var world = new World();
        Type[] types = [typeof(TestPositionComponent), typeof(string)];

        Assert.Throws<ArgumentException>(() => world.ViewEntities(types));
        Assert.Throws<ArgumentException>(() => world.ViewEntitiesFast(types).ToArray());
    }

    [Fact]
    public void ViewEntities_WithNullTypeEntry_ThrowsArgumentNullException()
    {
        var world = new World();
        Type[] types = [typeof(TestPositionComponent), null!];

        Assert.Throws<ArgumentNullException>(() => world.ViewEntities(types));
        Assert.Throws<ArgumentNullException>(() => world.ViewEntitiesFast(types).ToArray());
    }

    [Fact]
    public void ViewEntitiesFast_AreFailFast_WhenWorldMutatesDuringEnumeration()
    {
        var world = new World();
        Entity a = world.CreateEntity();
        Entity b = world.CreateEntity();
        Entity c = world.CreateEntity();

        world.AddComponent<TestPositionComponent>(a);
        world.AddComponent<TestPositionComponent>(b);
        world.FlushPending();

        using IEnumerator<Entity> iterator = world.ViewEntitiesFast([typeof(TestPositionComponent)]).GetEnumerator();
        Assert.True(iterator.MoveNext());

        world.AddComponent<TestPositionComponent>(c);
        world.FlushPending();

        Assert.Throws<InvalidOperationException>(() => iterator.MoveNext());
    }

    [Fact]
    public void KillEntity_IsDeferred_AndRemovesComponentsOnFlush()
    {
        var world = new World();
        Entity entity = world.CreateEntity();
        world.AddComponent<TestPositionComponent>(entity);
        world.FlushPending();

        Assert.True(world.KillEntity(entity));
        Assert.False(world.KillEntity(entity));
        Assert.Single(world.ViewComponents<TestPositionComponent>());

        world.FlushPending();
        Assert.Empty(world.ViewComponents<TestPositionComponent>());
        Assert.Throws<InvalidOperationException>(() => world.RemoveComponent<TestPositionComponent>(entity));
    }

    [Fact]
    public void KillEntity_WithUnknownEntity_ReturnsFalse()
    {
        var world = new World();
        var outside = new Entity(Guid.NewGuid());

        Assert.False(world.KillEntity(outside));
    }

    [Fact]
    public void KillEntity_WithNullEntity_Throws()
    {
        var world = new World();
        Assert.Throws<ArgumentNullException>(() => world.KillEntity(null!));
    }

    [Fact]
    public void RemoveComponent_WhenMissing_ReturnsFalse()
    {
        var world = new World();
        Entity entity = world.CreateEntity();

        Assert.False(world.RemoveComponent<TestPositionComponent>(entity));
    }

    [Fact]
    public void RemoveComponent_WithNullEntity_Throws()
    {
        var world = new World();
        Assert.Throws<ArgumentNullException>(() => world.RemoveComponent<TestPositionComponent>(null!));
    }

    [Fact]
    public void RemoveComponent_CancelsPendingAdd_AndComponentIsNotCreated()
    {
        TestPositionComponent.resetCount = 0;
        var world = new World();
        Entity entity = world.CreateEntity();

        world.AddComponent<TestPositionComponent>(entity);
        Assert.True(world.RemoveComponent<TestPositionComponent>(entity));

        world.FlushPending();
        Assert.Empty(world.ViewComponents<TestPositionComponent>());
        Assert.Equal(1, TestPositionComponent.resetCount);
    }

    [Fact]
    public void AddComponent_TwiceBeforeFlush_EndsWithSingleComponent()
    {
        TestPositionComponent.resetCount = 0;
        var world = new World();
        Entity entity = world.CreateEntity();

        world.AddComponent<TestPositionComponent>(entity);
        world.AddComponent<TestPositionComponent>(entity);
        world.FlushPending();

        Assert.Single(world.ViewComponents<TestPositionComponent>(entity.id));
        Assert.Equal(1, TestPositionComponent.resetCount);
    }

    [Fact]
    public void KillEntity_RemovesPendingAdds_AndCallsReset()
    {
        TestPositionComponent.resetCount = 0;
        var world = new World();
        Entity entity = world.CreateEntity();

        world.AddComponent<TestPositionComponent>(entity);
        Assert.True(world.KillEntity(entity));
        world.FlushPending();

        Assert.Empty(world.ViewComponents<TestPositionComponent>());
        Assert.Equal(1, TestPositionComponent.resetCount);
    }

    [Fact]
    public void RegisterSystem_RespectsOrderAndStableTypeNameTieBreak()
    {
        var world = new World();
        var trace = new List<string>();
        var late = new HighOrderSystem(trace);
        var early = new LowOrderSystem(trace);

        world.RegisterSystem(late);
        world.RegisterSystem(early);
        world.Update(0.5f);

        Assert.Equal(["early", "late"], trace);
    }

    [Fact]
    public void RegisterSystem_SameOrder_UsesTypeNameTieBreak()
    {
        var world = new World();
        var trace = new List<string>();

        world.RegisterSystem(new ZSameOrderSystem(trace));
        world.RegisterSystem(new ASameOrderSystem(trace));
        world.Update(0.1f);

        Assert.Equal(["A", "Z"], trace);
    }

    [Fact]
    public void RegisterSystem_WithNull_Throws()
    {
        var world = new World();
        Assert.Throws<ArgumentNullException>(() => world.RegisterSystem(null!));
    }

    [Fact]
    public void UnregisterSystem_RemovesByType()
    {
        var world = new World();
        world.RegisterSystem(new LowOrderSystem(new List<string>()));

        Assert.True(world.UnregisterSystem<LowOrderSystem>());
        Assert.False(world.UnregisterSystem<LowOrderSystem>());
    }

    [Fact]
    public void FastViews_AreFailFast_WhenWorldMutatesDuringEnumeration()
    {
        var world = new World();
        Entity a = world.CreateEntity();
        Entity b = world.CreateEntity();
        Entity c = world.CreateEntity();

        world.AddComponent<TestPositionComponent>(a);
        world.AddComponent<TestPositionComponent>(b);
        world.FlushPending();

        using IEnumerator<TestPositionComponent> iterator = world.ViewComponentsFast<TestPositionComponent>().GetEnumerator();
        Assert.True(iterator.MoveNext());

        world.AddComponent<TestPositionComponent>(c);
        world.FlushPending();

        Assert.Throws<InvalidOperationException>(() => iterator.MoveNext());
    }

    [Fact]
    public void GenericSystemBase_ForwardsUpdateToProcess()
    {
        var world = new World();
        var system = new GenericForwardingSystem();

        world.RegisterSystem(system);
        world.Update(0.25f);

        Assert.Equal(1, system.updateCount);
        Assert.Equal(0.25f, system.lastDeltaTime);
    }

    [Fact]
    public void Update_FlushesPendingBeforeAndAfterSystems()
    {
        var world = new World();
        Entity entity = world.CreateEntity();
        world.AddComponent<TestPositionComponent>(entity);

        var system = new FlushProbeSystem(entity);
        world.RegisterSystem(system);
        world.Update(0.016f);

        Assert.True(system.seenAtSystemStart);
        Assert.Single(world.ViewComponents<TestVelocityComponent>(entity.id));
    }

    private sealed class TestPositionComponent : Component
    {
        public override void Reset()
        {
            resetCount++;
        }

        public static int resetCount;
    }

    private sealed class TestVelocityComponent : Component;

    private sealed class LowOrderSystem(List<string> sink) : ISystem
    {
        public int order => 1;

        public void Update(World world, float deltaTime)
        {
            sink.Add("early");
        }
    }

    private sealed class HighOrderSystem(List<string> sink) : ISystem
    {
        public int order => 10;

        public void Update(World world, float deltaTime)
        {
            sink.Add("late");
        }
    }

    private sealed class GenericForwardingSystem : System<TestPositionComponent>
    {
        public int updateCount;
        public float lastDeltaTime;

        protected override void Process(World world, float deltaTime)
        {
            updateCount++;
            lastDeltaTime = deltaTime;
        }
    }

    private sealed class FlushProbeSystem(Entity entity) : ISystem
    {
        public bool seenAtSystemStart;
        public int order => 0;

        public void Update(World world, float deltaTime)
        {
            seenAtSystemStart = world.ViewComponents<TestPositionComponent>(entity.id).Count == 1;
            world.AddComponent<TestVelocityComponent>(entity);
        }
    }

    private sealed class ASameOrderSystem(List<string> sink) : ISystem
    {
        public int order => 5;
        public void Update(World world, float deltaTime) => sink.Add("A");
    }

    private sealed class ZSameOrderSystem(List<string> sink) : ISystem
    {
        public int order => 5;
        public void Update(World world, float deltaTime) => sink.Add("Z");
    }
}
