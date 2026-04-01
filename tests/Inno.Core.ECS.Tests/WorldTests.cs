using System;
using System.Collections.Generic;
using System.Linq;

using Inno.Core.ECS;
using Inno.Core.Reflection;

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

        EntityViewHandle handle = world.CreateEntityViewHandle([typeof(TestPositionComponent), typeof(TestVelocityComponent)]);
        IReadOnlyList<Entity> entities = world.ViewEntities(handle);

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

        EntityViewHandle handle = world.CreateEntityViewHandle([typeof(TestPositionComponent), typeof(TestVelocityComponent)]);
        IReadOnlyList<Guid> fast = [.. world.ViewEntitiesFast(handle).Select(static e => e.id).OrderBy(static id => id)];
        IReadOnlyList<Guid> expected = new[] { bothA.id, bothB.id }.OrderBy(static id => id).ToArray();

        Assert.Equal(expected, fast);
    }

    [Fact]
    public void ViewEntities_WithNullHandle_ReturnsAllEntities()
    {
        var world = new World();
        Entity a = world.CreateEntity();
        Entity b = world.CreateEntity();
        Entity c = world.CreateEntity();

        IReadOnlyList<Guid> all = [.. world.ViewEntities().Select(static e => e.id).OrderBy(static id => id)];
        IReadOnlyList<Guid> expected = new[] { a.id, b.id, c.id }.OrderBy(static id => id).ToArray();

        Assert.Equal(expected, all);
    }

    [Fact]
    public void ViewEntitiesFast_WithNullHandle_ReturnsAllEntities()
    {
        var world = new World();
        Entity a = world.CreateEntity();
        Entity b = world.CreateEntity();
        Entity c = world.CreateEntity();

        IReadOnlyList<Guid> all = [.. world.ViewEntitiesFast().Select(static e => e.id).OrderBy(static id => id)];
        IReadOnlyList<Guid> expected = new[] { a.id, b.id, c.id }.OrderBy(static id => id).ToArray();

        Assert.Equal(expected, all);
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

        EntityViewHandle handle = world.CreateEntityViewHandle([typeof(TestPositionComponent), typeof(TestVelocityComponent)]);
        IReadOnlyList<Entity> snapshot = world.ViewEntities(handle);

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

        EntityViewHandle handle = world.CreateEntityViewHandle(
            [typeof(TestPositionComponent), typeof(TestPositionComponent), typeof(TestVelocityComponent)]);
        IReadOnlyList<Entity> entities = world.ViewEntities(handle);

        Assert.Single(entities);
        Assert.Equal(both.id, entities[0].id);
    }

    [Fact]
    public void ViewEntities_WithEmptyTypes_ThrowsArgumentException()
    {
        var world = new World();

        Assert.Throws<ArgumentException>(() => world.CreateEntityViewHandle(Array.Empty<Type>()));
    }

    [Fact]
    public void ViewEntities_WithNonComponentType_ThrowsArgumentException()
    {
        var world = new World();
        Type[] types = [typeof(TestPositionComponent), typeof(string)];

        Assert.Throws<ArgumentException>(() => world.CreateEntityViewHandle(types));
    }

    [Fact]
    public void ViewEntities_WithNullTypeEntry_ThrowsArgumentNullException()
    {
        var world = new World();
        Type[] types = [typeof(TestPositionComponent), null!];

        Assert.Throws<ArgumentNullException>(() => world.CreateEntityViewHandle(types));
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

        EntityViewHandle handle = world.CreateEntityViewHandle([typeof(TestPositionComponent)]);
        using IEnumerator<Entity> iterator = world.ViewEntitiesFast(handle).GetEnumerator();
        Assert.True(iterator.MoveNext());

        world.AddComponent<TestPositionComponent>(c);
        world.FlushPending();

        Assert.Throws<InvalidOperationException>(() => iterator.MoveNext());
    }

    [Fact]
    public void EntityViewHandle_CanBeReused_ByViewEntitiesApis()
    {
        var world = new World();
        Entity bothA = world.CreateEntity();
        Entity bothB = world.CreateEntity();
        Entity onlyPos = world.CreateEntity();

        world.AddComponent<TestPositionComponent>(bothA);
        world.AddComponent<TestVelocityComponent>(bothA);
        world.AddComponent<TestPositionComponent>(bothB);
        world.AddComponent<TestVelocityComponent>(bothB);
        world.AddComponent<TestPositionComponent>(onlyPos);
        world.FlushPending();

        EntityViewHandle handle = world.CreateEntityViewHandle([typeof(TestPositionComponent), typeof(TestVelocityComponent)]);
        Assert.True(handle.isValid);

        IReadOnlyList<Guid> snapshot = [.. world.ViewEntities(handle).Select(static e => e.id).OrderBy(static id => id)];
        IReadOnlyList<Guid> fast = [.. world.ViewEntitiesFast(handle).Select(static e => e.id).OrderBy(static id => id)];
        IReadOnlyList<Guid> expected = new[] { bothA.id, bothB.id }.OrderBy(static id => id).ToArray();

        Assert.Equal(expected, snapshot);
        Assert.Equal(expected, fast);
    }

    [Fact]
    public void ViewEntities_RemainsStable_WhenUnrelatedComponentTypeChanges()
    {
        var world = new World();
        Entity both = world.CreateEntity();
        Entity onlyPos = world.CreateEntity();
        Entity unrelated = world.CreateEntity();

        world.AddComponent<TestPositionComponent>(both);
        world.AddComponent<TestVelocityComponent>(both);
        world.AddComponent<TestPositionComponent>(onlyPos);
        world.FlushPending();

        EntityViewHandle handle = world.CreateEntityViewHandle([typeof(TestPositionComponent), typeof(TestVelocityComponent)]);
        IReadOnlyList<Entity> first = world.ViewEntities(handle);

        world.AddComponent<TestTagComponent>(unrelated);
        world.FlushPending();

        IReadOnlyList<Entity> second = world.ViewEntities(handle);

        Assert.Single(first);
        Assert.Single(second);
        Assert.Equal(both.id, second[0].id);
    }

    [Fact]
    public void ViewEntities_ReflectsChanges_WhenRelatedComponentTypeChanges()
    {
        var world = new World();
        Entity both = world.CreateEntity();
        Entity later = world.CreateEntity();

        world.AddComponent<TestPositionComponent>(both);
        world.AddComponent<TestVelocityComponent>(both);
        world.FlushPending();

        EntityViewHandle handle = world.CreateEntityViewHandle([typeof(TestPositionComponent), typeof(TestVelocityComponent)]);
        IReadOnlyList<Entity> first = world.ViewEntities(handle);

        world.AddComponent<TestPositionComponent>(later);
        world.AddComponent<TestVelocityComponent>(later);
        world.FlushPending();

        IReadOnlyList<Entity> second = world.ViewEntities(handle);

        Assert.Single(first);
        Assert.Equal(2, second.Count);
    }

    [Fact]
    public void EntityViewHandle_Default_IsInvalid()
    {
        EntityViewHandle handle = default;
        Assert.False(handle.isValid);
    }

    [Fact]
    public void ViewEntities_WithHandleFromDifferentWorld_Throws()
    {
        var worldA = new World();
        var worldB = new World();
        EntityViewHandle handle = worldA.CreateEntityViewHandle([typeof(TestPositionComponent)]);

        Assert.Throws<InvalidOperationException>(() => worldB.ViewEntities(handle));
        Assert.Throws<InvalidOperationException>(() => worldB.ViewEntitiesFast(handle).ToArray());
    }

    [Fact]
    public void EntityViewHandle_IsInvalid_AfterTypeCacheRebuildWithoutEcsTypes()
    {
        try
        {
            TypeCacheManager.Initialize();
            var world = new World();
            EntityViewHandle handle = world.CreateEntityViewHandle([typeof(TestPositionComponent)]);
            Assert.True(handle.isValid);

            TypeCacheManager.Rebuild(typeof(string).Assembly);

            Assert.False(handle.isValid);
            Assert.Throws<InvalidOperationException>(() => world.ViewEntities(handle));
            Assert.Throws<InvalidOperationException>(() => world.ViewEntitiesFast(handle).ToArray());
        }
        finally
        {
            TypeCacheManager.Rebuild();
        }
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

    private sealed class TestTagComponent : Component;

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
