using System;
using System.Collections.Generic;
using System.Linq;

using Inno.Core.ECS;
using Inno.Core.Reflection;

using Xunit;
using EcsSystem = Inno.Core.ECS.System;

namespace Inno.Core.ECS.Tests;

public sealed class WorldTests
{
    [Fact]
    public void AddAndRemoveComponent_FlushPending_UpdatesViews()
    {
        var world = new World();
        Entity entity = world.CreateEntity<TestEntity>();

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
        Entity outside = new World().CreateEntity<TestEntity>();

        Assert.Throws<InvalidOperationException>(() => world.AddComponent<TestPositionComponent>(outside));
    }

    [Fact]
    public void AddComponent_WithNullEntity_Throws()
    {
        var world = new World();
        Assert.Throws<ArgumentNullException>(() => world.AddComponent<TestPositionComponent>(null!));
    }

    [Fact]
    public void CreateEntity_Generic_CreatesRequestedEntityType()
    {
        var world = new World();

        TestEntity entity = world.CreateEntity<TestEntity>();

        Assert.IsType<TestEntity>(entity);
        Assert.True(Id(world, entity) > 0);
        Assert.Contains(entity, world.ViewEntities());
    }

    [Fact]
    public void ViewComponents_WithEntityFilter_ReturnsOnlyRequestedEntity()
    {
        var world = new World();
        Entity a = world.CreateEntity<TestEntity>();
        Entity b = world.CreateEntity<TestEntity>();

        world.AddComponent<TestPositionComponent>(a);
        world.AddComponent<TestPositionComponent>(b);
        world.FlushPending();

        IReadOnlyList<TestPositionComponent> aComponents = world.ViewComponents<TestPositionComponent>(Id(world, a));
        IReadOnlyList<TestPositionComponent> bComponents = world.ViewComponents<TestPositionComponent>(Id(world, b));

        Assert.Single(aComponents);
        Assert.Single(bComponents);
        Assert.NotSame(aComponents[0], bComponents[0]);
        Assert.NotEqual(aComponents[0].identity.runtimeId, bComponents[0].identity.runtimeId);
    }

    [Fact]
    public void ViewComponentsFast_MatchesSnapshotResults()
    {
        var world = new World();
        Entity a = world.CreateEntity<TestEntity>();
        Entity b = world.CreateEntity<TestEntity>();

        world.AddComponent<TestPositionComponent>(a);
        world.AddComponent<TestPositionComponent>(b);
        world.FlushPending();

        IReadOnlyList<int?> snapshot = [.. world.ViewComponents<TestPositionComponent>().Select(static c => c.identity.runtimeId).OrderBy(static id => id)];
        IReadOnlyList<int?> fast = [.. world.ViewComponentsFast<TestPositionComponent>().Select(static c => c.identity.runtimeId).OrderBy(static id => id)];

        Assert.Equal(snapshot, fast);
    }

    [Fact]
    public void ViewComponents_WithUnknownEntityFilter_ReturnsEmpty()
    {
        var world = new World();
        world.CreateEntity<TestEntity>();

        Assert.Empty(world.ViewComponents<TestPositionComponent>(-1));
    }

    [Fact]
    public void ViewComponents_Snapshot_IsStableAfterWorldMutation()
    {
        var world = new World();
        Entity a = world.CreateEntity<TestEntity>();
        Entity b = world.CreateEntity<TestEntity>();

        world.AddComponent<TestPositionComponent>(a);
        world.FlushPending();
        IReadOnlyList<TestPositionComponent> snapshot = world.ViewComponents<TestPositionComponent>();

        world.AddComponent<TestPositionComponent>(b);
        world.FlushPending();

        Assert.Single(snapshot);
        Assert.Same(snapshot[0], world.ViewComponents<TestPositionComponent>(Id(world, a))[0]);
    }

