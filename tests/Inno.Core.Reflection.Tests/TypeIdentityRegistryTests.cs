using System;
using System.Collections.Generic;

using Inno.Core.Reflection;

using Xunit;

namespace Inno.Core.Reflection.Tests;

[Collection(TypeIdentityRegistryCollection.NAME)]
public sealed class TypeIdentityRegistryTests
{
    [Fact]
    public void Rebuild_IndexesStableAndRuntimeMappings()
    {
        TypeIdentityRegistry.Rebuild([
            typeof(TypeIdentityRegistryFixtures.StableTypeA),
            typeof(TypeIdentityRegistryFixtures.UnstableTypeA)
        ]);

        Assert.True(TypeIdentityRegistry.TryGetStableTypeId(typeof(TypeIdentityRegistryFixtures.StableTypeA), out Guid stableId));
        Assert.Equal(Guid.Parse("11111111-1111-1111-1111-111111111111"), stableId);
        Assert.Equal(stableId, TypeIdentityRegistry.GetStableTypeId(typeof(TypeIdentityRegistryFixtures.StableTypeA)));
        Assert.True(TypeIdentityRegistry.TryResolveType(stableId, out Type? resolvedStableType));
        Assert.Equal(typeof(TypeIdentityRegistryFixtures.StableTypeA), resolvedStableType);
        Assert.False(TypeIdentityRegistry.TryGetStableTypeId(typeof(TypeIdentityRegistryFixtures.UnstableTypeA), out _));
        Assert.False(TypeIdentityRegistry.TryResolveType(Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff"), out _));
        Assert.Equal(1, TypeIdentityRegistry.stableCount);

        Assert.True(TypeIdentityRegistry.TryGetRuntimeTypeId(typeof(TypeIdentityRegistryFixtures.StableTypeA), out int stableRuntimeId));
        Assert.True(TypeIdentityRegistry.TryGetRuntimeTypeId(typeof(TypeIdentityRegistryFixtures.UnstableTypeA), out int unstableRuntimeId));
        Assert.NotEqual(stableRuntimeId, unstableRuntimeId);
        Assert.True(TypeIdentityRegistry.TryResolveRuntimeType(stableRuntimeId, out Type? resolvedRuntimeType));
        Assert.Equal(typeof(TypeIdentityRegistryFixtures.StableTypeA), resolvedRuntimeType);
    }

    [Fact]
    public void Rebuild_DuplicateStableId_Throws()
    {
        Assert.Throws<InvalidOperationException>(() =>
            TypeIdentityRegistry.Rebuild([
                typeof(TypeIdentityRegistryNegativeFixtures.DuplicateStableTypeA),
                typeof(TypeIdentityRegistryNegativeFixtures.DuplicateStableTypeB)
            ]));
    }

    [Fact]
    public void Rebuild_InvalidStableId_Throws()
    {
        Assert.Throws<InvalidOperationException>(() =>
            TypeIdentityRegistry.Rebuild([typeof(TypeIdentityRegistryNegativeFixtures.InvalidStableType)]));
    }

    [Fact]
    public void GetStableTypeId_And_GetRuntimeTypeId_Throw_WhenMissing()
    {
        TypeIdentityRegistry.Rebuild([typeof(TypeIdentityRegistryFixtures.StableTypeA)]);

        Assert.Throws<KeyNotFoundException>(() => TypeIdentityRegistry.GetStableTypeId(typeof(TypeIdentityRegistryFixtures.UnstableTypeA)));
        Assert.Throws<KeyNotFoundException>(() => TypeIdentityRegistry.GetRuntimeTypeId(typeof(TypeIdentityRegistryFixtures.UnstableTypeA)));
    }

    [Fact]
    public void GetOrAddRuntimeTypeId_AddsAndResolvesType()
    {
        TypeIdentityRegistry.Rebuild([]);

        int runtimeTypeId = TypeIdentityRegistry.GetOrAddRuntimeTypeId(typeof(TypeIdentityRegistryFixtures.UnstableTypeA));
        int runtimeTypeId2 = TypeIdentityRegistry.GetOrAddRuntimeTypeId(typeof(TypeIdentityRegistryFixtures.UnstableTypeA));
        Assert.Equal(runtimeTypeId, runtimeTypeId2);

        Assert.True(TypeIdentityRegistry.TryResolveRuntimeType(runtimeTypeId, out Type? resolved));
        Assert.Equal(typeof(TypeIdentityRegistryFixtures.UnstableTypeA), resolved);
    }

    [Fact]
    public void Rebuild_PreservesRuntimeId_ForExistingType()
    {
        TypeIdentityRegistry.Rebuild([typeof(TypeIdentityRegistryFixtures.StableTypeA)]);
        int before = TypeIdentityRegistry.GetRuntimeTypeId(typeof(TypeIdentityRegistryFixtures.StableTypeA));

        TypeIdentityRegistry.Rebuild([
            typeof(TypeIdentityRegistryFixtures.StableTypeA),
            typeof(TypeIdentityRegistryFixtures.StableTypeB)
        ]);
        int after = TypeIdentityRegistry.GetRuntimeTypeId(typeof(TypeIdentityRegistryFixtures.StableTypeA));

        Assert.Equal(before, after);
    }

    [Fact]
    public void Version_IncrementsOnRebuild()
    {
        int before = TypeIdentityRegistry.version;
        TypeIdentityRegistry.Rebuild([typeof(TypeIdentityRegistryFixtures.StableTypeA)]);
        int after = TypeIdentityRegistry.version;

        Assert.True(after > before);
    }

    [Fact]
    public void RebuildFromLoadedAssemblies_RespectsNamespacePrefix()
    {
        TypeIdentityRegistry.RebuildFromLoadedAssemblies("TypeIdentityRegistryFixtures");

        Assert.True(TypeIdentityRegistry.TryGetStableTypeId(typeof(TypeIdentityRegistryFixtures.StableTypeA), out Guid stableId));
        Assert.Equal(Guid.Parse("11111111-1111-1111-1111-111111111111"), stableId);
        Assert.True(TypeIdentityRegistry.stableCount >= 1);
    }

    [Fact]
    public void GetStableTypeMapSnapshot_UsesLockKeyFormat()
    {
        TypeIdentityRegistry.Rebuild([typeof(TypeIdentityRegistryFixtures.StableTypeA)]);
        IReadOnlyDictionary<string, Guid> map = TypeIdentityRegistry.GetStableTypeMapSnapshot();

        string expectedKey =
            $"{typeof(TypeIdentityRegistryFixtures.StableTypeA).Assembly.GetName().Name}:{typeof(TypeIdentityRegistryFixtures.StableTypeA).FullName}";
        Assert.True(map.TryGetValue(expectedKey, out Guid stableId));
        Assert.Equal(Guid.Parse("11111111-1111-1111-1111-111111111111"), stableId);
    }
}
