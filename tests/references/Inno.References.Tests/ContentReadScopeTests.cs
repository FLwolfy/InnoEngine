using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Inno.Core.Identity;
using Xunit;

namespace Inno.References.Tests;

public sealed class ContentReadScopeTests
{
    [Fact]
    public void ScopeResolvesOnlyCapturedGenerationAndRevokesOnDispose()
    {
        var owner = new IdentityAllocator();
        var root = new Root();
        owner.Register(root);
        Guid id = root.identity.persistentId;
        using var scope = new ContentReadScope([root.identity], id);
        Assert.Same(root, Assert.Single(scope.GetValues<Root>()));
        owner.Unregister(root);
        var replacement = new Root();
        owner.Register(replacement, id);
        Assert.False(scope.TryGetValue(id, out Root? _));
        Assert.Empty(scope.GetValues<Root>());
        scope.Dispose();
        Assert.Throws<ObjectDisposedException>(() => scope.GetValues<Root>());
    }

    [Fact]
    public void ScopeDoesNotOwnRootOrAllocatorAndFreezesIdentities()
    {
        (ContentReadScope scope, WeakReference root, WeakReference allocator) = CreateScope();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Assert.False(root.IsAlive);
        Assert.False(allocator.IsAlive);
        Assert.Empty(scope.GetValues<Root>());
        Assert.Throws<NotSupportedException>(() => ((IList<Identity>)scope.contents).Clear());
        scope.Dispose();
    }

    [Fact]
    public void DetachedDuplicateAndUnknownActiveRootsAreRejected()
    {
        var root = new Root();
        Assert.Throws<ArgumentException>(() => new ContentReadScope([root.identity]));
        var owner = new IdentityAllocator();
        owner.Register(root);
        Assert.Throws<ArgumentException>(() => new ContentReadScope([root.identity, root.identity]));
        Assert.Throws<ArgumentException>(() => new ContentReadScope([root.identity], Guid.NewGuid()));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (ContentReadScope, WeakReference, WeakReference) CreateScope()
    {
        var owner = new IdentityAllocator();
        var root = new Root();
        owner.Register(root);
        return (new ContentReadScope([root.identity]), new WeakReference(root), new WeakReference(owner));
    }

    private sealed class Root : IdentityObject;
}
