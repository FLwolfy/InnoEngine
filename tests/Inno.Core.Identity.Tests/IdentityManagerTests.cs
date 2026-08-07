using System;
using System.Reflection;
using Inno.Core.Identity;
using Xunit;

namespace Inno.Core.Identity.Tests;

public sealed class IdentityManagerTests
{
    private static readonly MethodInfo S_SET_IDENTITY_METHOD =
        typeof(IIdentityObject).GetMethod(
            "SetIdentity",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
            null,
            new[] { typeof(Identity) },
            null)
        ?? throw new InvalidOperationException("IIdentityObject.SetIdentity(Identity) was not found.");

    private static void SetIdentity(IIdentityObject target, Identity identity)
        => S_SET_IDENTITY_METHOD.Invoke(target, new object[] { identity });

    private static void AssertRuntimeIdNotNull(IIdentityObject target)
        => Assert.NotNull(((IIdentityObject)target).GetIdentity().runtimeId);

    private static void AssertRuntimeIdIsNull(IIdentityObject target)
        => Assert.Null(((IIdentityObject)target).GetIdentity().runtimeId);

    private static int UnpackSlot(int runtimeId)
        => runtimeId & 0x000F_FFFF;

    private static int UnpackGeneration(int runtimeId)
        => (runtimeId >> 20) & 0x0000_0FFF;

    private static Identity GetIdentity(IIdentityObject target)
        => ((IIdentityObject)target).GetIdentity();

    private static WeakReference<Entity> CreateRegisteredEntity(out int runtimeId)
    {
        Entity entity = new();
        IdentityManager.Register(entity);
        runtimeId = GetIdentity(entity).runtimeId!.Value;
        return new WeakReference<Entity>(entity);
    }

    private sealed class Entity : IIdentityObject
    {
        public Entity()
        {
        }

        public Entity(Guid persistentId)
        {
            SetIdentity(this, new Identity(persistentId));
        }
    }

    private sealed class UnrelatedEntity : IIdentityObject
    {
    }

    [Fact]
    public void GetIdentity_DoesNotAutoRegisterObject()
    {
        IdentityManager.Initialize();
        var entity = new Entity();

        Identity identity = GetIdentity(entity);
        Assert.NotEqual(Guid.Empty, identity.persistentId);
        Assert.Null(identity.runtimeId);
    }

    [Fact]
    public void Register_AndGet_ByRuntimeId_AndPersistentId_AndExplicitPersistentOverride()
    {
        IdentityManager.Initialize();
        var entity = new Entity();
        Assert.True(IdentityManager.Register(entity, Guid.Parse("11111111-1111-1111-1111-111111111111")));
        AssertRuntimeIdNotNull(entity);

        Identity identity = GetIdentity(entity);
        Assert.Equal(Guid.Parse("11111111-1111-1111-1111-111111111111"), identity.persistentId);
        int runtimeId = identity.runtimeId!.Value;
        Assert.Same(entity, IdentityManager.Get<IIdentityObject>(runtimeId));
        Assert.Same(entity, IdentityManager.Get<IIdentityObject>(identity.persistentId));
        Assert.Same(entity, IdentityManager.Get<Entity>(runtimeId));
        Assert.Null(IdentityManager.Get<UnrelatedEntity>(runtimeId));
    }

    [Fact]
    public void Register_ReturnsFalse_WhenAlreadyRegistered()
    {
        IdentityManager.Initialize();
        var entity = new Entity();
        Assert.True(IdentityManager.Register(entity));
        int firstRuntimeId = GetIdentity(entity).runtimeId!.Value;

        Assert.False(IdentityManager.Register(entity));
        Assert.Equal(firstRuntimeId, GetIdentity(entity).runtimeId);
    }

    [Fact]
    public void Unregister_RemovesRuntimeId_AndGetReturnsNull()
    {
        IdentityManager.Initialize();
        var entity = new Entity();
        IdentityManager.Register(entity);
        int runtimeId = GetIdentity(entity).runtimeId!.Value;

        Assert.True(IdentityManager.Unregister(entity));
        AssertRuntimeIdIsNull(entity);
        Assert.Null(IdentityManager.Get<IIdentityObject>(runtimeId));
        Assert.Null(IdentityManager.Get<IIdentityObject>(Guid.Empty));
    }

    [Fact]
    public void SlotReuse_ChangesGeneration_WhenReRegistered()
    {
        IdentityManager.Initialize();
        var first = new Entity();
        IdentityManager.Register(first);
        int firstRuntimeId = GetIdentity(first).runtimeId!.Value;

        Assert.True(IdentityManager.Unregister(first));
        Assert.Null(IdentityManager.Get<Entity>(firstRuntimeId));

        var second = new Entity();
        IdentityManager.Register(second);
        int secondRuntimeId = GetIdentity(second).runtimeId!.Value;

        Assert.Equal(UnpackSlot(firstRuntimeId), UnpackSlot(secondRuntimeId));
        Assert.NotEqual(UnpackGeneration(firstRuntimeId), UnpackGeneration(secondRuntimeId));
        Assert.Same(second, IdentityManager.Get<IIdentityObject>(secondRuntimeId));
    }

    [Fact]
    public void Register_RequiresUniquePersistentId()
    {
        IdentityManager.Initialize();
        Guid fixedId = Guid.NewGuid();
        var first = new Entity();
        Assert.True(IdentityManager.Register(first, fixedId));
        var second = new Entity();

        Assert.Throws<InvalidOperationException>(() => IdentityManager.Register(second, fixedId));
    }

    [Fact]
    public void Get_ReturnsNull_ForInvalidLookupKeys()
    {
        IdentityManager.Initialize();
        Assert.Null(IdentityManager.Get<IIdentityObject>(int.MaxValue));
        Assert.Null(IdentityManager.Get<IIdentityObject>(Guid.Empty));
    }

    [Fact]
    public void RegisteredIdentity_DoesNotPreventGarbageCollection()
    {
        IdentityManager.Initialize();

        int runtimeId;
        WeakReference<Entity> weakEntity;

        weakEntity = CreateRegisteredEntity(out runtimeId);

        for (int i = 0; i < 8; i++)
        {
            GC.Collect(2, GCCollectionMode.Forced, true, true);
            GC.WaitForPendingFinalizers();
            GC.Collect(2, GCCollectionMode.Forced, true, true);
        }

        for (int i = 0; i < 8; i++)
        {
            GC.Collect(2, GCCollectionMode.Forced, true, true);
            GC.WaitForPendingFinalizers();
            GC.Collect(2, GCCollectionMode.Forced, true, true);
        }

        Assert.False(weakEntity.TryGetTarget(out _));
        Assert.Null(IdentityManager.Get<Entity>(runtimeId));
    }
}
