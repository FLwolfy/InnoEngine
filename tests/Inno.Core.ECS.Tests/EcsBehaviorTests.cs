using System;
using System.Threading;

using Inno.Core.ECS;

using Xunit;

namespace Inno.Core.ECS.Tests;

public sealed class EcsBehaviorTests
{
    [Fact]
    public void AddGetRemoveComponent_IsTypeSafeAndStable()
    {
        var world = new EcsWorld();
        Entity entity = world.CreateEntity();

        world.AddComponent(entity, new Position(1, 2, 3));
        Assert.True(world.HasComponent<Position>(entity));
        Assert.True(world.TryGetComponent(entity, out Position value));
        Assert.Equal(2f, value.y);

        ref Position pos = ref world.GetComponent<Position>(entity);
        pos.x = 9f;
        Assert.Equal(9f, world.GetComponent<Position>(entity).x);

        Assert.True(world.RemoveComponent<Position>(entity));
        Assert.False(world.HasComponent<Position>(entity));
    }

    [Fact]
    public void QueryAndParallelQuery_WorkCorrectly()
    {
        var world = new EcsWorld();
        Span<Entity> entities = stackalloc Entity[1024];
        world.CreateEntities(entities);
        for (int i = 0; i < entities.Length; i++)
        {
            world.AddComponent(entities[i], new Position(i, i, i));
            world.AddComponent(entities[i], new Velocity(1, 0, 0));
        }

        world.Query<Position, Velocity>().ParallelForEach(static (_, ref Position p, ref Velocity v) =>
        {
            p.x += v.x;
        }, chunkSize: 64);

        int count = 0;
        world.Query<Position>().ForEach((_, ref Position p) =>
        {
            Assert.True(p.x >= 1f);
            count++;
        });
        Assert.Equal(1024, count);
    }

    [Fact]
    public void CommandBuffer_DefersAndPlaysBack()
    {
        var world = new EcsWorld();
        Entity entity = world.CreateEntity();
        var cmd = new EcsCommandBuffer();
        cmd.EnqueueAddComponent(entity, new Position(3, 4, 5));
        cmd.EnqueueAddComponent(entity, new Velocity(2, 0, 0));
        cmd.EnqueueRemoveComponent<Velocity>(entity);
        cmd.Playback(world);

        Assert.True(world.HasComponent<Position>(entity));
        Assert.False(world.HasComponent<Velocity>(entity));
    }

    [Fact]
    public void HierarchyCloneAndRelations_Work()
    {
        var world = new EcsWorld();
        Entity parent = world.CreateEntity();
        Entity child = world.CreateEntity();
        world.SetParent(child, parent);
        world.SetRelation(parent, child, new Link(7));
        world.AddComponent(parent, new Position(1, 1, 1));

        Entity clone = world.CloneEntity(parent, cloneChildren: true);
        Assert.True(world.HasComponent<Position>(clone));
        Assert.True(world.TryGetRelation(clone, child, out Link relation));
        Assert.Equal(7, relation.strength);

        int childCount = 0;
        foreach (Entity _ in world.GetChildren(clone))
        {
            childCount++;
        }

        Assert.Equal(1, childCount);
    }

    [Fact]
    public void Serialization_RoundTrip_Works()
    {
        var world = new EcsWorld();
        Entity a = world.CreateEntity();
        Entity b = world.CreateEntity();
        world.SetParent(b, a);
        world.AddComponent(a, new Position(8, 9, 10));
        world.SetRelation(a, b, new Link(99));

        string json = EcsJsonSerializer.Serialize(world);

        var restored = new EcsWorld();
        _ = restored.Query<Position>();
        _ = restored.QueryRelations<Link>();
        EcsJsonSerializer.DeserializeInto(restored, json);

        Entity ra = default;
        Entity rb = default;
        foreach (Entity entity in restored.Entities)
        {
            if (entity.id == a.id)
            {
                ra = entity;
            }

            if (entity.id == b.id)
            {
                rb = entity;
            }
        }

        Assert.True(ra.IsValid);
        Assert.True(rb.IsValid);
        Assert.True(restored.HasComponent<Position>(ra));
        Assert.True(restored.TryGetRelation(ra, rb, out Link link));
        Assert.Equal(99, link.strength);
    }

    [Fact]
    public void SystemsAndSimd_AreUsable()
    {
        var world = new EcsWorld();
        Entity entity = world.CreateEntity();
        world.AddComponent(entity, new Position(1, 0, 0));
        world.AddComponent(entity, new Velocity(2, 0, 0));

        var group = new EcsSystemGroup();
        group.Add(new MoveSystem());
        group.Update(world, 0.5f);
        Assert.Equal(2f, world.GetComponent<Position>(entity).x);

        float dot = EcsSimd.Dot([1f, 2f, 3f], [4f, 5f, 6f]);
        Assert.Equal(32f, dot);
    }

    [Fact]
    public void EntityAndComponentEvents_AreRaised()
    {
        var world = new EcsWorld();
        int created = 0;
        int destroyed = 0;
        int componentAdded = 0;
        world.EntityCreated += _ => Interlocked.Increment(ref created);
        world.EntityDestroyed += _ => Interlocked.Increment(ref destroyed);
        world.ComponentAdded += (_, _) => Interlocked.Increment(ref componentAdded);

        Entity entity = world.CreateEntity();
        world.AddComponent(entity, new Position(0, 0, 0));
        world.DestroyEntity(entity);

        Assert.Equal(1, created);
        Assert.Equal(1, destroyed);
        Assert.Equal(1, componentAdded);
    }

    private struct Position : IComponent
    {
        public readonly float y;
        public readonly float z;
        public float x;

        public Position(float x, float y, float z)
        {
            this.x = x;
            this.y = y;
            this.z = z;
        }
    }

    private readonly struct Velocity : IComponent
    {
        public readonly float y;
        public readonly float z;
        public readonly float x;

        public Velocity(float x, float y, float z)
        {
            this.x = x;
            this.y = y;
            this.z = z;
        }
    }

    private readonly struct Link : IRelation
    {
        public readonly int strength;

        public Link(int strength)
        {
            this.strength = strength;
        }
    }

    private sealed class MoveSystem : IEcsSystem
    {
        public void Update(EcsWorld world, float deltaTime)
        {
            world.Query<Position, Velocity>().ForEach((_, ref Position p, ref Velocity v) =>
            {
                p.x += v.x * deltaTime;
            });
        }
    }
}
