using System;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;

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

    [Fact]
    public void Rebuild_WithSpecificAssembly_LoadsTypesFromThatAssembly()
    {
        TypeCacheManager.Rebuild(typeof(TypeCacheTests).Assembly);

        Assert.True(TypeCache.TryGetRuntimeTypeId(typeof(TestDerived), out _));
        Assert.False(TypeCache.TryGetRuntimeTypeId(typeof(TypeCacheManager), out _));
    }

    [Fact]
    public void Rebuild_WithNonInnoAssembly_DoesNotLoadTestTypes()
    {
        TypeCacheManager.Rebuild(typeof(string).Assembly);

        Assert.False(TypeCache.TryGetRuntimeTypeId(typeof(TestDerived), out _));
        Assert.False(TypeCache.TryGetRuntimeTypeId(typeof(TypeCacheManager), out _));
    }

    [Fact]
    public void Rebuild_WithCoreReflectionAssembly_LoadsCoreTypesButNotTestTypes()
    {
        TypeCacheManager.Rebuild(typeof(TypeCacheManager).Assembly);

        Assert.True(TypeCache.TryGetRuntimeTypeId(typeof(TypeCacheManager), out _));
        Assert.False(TypeCache.TryGetRuntimeTypeId(typeof(TestDerived), out _));
    }

    [Fact]
    public void Rebuild_WithDynamicAssembly_IgnoresAssemblyAndClearsLoadedTypes()
    {
        AssemblyBuilder dynamicAssembly = AssemblyBuilder.DefineDynamicAssembly(
            new AssemblyName("Inno.Dynamic.TypeCacheTests"),
            AssemblyBuilderAccess.Run);
        ModuleBuilder moduleBuilder = dynamicAssembly.DefineDynamicModule("Main");
        Type generatedType = moduleBuilder
            .DefineType("Inno.Dynamic.GeneratedType", TypeAttributes.Public)
            .CreateType()!;

        TypeCacheManager.Rebuild(dynamicAssembly);

        Assert.False(TypeCache.TryGetRuntimeTypeId(generatedType, out _));
        Assert.False(TypeCache.TryGetRuntimeTypeId(typeof(TestDerived), out _));
        Assert.False(TypeCache.TryGetRuntimeTypeId(typeof(TypeCacheManager), out _));
    }

    [Fact]
    public void Rebuild_GlobalAfterScopedRebuild_RestoresAllLoadedTypes()
    {
        TypeCacheManager.Rebuild(typeof(string).Assembly);
        Assert.False(TypeCache.TryGetRuntimeTypeId(typeof(TestDerived), out _));
        Assert.False(TypeCache.TryGetRuntimeTypeId(typeof(TypeCacheManager), out _));

        TypeCacheManager.Rebuild();

        Assert.True(TypeCache.TryGetRuntimeTypeId(typeof(TestDerived), out _));
        Assert.True(TypeCache.TryGetRuntimeTypeId(typeof(TypeCacheManager), out _));
    }

    [Fact]
    public void Rebuild_NullAssembly_BehavesAsGlobalRebuild()
    {
        TypeCacheManager.Rebuild(typeof(string).Assembly);
        Assert.False(TypeCache.TryGetRuntimeTypeId(typeof(TestDerived), out _));

        TypeCacheManager.Rebuild((Assembly?)null);

        Assert.True(TypeCache.TryGetRuntimeTypeId(typeof(TestDerived), out _));
        Assert.True(TypeCache.TryGetRuntimeTypeId(typeof(TypeCacheManager), out _));
    }

    [Fact]
    public void RuntimeTypeId_IsInvalidAfterScopedRemoval_AndChangesWhenTypeReturns()
    {
        TypeCacheManager.Rebuild();
        Assert.True(TypeCache.TryGetRuntimeTypeId(typeof(TestDerived), out int originalRuntimeId));
        Assert.True(TypeCache.TryResolveType(originalRuntimeId, out Type? resolvedBeforeRemoval));
        Assert.Equal(typeof(TestDerived), resolvedBeforeRemoval);

        TypeCacheManager.Rebuild(typeof(string).Assembly);
        Assert.False(TypeCache.TryResolveType(originalRuntimeId, out _));

        TypeCacheManager.Rebuild();
        Assert.True(TypeCache.TryGetRuntimeTypeId(typeof(TestDerived), out int runtimeIdAfterReturn));
        Assert.NotEqual(originalRuntimeId, runtimeIdAfterReturn);
    }

    [Fact]
    public void StableTypeId_IsInvalidAfterScopedRemoval_AndResolvesAgainAfterGlobalRebuild()
    {
        Guid stableId = Guid.Parse("11111111-1111-1111-1111-111111111111");

        TypeCacheManager.Rebuild();
        Assert.True(TypeCache.TryResolveType(stableId, out Type? resolvedBeforeRemoval));
        Assert.Equal(typeof(StableAnnotatedTypeA), resolvedBeforeRemoval);

        TypeCacheManager.Rebuild(typeof(string).Assembly);
        Assert.False(TypeCache.TryResolveType(stableId, out _));

        TypeCacheManager.Rebuild();
        Assert.True(TypeCache.TryResolveType(stableId, out Type? resolvedAfterRestore));
        Assert.Equal(typeof(StableAnnotatedTypeA), resolvedAfterRestore);
    }

    [Fact]
    public void QueryApis_ReturnEmpty_WhenCacheBuiltFromAssemblyWithoutInnoTypes()
    {
        TypeCacheManager.Rebuild(typeof(string).Assembly);

        Assert.Empty(TypeCache.GetSubTypesOf<TestBase>());
        Assert.Empty(TypeCache.GetTypesImplementing<ITestContract>());
        Assert.Empty(TypeCache.GetTypesWithAttribute<TestMarkerAttribute>());
    }

    [Fact]
    public void TryGetTypeId_ThrowsArgumentNullException_ForNullType()
    {
        TypeCacheManager.Rebuild();

        Assert.Throws<ArgumentNullException>(() => TypeCache.TryGetRuntimeTypeId(null!, out _));
        Assert.Throws<ArgumentNullException>(() => TypeCache.TryGetStableTypeId(null!, out _));
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
