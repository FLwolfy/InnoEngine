using System;
using Inno.Core.Identity;
using Xunit;

namespace Inno.Core.Identity.Tests;

public sealed class IdentityAllocatorTests
{
    private readonly IdentityAllocator m_allocator = new();

    private static void AssertRuntimeIdNotNull(IdentityObject target)
        => Assert.NotNull(target.identity.runtimeId);

    private static void AssertRuntimeIdIsNull(IdentityObject target)
        => Assert.Null(target.identity.runtimeId);

    private static int UnpackSlot(int runtimeId)
        => runtimeId & 0x000F_FFFF;

    private static int UnpackGeneration(int runtimeId)
        => (runtimeId >> 20) & 0x0000_0FFF;

    private static Identity GetIdentity(IdentityObject target)
        => target.identity;

    private WeakReference<Entity> CreateRegisteredEntity(out int runtimeId)
    {
        Entity entity = new();
        m_allocator.Register(entity);
        runtimeId = GetIdentity(entity).runtimeId!.Value;
        return new WeakReference<Entity>(entity);
    }

    private sealed class Entity : IdentityObject
    {
    }

    private sealed class UnrelatedEntity : IdentityObject
    {
    }

    [Fact]
    public void GetIdentity_DoesNotAutoRegisterObject()
    {
        var entity = new Entity();

        Identity identity = GetIdentity(entity);
        Assert.NotEqual(Guid.Empty, identity.persistentId);
        Assert.Null(identity.runtimeId);
    }

    [Fact]
    public void InitializePersistentIdentity_AssignsDetachedIdentityWithoutUnregisterEvent()
    {
        var entity = new Entity();
        Guid persistentId = Guid.NewGuid();
        int unregisteredCount = 0;

        void OnUnregistered(IdentityObject _) => unregisteredCount++;
        m_allocator.ObjectUnregistered += OnUnregistered;
        try
        {
            m_allocator.InitializePersistentIdentity(entity, persistentId);

            Assert.Equal(persistentId, GetIdentity(entity).persistentId);
            Assert.Null(GetIdentity(entity).runtimeId);
            Assert.Equal(0, unregisteredCount);
        }
        finally
        {
            m_allocator.ObjectUnregistered -= OnUnregistered;
        }
    }

    [Fact]
    public void Register_AndGet_ByRuntimeId_AndPersistentId_AndExplicitPersistentOverride()
    {
        var entity = new Entity();
        Assert.True(m_allocator.Register(entity, Guid.Parse("11111111-1111-1111-1111-111111111111")));
        AssertRuntimeIdNotNull(entity);

        Identity identity = GetIdentity(entity);
        Assert.Equal(Guid.Parse("11111111-1111-1111-1111-111111111111"), identity.persistentId);
        int runtimeId = identity.runtimeId!.Value;
        Assert.Same(entity, m_allocator.Get<IdentityObject>(runtimeId));
        Assert.Same(entity, m_allocator.Get<IdentityObject>(identity.persistentId));
        Assert.Same(entity, m_allocator.Get<Entity>(runtimeId));
        Assert.Null(m_allocator.Get<UnrelatedEntity>(runtimeId));
    }

    [Fact]
    public void Register_ReturnsFalse_WhenAlreadyRegistered()
    {
        var entity = new Entity();
        Assert.True(m_allocator.Register(entity));
        int firstRuntimeId = GetIdentity(entity).runtimeId!.Value;

        Assert.False(m_allocator.Register(entity));
        Assert.Equal(firstRuntimeId, GetIdentity(entity).runtimeId);
    }

    [Fact]
    public void Unregister_RemovesRuntimeId_AndGetReturnsNull()
    {
        var entity = new Entity();
        m_allocator.Register(entity);
        int runtimeId = GetIdentity(entity).runtimeId!.Value;

        Assert.True(m_allocator.Unregister(entity));
        AssertRuntimeIdIsNull(entity);
        Assert.Null(m_allocator.Get<IdentityObject>(runtimeId));
        Assert.Null(m_allocator.Get<IdentityObject>(Guid.Empty));
    }

    [Fact]
    public void Unregister_EventRunsAfterRemoval_InvokesEveryHandler_AndAggregatesFailures()
    {
        var entity = new Entity();
        Assert.True(m_allocator.Register(entity));
        Identity identity = GetIdentity(entity);
        int successfulHandlers = 0;

        void FailingHandler(IdentityObject removed)
        {
            Assert.Same(entity, removed);
            Assert.Null(GetIdentity(removed).runtimeId);
            Assert.Null(m_allocator.Get<Entity>(identity.persistentId));
            throw new InvalidOperationException("expected handler failure");
        }

        void SuccessfulHandler(IdentityObject removed)
        {
            Assert.Same(entity, removed);
            successfulHandlers++;
        }

        m_allocator.ObjectUnregistered += FailingHandler;
        m_allocator.ObjectUnregistered += SuccessfulHandler;
        try
        {
            AggregateException exception = Assert.Throws<AggregateException>(
                () => m_allocator.Unregister(entity));

            Assert.Single(exception.InnerExceptions);
            Assert.Equal(1, successfulHandlers);
            Assert.Null(GetIdentity(entity).runtimeId);
        }
        finally
        {
            m_allocator.ObjectUnregistered -= FailingHandler;
            m_allocator.ObjectUnregistered -= SuccessfulHandler;
        }
    }

    [Fact]
    public void SlotReuse_ChangesGeneration_WhenReRegistered()
    {
        var first = new Entity();
        m_allocator.Register(first);
        int firstRuntimeId = GetIdentity(first).runtimeId!.Value;

        Assert.True(m_allocator.Unregister(first));
        Assert.Null(m_allocator.Get<Entity>(firstRuntimeId));

        var second = new Entity();
        m_allocator.Register(second);
        int secondRuntimeId = GetIdentity(second).runtimeId!.Value;

        Assert.Equal(UnpackSlot(firstRuntimeId), UnpackSlot(secondRuntimeId));
        Assert.NotEqual(UnpackGeneration(firstRuntimeId), UnpackGeneration(secondRuntimeId));
        Assert.Same(second, m_allocator.Get<IdentityObject>(secondRuntimeId));
    }

    [Fact]
    public void Register_RequiresUniquePersistentId()
    {
        Guid fixedId = Guid.NewGuid();
        var first = new Entity();
        Assert.True(m_allocator.Register(first, fixedId));
        var second = new Entity();

        Assert.Throws<InvalidOperationException>(() => m_allocator.Register(second, fixedId));
    }

    [Fact]
    public void Get_ReturnsNull_ForInvalidLookupKeys()
    {
        Assert.Null(m_allocator.Get<IdentityObject>(int.MaxValue));
        Assert.Null(m_allocator.Get<IdentityObject>(Guid.Empty));
    }

    [Fact]
    public void RegisteredIdentity_DoesNotPreventGarbageCollection()
    {

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
        Assert.Null(m_allocator.Get<Entity>(runtimeId));
    }
}