    [Fact]
    public void ViewEntities_ReturnsIntersectionOfAllTypes()
    {
        var world = new World();
        Entity onlyPos = world.CreateEntity<TestEntity>();
        Entity onlyVel = world.CreateEntity<TestEntity>();
        Entity both = world.CreateEntity<TestEntity>();

        world.AddComponent<TestPositionComponent>(onlyPos);
        world.AddComponent<TestVelocityComponent>(onlyVel);
        world.AddComponent<TestPositionComponent>(both);
        world.AddComponent<TestVelocityComponent>(both);
        world.FlushPending();

        EntityViewHandle handle = world.CreateEntityViewHandle([typeof(TestPositionComponent), typeof(TestVelocityComponent)]);
        IReadOnlyList<Entity> entities = world.ViewEntities(handle);

        Assert.Single(entities);
        Assert.Equal(Id(world, both), Id(world, entities[0]));
    }

    [Fact]
    public void ViewEntitiesFast_ReturnsIntersectionOfAllTypes()
    {
        var world = new World();
        Entity onlyPos = world.CreateEntity<TestEntity>();
        Entity bothA = world.CreateEntity<TestEntity>();
        Entity bothB = world.CreateEntity<TestEntity>();

        world.AddComponent<TestPositionComponent>(onlyPos);
        world.AddComponent<TestPositionComponent>(bothA);
        world.AddComponent<TestVelocityComponent>(bothA);
        world.AddComponent<TestPositionComponent>(bothB);
        world.AddComponent<TestVelocityComponent>(bothB);
        world.FlushPending();

        EntityViewHandle handle = world.CreateEntityViewHandle([typeof(TestPositionComponent), typeof(TestVelocityComponent)]);
        IReadOnlyList<int> fast = [.. world.ViewEntitiesFast(handle).Select(e => Id(world, e)).OrderBy(static id => id)];
        IReadOnlyList<int> expected = new[] { Id(world, bothA), Id(world, bothB) }.OrderBy(static id => id).ToArray();

        Assert.Equal(expected, fast);
    }

    [Fact]
    public void ViewEntities_WithNullHandle_ReturnsAllEntities()
    {
        var world = new World();
        Entity a = world.CreateEntity<TestEntity>();
        Entity b = world.CreateEntity<TestEntity>();
        Entity c = world.CreateEntity<TestEntity>();

        IReadOnlyList<int> all = [.. world.ViewEntities().Select(e => Id(world, e)).OrderBy(static id => id)];
        IReadOnlyList<int> expected = new[] { Id(world, a), Id(world, b), Id(world, c) }.OrderBy(static id => id).ToArray();

        Assert.Equal(expected, all);
    }

    [Fact]
    public void ViewEntitiesFast_WithNullHandle_ReturnsAllEntities()
    {
        var world = new World();
        Entity a = world.CreateEntity<TestEntity>();
        Entity b = world.CreateEntity<TestEntity>();
        Entity c = world.CreateEntity<TestEntity>();

        IReadOnlyList<int> all = [.. world.ViewEntitiesFast().Select(e => Id(world, e)).OrderBy(static id => id)];
        IReadOnlyList<int> expected = new[] { Id(world, a), Id(world, b), Id(world, c) }.OrderBy(static id => id).ToArray();

        Assert.Equal(expected, all);
    }

    [Fact]
    public void ViewEntities_Snapshot_IsStableAfterWorldMutation()
    {
        var world = new World();
        Entity both = world.CreateEntity<TestEntity>();
        Entity bothLater = world.CreateEntity<TestEntity>();
        world.AddComponent<TestPositionComponent>(both);
        world.AddComponent<TestVelocityComponent>(both);
        world.FlushPending();

        EntityViewHandle handle = world.CreateEntityViewHandle([typeof(TestPositionComponent), typeof(TestVelocityComponent)]);
        IReadOnlyList<Entity> snapshot = world.ViewEntities(handle);

        world.AddComponent<TestPositionComponent>(bothLater);
        world.AddComponent<TestVelocityComponent>(bothLater);
        world.FlushPending();

        Assert.Single(snapshot);
        Assert.Equal(Id(world, both), Id(world, snapshot[0]));
    }

