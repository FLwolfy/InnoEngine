using System;
using System.Linq;

using Inno.Core.Reflection;
using ExternalNamespace;

using Xunit;

namespace Inno.Core.Reflection.Tests;

[Collection(TypeCacheCollection.NAME)]
public sealed class TypeCacheTests
{
    [Fact]
    public void GetSubTypesOf_ReturnsDerivedTypes()
    {
        TypeCacheManager.Rebuild();

        var types = TypeCache.GetSubTypesOf<TestBase>();
        Assert.Contains(typeof(TestDerived), types);
        Assert.DoesNotContain(typeof(TestAbstractDerived), types);
    }

    [Fact]
    public void GetTypesImplementing_ReturnsImplementations()
    {
        TypeCacheManager.Rebuild();

        var types = TypeCache.GetTypesImplementing<ITestContract>();
        Assert.Contains(typeof(TestContractImpl), types);
        Assert.DoesNotContain(typeof(TestAbstractContractImpl), types);
    }

    [Fact]
    public void GetTypesWithAttribute_ReturnsAttributedTypes()
    {
        TypeCacheManager.Rebuild();

        var types = TypeCache.GetTypesWithAttribute<TestMarkerAttribute>();
        Assert.Contains(typeof(AttributedType), types);
        Assert.DoesNotContain(typeof(AbstractAttributedType), types);
    }

    [Fact]
    public void TryGetRuntimeTypeId_ForLoadedType_ReturnsTrue()
    {
        TypeCacheManager.Rebuild();

        Assert.True(TypeCache.TryGetRuntimeTypeId(typeof(TestDerived), out int runtimeTypeId));
        Assert.True(runtimeTypeId > 0);
    }

    [Fact]
    public void TryGetRuntimeTypeId_ForTypeOutsideInnoNamespace_ReturnsFalse()
    {
        TypeCacheManager.Rebuild();

        Assert.False(TypeCache.TryGetRuntimeTypeId(typeof(OutsideNamespaceType), out _));
    }

    [Fact]
    public void TryGetStableTypeId_ForTypeWithoutStableAttribute_ReturnsFalse()
    {
        TypeCacheManager.Rebuild();

        Assert.False(TypeCache.TryGetStableTypeId(typeof(TypeCacheManager), out _));
    }

    [Fact]
    public void TryGetStableTypeId_ForTypeOutsideInnoNamespace_ReturnsFalse()
    {
        TypeCacheManager.Rebuild();

        Assert.False(TypeCache.TryGetStableTypeId(typeof(OutsideStableAnnotatedType), out _));
    }

    [Fact]
    public void TryGetStableTypeId_ForStableAnnotatedType_ReturnsTrueAndExpectedGuid()
    {
        TypeCacheManager.Rebuild();

        Assert.True(TypeCache.TryGetStableTypeId(typeof(StableAnnotatedTypeA), out Guid stableId));
        Assert.Equal(Guid.Parse("11111111-1111-1111-1111-111111111111"), stableId);
    }

    [Fact]
    public void TryResolveType_ByStableId_ReturnsOriginalType()
    {
        TypeCacheManager.Rebuild();

        Guid stableId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        Assert.True(TypeCache.TryResolveType(stableId, out Type? resolved));
        Assert.Equal(typeof(StableAnnotatedTypeA), resolved);
    }

    [Fact]
    public void TryResolveRuntimeType_ForNonLoadedRuntimeTypeId_ReturnsFalse()
    {
        TypeCacheManager.Rebuild();
        Assert.False(TypeCache.TryResolveType(int.MaxValue, out _));
    }

    [Fact]
    public void TryResolveStableType_ForUnknownStableId_ReturnsFalse()
    {
        TypeCacheManager.Rebuild();

        Assert.False(TypeCache.TryResolveType(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), out _));
    }

    [Fact]
    public void RuntimeTypeId_ResolvesBackToSameType()
    {
        TypeCacheManager.Rebuild();

        Assert.True(TypeCache.TryGetRuntimeTypeId(typeof(TestContractImpl), out int runtimeId));
        Assert.True(TypeCache.TryResolveType(runtimeId, out Type? resolved));
        Assert.Equal(typeof(TestContractImpl), resolved);
    }

    [Fact]
    public void RuntimeTypeId_IsStableAcrossRebuild_ForExistingType()
    {
        TypeCacheManager.Rebuild();
        Assert.True(TypeCache.TryGetRuntimeTypeId(typeof(TestDerived), out int firstId));

        TypeCacheManager.Rebuild();
        Assert.True(TypeCache.TryGetRuntimeTypeId(typeof(TestDerived), out int secondId));

        Assert.Equal(firstId, secondId);
    }

    [Fact]
    public void RuntimeTypeId_IsUniqueBetweenTypes()
    {
        TypeCacheManager.Rebuild();

        Assert.True(TypeCache.TryGetRuntimeTypeId(typeof(TestDerived), out int a));
        Assert.True(TypeCache.TryGetRuntimeTypeId(typeof(TestContractImpl), out int b));
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void QueryResults_AreDeterministicAcrossRebuild()
    {
        TypeCacheManager.Rebuild();
        Type[] first = TypeCache.GetSubTypesOf<TestBase>().OrderBy(static t => t.FullName, StringComparer.Ordinal).ToArray();

        TypeCacheManager.Rebuild();
        Type[] second = TypeCache.GetSubTypesOf<TestBase>().OrderBy(static t => t.FullName, StringComparer.Ordinal).ToArray();

        Assert.Equal(first, second);
    }
}

public class TestBase;
public sealed class TestDerived : TestBase;
public abstract class TestAbstractDerived : TestBase;

public interface ITestContract;
public sealed class TestContractImpl : ITestContract;
public abstract class TestAbstractContractImpl : ITestContract;

[AttributeUsage(AttributeTargets.Class)]
public sealed class TestMarkerAttribute : Attribute;

[TestMarker]
public sealed class AttributedType;

[TestMarker]
public abstract class AbstractAttributedType;

[StableTypeId("11111111-1111-1111-1111-111111111111")]
public sealed class StableAnnotatedTypeA;

[StableTypeId("22222222-2222-2222-2222-222222222222")]
public struct StableAnnotatedTypeB;
