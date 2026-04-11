using System;

using Inno.Core.Identity;

using Xunit;

namespace Inno.Core.Identity.Tests;

public sealed class IdentityRegistryTests
{
    private sealed class Entity : IIdentityObject
    {
        public Identity identity { get; set; }
    }

    [Fact]
    public void Register_AssignsPersistentIdAndRuntimeId()
    {
        var registry = new IdentityRegistry();
        var entity = new Entity();

        Assert.True(registry.Register(entity));
        Assert.NotEqual(Guid.Empty, entity.identity.persistentId);
        Assert.True(entity.identity.runtimeId.HasValue);
        Assert.NotEqual(0, entity.identity.runtimeId.Value);
    }

    [Fact]
    public void RuntimeId_IsNull_WhenNeverRegistered()
    {
        var entity = new Entity();
        Assert.Null(entity.identity.runtimeId);
    }

    [Fact]
    public void Unregister_MakesIdentityRuntimeInvalid()
    {
        var registry = new IdentityRegistry();
        var entity = new Entity();
        Assert.True(registry.Register(entity));

        Assert.True(registry.Unregister(entity));
        Assert.Null(entity.identity.runtimeId);
    }

    [Fact]
    public void TryGet_ByRuntimeAndPersistent_ReturnsSameObject()
    {
        var registry = new IdentityRegistry();
        var entity = new Entity();
        Assert.True(registry.Register(entity));
        Assert.True(entity.identity.runtimeId.HasValue);
        int runtimeId = entity.identity.runtimeId.Value;

        Assert.True(registry.TryGet(runtimeId, out IIdentityObject? fromRuntime));
        Assert.True(registry.TryGet(entity.identity.persistentId, out IIdentityObject? fromPersistent));

        Assert.Same(entity, fromRuntime);
        Assert.Same(entity, fromPersistent);
    }

    [Fact]
    public void SlotReuse_InvalidatesOldRuntimeIdByGeneration()
    {
        var registry = new IdentityRegistry();

        var first = new Entity();
        Assert.True(registry.Register(first));
        Assert.True(first.identity.runtimeId.HasValue);
        int oldRuntimeId = first.identity.runtimeId.Value;

        Assert.True(registry.Unregister(first));

        var second = new Entity();
        Assert.True(registry.Register(second));
        Assert.True(second.identity.runtimeId.HasValue);
        int newRuntimeId = second.identity.runtimeId.Value;

        Assert.Equal(UnpackSlot(oldRuntimeId), UnpackSlot(newRuntimeId));
        Assert.NotEqual(UnpackGeneration(oldRuntimeId), UnpackGeneration(newRuntimeId));
        Assert.False(registry.TryGet(oldRuntimeId, out _));
        Assert.True(registry.TryGet(newRuntimeId, out IIdentityObject? resolved));
        Assert.Same(second, resolved);
    }

    [Fact]
    public void RuntimeId_BitPackingRoundtrip_Works()
    {
        int runtimeId = PackRuntimeId(slot: 12345, generation: 321);
        Assert.Equal(12345, UnpackSlot(runtimeId));
        Assert.Equal(321, UnpackGeneration(runtimeId));
    }

    [Fact]
    public void Register_SameObjectTwice_ReturnsFalse()
    {
        var registry = new IdentityRegistry();
        var entity = new Entity();

        Assert.True(registry.Register(entity));
        int firstRuntimeId = entity.identity.runtimeId!.Value;

        Assert.False(registry.Register(entity));
        Assert.Equal(firstRuntimeId, entity.identity.runtimeId!.Value);
        Assert.Equal(1, registry.count);
    }

    [Fact]
    public void Register_PreservesPreassignedPersistentId()
    {
        var registry = new IdentityRegistry();
        Guid assigned = Guid.NewGuid();
        var entity = new Entity
        {
            identity = new Identity(assigned)
        };

        Assert.True(registry.Register(entity));
        Assert.Equal(assigned, entity.identity.persistentId);
        Assert.True(registry.TryGet(assigned, out IIdentityObject? resolved));
        Assert.Same(entity, resolved);
    }

    [Fact]
    public void Register_DuplicatePersistentId_Throws()
    {
        var registry = new IdentityRegistry();
        Guid shared = Guid.NewGuid();
        var a = new Entity { identity = new Identity(shared) };
        var b = new Entity { identity = new Identity(shared) };

        Assert.True(registry.Register(a));
        Assert.Throws<InvalidOperationException>(() => registry.Register(b));
    }

    [Fact]
    public void TryGet_ReturnsFalse_ForInvalidRuntimeId()
    {
        var registry = new IdentityRegistry();
        var entity = new Entity();
        Assert.True(registry.Register(entity));

        Assert.False(registry.TryGet(int.MaxValue, out _));
    }

    [Fact]
    public void TryGet_ReturnsFalse_ForEmptyPersistentId()
    {
        var registry = new IdentityRegistry();
        Assert.False(registry.TryGet(Guid.Empty, out _));
    }

    [Fact]
    public void RuntimeId_BecomesNull_WhenUnregisteredAndReRegisterGetsNewGeneration()
    {
        var registry = new IdentityRegistry();
        var entity = new Entity();
        Assert.True(registry.Register(entity));

        int firstRuntimeId = entity.identity.runtimeId!.Value;
        Assert.True(registry.Unregister(entity));
        Assert.Null(entity.identity.runtimeId);

        Assert.True(registry.Register(entity));
        int secondRuntimeId = entity.identity.runtimeId!.Value;
        Assert.NotEqual(firstRuntimeId, secondRuntimeId);
    }

    private static int PackRuntimeId(int slot, int generation)
        => (generation << 20) | (slot & 0x000F_FFFF);

    private static int UnpackSlot(int runtimeId)
        => runtimeId & 0x000F_FFFF;

    private static int UnpackGeneration(int runtimeId)
        => (runtimeId >> 20) & 0x0000_0FFF;
}