    [Fact]
    public void ViewEntities_WithDuplicateTypes_DeduplicatesInput()
    {
        var world = new World();
        Entity both = world.CreateEntity<TestEntity>();

        world.AddComponent<TestPositionComponent>(both);
        world.AddComponent<TestVelocityComponent>(both);
        world.FlushPending();

        EntityViewHandle handle = world.CreateEntityViewHandle(
            [typeof(TestPositionComponent), typeof(TestPositionComponent), typeof(TestVelocityComponent)]);
        IReadOnlyList<Entity> entities = world.ViewEntities(handle);

        Assert.Single(entities);
        Assert.Equal(Id(world, both), Id(world, entities[0]));
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
        Entity a = world.CreateEntity<TestEntity>();
        Entity b = world.CreateEntity<TestEntity>();
        Entity c = world.CreateEntity<TestEntity>();

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
        Entity bothA = world.CreateEntity<TestEntity>();
        Entity bothB = world.CreateEntity<TestEntity>();
        Entity onlyPos = world.CreateEntity<TestEntity>();

        world.AddComponent<TestPositionComponent>(bothA);
        world.AddComponent<TestVelocityComponent>(bothA);
        world.AddComponent<TestPositionComponent>(bothB);
        world.AddComponent<TestVelocityComponent>(bothB);
        world.AddComponent<TestPositionComponent>(onlyPos);
        world.FlushPending();

        EntityViewHandle handle = world.CreateEntityViewHandle([typeof(TestPositionComponent), typeof(TestVelocityComponent)]);
        Assert.True(handle.isValid);

        IReadOnlyList<int> snapshot = [.. world.ViewEntities(handle).Select(e => Id(world, e)).OrderBy(static id => id)];
        IReadOnlyList<int> fast = [.. world.ViewEntitiesFast(handle).Select(e => Id(world, e)).OrderBy(static id => id)];
        IReadOnlyList<int> expected = new[] { Id(world, bothA), Id(world, bothB) }.OrderBy(static id => id).ToArray();

        Assert.Equal(expected, snapshot);
        Assert.Equal(expected, fast);
    }

    [Fact]
    public void ViewEntities_RemainsStable_WhenUnrelatedComponentTypeChanges()
    {
        var world = new World();
        Entity both = world.CreateEntity<TestEntity>();
        Entity onlyPos = world.CreateEntity<TestEntity>();
        Entity unrelated = world.CreateEntity<TestEntity>();

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
        Assert.Equal(Id(world, both), Id(world, second[0]));
    }

    [Fact]
    public void ViewEntities_ReflectsChanges_WhenRelatedComponentTypeChanges()
    {
        var world = new World();
        Entity both = world.CreateEntity<TestEntity>();
        Entity later = world.CreateEntity<TestEntity>();

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

            TypeCacheManager.Rebuild(typeof(string).Assembly.GetName().Name);

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
        Entity entity = world.CreateEntity<TestEntity>();
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
        Entity outside = new World().CreateEntity<TestEntity>();

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
        Entity entity = world.CreateEntity<TestEntity>();

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
        Entity entity = world.CreateEntity<TestEntity>();

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
        Entity entity = world.CreateEntity<TestEntity>();

        world.AddComponent<TestPositionComponent>(entity);
        world.AddComponent<TestPositionComponent>(entity);
        world.FlushPending();

        Assert.Single(world.ViewComponents<TestPositionComponent>(Id(world, entity)));
        Assert.Equal(1, TestPositionComponent.resetCount);
    }

    [Fact]
    public void KillEntity_RemovesPendingAdds_AndCallsReset()
    {
        TestPositionComponent.resetCount = 0;
        var world = new World();
        Entity entity = world.CreateEntity<TestEntity>();

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
        world.Process(0.5f);

        Assert.Equal(["early", "late"], trace);
    }

