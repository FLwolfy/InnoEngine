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
        Assert.NotEqual(0, entity.identity.runtimeId);
        Assert.True(entity.identity.TryGetRuntimeId(out int runtimeId));
        Assert.Equal(entity.identity.runtimeId, runtimeId);
    }

    [Fact]
    public void TryGetRuntimeId_ReturnsFalse_WhenNeverRegistered()
    {
        var entity = new Entity();
        Assert.False(entity.identity.TryGetRuntimeId(out _));
    }

    [Fact]
    public void Unregister_MakesIdentityRuntimeInvalid()
    {
        var registry = new IdentityRegistry();
        var entity = new Entity();
        Assert.True(registry.Register(entity));

        Assert.True(registry.Unregister(entity));
        Assert.False(entity.identity.TryGetRuntimeId(out _));
        Assert.Equal(0, entity.identity.runtimeId);
    }

    [Fact]
    public void TryGet_ByRuntimeAndPersistent_ReturnsSameObject()
    {
        var registry = new IdentityRegistry();
        var entity = new Entity();
        Assert.True(registry.Register(entity));

        Assert.True(registry.TryGet(entity.identity.runtimeId, out IIdentityObject? fromRuntime));
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
        int oldRuntimeId = first.identity.runtimeId;

        Assert.True(registry.Unregister(first));

        var second = new Entity();
        Assert.True(registry.Register(second));
        int newRuntimeId = second.identity.runtimeId;

        Assert.Equal(RuntimeIdCodec.UnpackSlot(oldRuntimeId), RuntimeIdCodec.UnpackSlot(newRuntimeId));
        Assert.NotEqual(RuntimeIdCodec.UnpackGeneration(oldRuntimeId), RuntimeIdCodec.UnpackGeneration(newRuntimeId));
        Assert.False(registry.TryGet(oldRuntimeId, out _));
        Assert.True(registry.TryGet(newRuntimeId, out IIdentityObject? resolved));
        Assert.Same(second, resolved);
    }

    [Fact]
    public void RuntimeIdCodec_PackRoundtrip_Works()
    {
        int runtimeId = RuntimeIdCodec.Pack(slot: 12345, generation: 321);
        Assert.Equal(12345, RuntimeIdCodec.UnpackSlot(runtimeId));
        Assert.Equal(321, RuntimeIdCodec.UnpackGeneration(runtimeId));
    }
}
