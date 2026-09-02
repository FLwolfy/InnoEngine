using System;
using System.Threading.Tasks;

using Inno.Assets;

using Xunit;

namespace Inno.Assets.Tests;

public sealed class AssetExecutionContextTests
{
    [Fact]
    public void Assets_WithoutAnExecutionScope_FailsExplicitly()
    {
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            Assets.Load<TextAsset>(AssetPath.Project("missing.txt")));

        Assert.Contains("No asset lookup is bound", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NestedScopes_RestoreThePreviousLookup()
    {
        var outerAsset = new TextAsset();
        var innerAsset = new TextAsset();
        var outer = new FixedAssetLookup(outerAsset);
        var inner = new FixedAssetLookup(innerAsset);

        using (AssetExecutionContext.EnterScope(outer))
        {
            Assert.Same(outerAsset, Assets.Load<TextAsset>(AssetPath.Project("value.txt")));
            using (AssetExecutionContext.EnterScope(inner))
                Assert.Same(innerAsset, Assets.Load<TextAsset>(AssetPath.Project("value.txt")));
            Assert.Same(outerAsset, Assets.Load<TextAsset>(AssetPath.Project("value.txt")));
        }
    }

    [Fact]
    public void Scopes_DisposedOutOfOrder_AreRejectedWithoutLosingTheActiveLookup()
    {
        var outer = new FixedAssetLookup(new TextAsset());
        var inner = new FixedAssetLookup(new TextAsset());
        IDisposable outerScope = AssetExecutionContext.EnterScope(outer);
        IDisposable innerScope = AssetExecutionContext.EnterScope(inner);

        try
        {
            Assert.Throws<InvalidOperationException>(outerScope.Dispose);
            Assert.Same(inner, AssetExecutionContext.current);
        }
        finally
        {
            innerScope.Dispose();
            outerScope.Dispose();
        }
    }

    [Fact]
    public async Task ParallelAsyncFlows_ResolveTheirOwnLookups()
    {
        var firstAsset = new TextAsset();
        var secondAsset = new TextAsset();

        Task<TextAsset> first = ResolveAsync(new FixedAssetLookup(firstAsset));
        Task<TextAsset> second = ResolveAsync(new FixedAssetLookup(secondAsset));

        Assert.Same(firstAsset, await first);
        Assert.Same(secondAsset, await second);
    }

    private static async Task<TextAsset> ResolveAsync(IAssetLookup lookup)
    {
        using IDisposable scope = AssetExecutionContext.EnterScope(lookup);
        await Task.Yield();
        return Assets.Load<TextAsset>(AssetPath.Project("value.txt"));
    }

    private sealed class FixedAssetLookup(AssetObject asset) : IAssetLookup
    {
        public TAsset Load<TAsset>(AssetPath path)
            where TAsset : AssetObject
            => Cast<TAsset>();

        public TAsset Load<TAsset>(Guid persistentId)
            where TAsset : AssetObject
            => Cast<TAsset>();

        public bool TryLoad<TAsset>(AssetPath path, out TAsset? loadedAsset)
            where TAsset : AssetObject
        {
            loadedAsset = Cast<TAsset>();
            return true;
        }

        public bool TryLoad<TAsset>(Guid persistentId, out TAsset? loadedAsset)
            where TAsset : AssetObject
        {
            loadedAsset = Cast<TAsset>();
            return true;
        }

        private TAsset Cast<TAsset>()
            where TAsset : AssetObject
            => Assert.IsType<TAsset>(asset);
    }
}