    [Fact]
    public void RegisterSystem_SameOrder_UsesTypeNameTieBreak()
    {
        var world = new World();
        var trace = new List<string>();

        world.RegisterSystem(new ZSameOrderSystem(trace));
        world.RegisterSystem(new ASameOrderSystem(trace));
        world.Process(0.1f);

        Assert.Equal(["A", "Z"], trace);
    }

    [Fact]
    public void System_ProcessStages_AreDispatchedSeparately()
    {
        var world = new World();
        var system = new StageTraceSystem();

        world.RegisterSystem(system);
        world.FixedProcess(0.02f);
        world.Process(0.016f);
        world.LateProcess(0.016f);

        Assert.Equal(1, system.fixedProcessCount);
        Assert.Equal(1, system.processCount);
        Assert.Equal(1, system.lateProcessCount);
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
        Entity a = world.CreateEntity<TestEntity>();
        Entity b = world.CreateEntity<TestEntity>();
        Entity c = world.CreateEntity<TestEntity>();

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
    public void System_Process_IsCalledByWorldProcess()
    {
        var world = new World();
        var system = new GenericForwardingSystem();

        world.RegisterSystem(system);
        world.Process(0.25f);

        Assert.Equal(1, system.processCount);
        Assert.Equal(0.25f, system.lastDeltaTime);
    }

    [Fact]
    public void Process_FlushesPendingBeforeAndAfterSystems()
    {
        var world = new World();
        Entity entity = world.CreateEntity<TestEntity>();
        world.AddComponent<TestPositionComponent>(entity);

        var system = new FlushProbeSystem(entity);
        world.RegisterSystem(system);
        world.Process(0.016f);

        Assert.True(system.seenAtSystemStart);
        Assert.Single(world.ViewComponents<TestVelocityComponent>(Id(world, entity)));
    }

    private sealed class TestPositionComponent : Component
    {
        public override void Reset()
        {
            resetCount++;
        }

        public static int resetCount;
    }

    private sealed class TestVelocityComponent : Component
    {
        public override void Reset()
        {
        }
    }

    private sealed class TestTagComponent : Component
    {
        public override void Reset()
        {
        }
    }

    private static int Id(World world, Entity entity)
        => entity.identity.runtimeId ?? throw new InvalidOperationException("Entity is not registered.");

    private sealed class TestEntity : Entity;

    private sealed class LowOrderSystem(List<string> sink) : EcsSystem
    {
        public override int order => 1;

        public override void Process(World world, float deltaTime)
        {
            sink.Add("early");
        }
    }

    private sealed class HighOrderSystem(List<string> sink) : EcsSystem
    {
        public override int order => 10;

        public override void Process(World world, float deltaTime)
        {
            sink.Add("late");
        }
    }

    private sealed class GenericForwardingSystem : EcsSystem
    {
        public int processCount;
        public float lastDeltaTime;

        public override void Process(World world, float deltaTime)
        {
            processCount++;
            lastDeltaTime = deltaTime;
        }
    }

    private sealed class FlushProbeSystem(Entity entity) : EcsSystem
    {
        public bool seenAtSystemStart;
        public override int order => 0;

        public override void Process(World world, float deltaTime)
        {
            seenAtSystemStart = world.ViewComponents<TestPositionComponent>(Id(world, entity)).Count == 1;
            world.AddComponent<TestVelocityComponent>(entity);
        }
    }

    private sealed class StageTraceSystem : EcsSystem
    {
        public int fixedProcessCount;
        public int processCount;
        public int lateProcessCount;

        public override void FixedProcess(World world, float fixedDeltaTime) => fixedProcessCount++;
        public override void Process(World world, float deltaTime) => processCount++;
        public override void LateProcess(World world, float deltaTime) => lateProcessCount++;
    }

    private sealed class ASameOrderSystem(List<string> sink) : EcsSystem
    {
        public override int order => 5;
        public override void Process(World world, float deltaTime) => sink.Add("A");
    }

    private sealed class ZSameOrderSystem(List<string> sink) : EcsSystem
    {
        public override int order => 5;
        public override void Process(World world, float deltaTime) => sink.Add("Z");
    }
}
