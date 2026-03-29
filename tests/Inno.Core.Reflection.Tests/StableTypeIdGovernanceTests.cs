using System;
using System.Collections.Generic;
using System.IO;

using Inno.Core.Reflection;

using Xunit;

namespace Inno.Core.Reflection.Tests;

[Collection(TypeIdentityRegistryCollection.NAME)]
public sealed class StableTypeIdGovernanceTests
{
    [Fact]
    public void ParseLockJson_InvalidGuid_Throws()
    {
        const string json = """
                            {
                              "Inno.Core:Foo.Bar": "not-a-guid"
                            }
                            """;

        Assert.Throws<InvalidOperationException>(() => StableTypeIdGovernance.ParseLockJson(json));
    }

    [Fact]
    public void ToLockJson_And_ParseLockJson_RoundTrip()
    {
        var map = new Dictionary<string, Guid>(StringComparer.Ordinal)
        {
            ["A:Type1"] = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            ["B:Type2"] = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb")
        };

        string json = StableTypeIdGovernance.ToLockJson(map);
        var parsed = StableTypeIdGovernance.ParseLockJson(json);

        Assert.Equal(2, parsed.Count);
        Assert.Equal(map["A:Type1"], parsed["A:Type1"]);
        Assert.Equal(map["B:Type2"], parsed["B:Type2"]);
    }

    [Fact]
    public void ValidateLockOrThrow_WhenEqual_DoesNotThrow()
    {
        var lockMap = new Dictionary<string, Guid>(StringComparer.Ordinal)
        {
            ["X:T"] = Guid.Parse("11111111-1111-1111-1111-111111111111")
        };
        var currentMap = new Dictionary<string, Guid>(StringComparer.Ordinal)
        {
            ["X:T"] = Guid.Parse("11111111-1111-1111-1111-111111111111")
        };

        StableTypeIdGovernance.ValidateLockOrThrow(lockMap, currentMap);
    }

    [Fact]
    public void ValidateLockOrThrow_WhenDiffExists_Throws()
    {
        var lockMap = new Dictionary<string, Guid>(StringComparer.Ordinal)
        {
            ["A:Old"] = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            ["B:Keep"] = Guid.Parse("22222222-2222-2222-2222-222222222222")
        };
        var currentMap = new Dictionary<string, Guid>(StringComparer.Ordinal)
        {
            ["B:Keep"] = Guid.Parse("33333333-3333-3333-3333-333333333333"),
            ["C:New"] = Guid.Parse("44444444-4444-4444-4444-444444444444")
        };

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
            StableTypeIdGovernance.ValidateLockOrThrow(lockMap, currentMap));

        Assert.Contains("Added:", ex.Message);
        Assert.Contains("Removed:", ex.Message);
        Assert.Contains("Changed:", ex.Message);
    }

    [Fact]
    public void BuildCurrentStableMap_ReflectsCurrentRegistry()
    {
        TypeIdentityRegistry.Rebuild([typeof(TypeIdentityRegistryTests)]);
        IReadOnlyDictionary<string, Guid> map = StableTypeIdGovernance.BuildCurrentStableMap();

        Assert.Empty(map);
    }

    [Fact]
    public void StableTypeIdLock_ForCorePrefix_MustMatchCurrentSnapshot()
    {
        TypeCacheManager.Refresh();
        TypeIdentityRegistry.RebuildFromLoadedAssemblies("Inno.Core");

        string lockPath = Path.Combine(AppContext.BaseDirectory, "StableTypeId.lock.json");
        string json = File.ReadAllText(lockPath);

        var locked = StableTypeIdGovernance.ParseLockJson(json);
        var current = StableTypeIdGovernance.BuildCurrentStableMap();

        StableTypeIdGovernance.ValidateLockOrThrow(locked, current);
    }
}
