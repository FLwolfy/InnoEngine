using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

using ExternalNamespace;
using Inno.Core.Assemblies;
using Inno.Core.Reflection;

using Xunit;

namespace Inno.Core.Reflection.Tests;

[Collection(TypeCacheCollection.NAME)]
public sealed class TypeCacheTests : IDisposable
{
    private readonly string m_cacheDirectory = Path.Combine(
        Path.GetTempPath(),
        "InnoTypeCacheTests",
        Guid.NewGuid().ToString("N"));

    public TypeCacheTests()
    {
        AssemblyManager.Initialize(new AssemblyManagerOptions { cacheDirectory = m_cacheDirectory });
    }

    public void Dispose()
    {
        AssemblyManager.Shutdown();
        if (Directory.Exists(m_cacheDirectory))
            Directory.Delete(m_cacheDirectory, recursive: true);
    }

    [Fact]
    public void QueriesReturnOnlyConcreteMatchingTypes()
    {
        Assert.Contains(typeof(TestDerived), TypeCache.GetSubTypesOf<TestBase>());
        Assert.DoesNotContain(typeof(TestAbstractDerived), TypeCache.GetSubTypesOf<TestBase>());
        Assert.Contains(typeof(TestContractImpl), TypeCache.GetTypesImplementing<ITestContract>());
        Assert.DoesNotContain(typeof(TestAbstractContractImpl), TypeCache.GetTypesImplementing<ITestContract>());
        Assert.Contains(typeof(AttributedType), TypeCache.GetTypesWithAttribute<TestMarkerAttribute>());
        Assert.DoesNotContain(typeof(AbstractAttributedType), TypeCache.GetTypesWithAttribute<TestMarkerAttribute>());
    }

    [Fact]
    public void RuntimeIdentityRoundTripsAndSurvivesOrdinaryRebuild()
    {
        Assert.True(TypeCache.TryGetRuntimeTypeId(typeof(TestDerived), out int first));
        Assert.True(TypeCache.TryResolveType(first, out Type? resolved));
        Assert.Equal(typeof(TestDerived), resolved);

        AssemblyManager.Rebuild();

        Assert.True(TypeCache.TryGetRuntimeTypeId(typeof(TestDerived), out int second));
        Assert.Equal(first, second);
    }

    [Fact]
    public void StableIdentityUsesExplicitAttributeAndFallbackIsDeterministic()
    {
        Assert.True(TypeCache.TryGetStableTypeId(typeof(StableAnnotatedTypeA), out Guid explicitId));
        Assert.Equal(Guid.Parse("11111111-1111-1111-1111-111111111111"), explicitId);
        Assert.True(TypeCache.TryResolveType(explicitId, out Type? resolved));
        Assert.Equal(typeof(StableAnnotatedTypeA), resolved);

        Assert.True(TypeCache.TryGetStableTypeId(typeof(DeterministicStableType), out Guid first));
        AssemblyManager.Rebuild();
        Assert.True(TypeCache.TryGetStableTypeId(typeof(DeterministicStableType), out Guid second));
        Assert.Equal(first, second);
    }

    [Fact]
    public void AssemblyGroupIncludesTypesOutsideInnoNamespace()
    {
        Assert.True(TypeCache.TryGetRuntimeTypeId(typeof(OutsideNamespaceType), out _));
        Assert.True(TypeCache.TryGetStableTypeId(typeof(OutsideStableAnnotatedType), out Guid stableId));
        Assert.Equal(Guid.Parse("33333333-3333-3333-3333-333333333333"), stableId);
    }

    [Fact]
    public void RebuildPublishesOneNewImmutableSnapshot()
    {
        TypeCacheSnapshot previous = TypeCache.current;

        AssemblyManager.Rebuild();

        TypeCacheSnapshot current = TypeCache.current;
        Assert.NotSame(previous, current);
        Assert.True(current.version > previous.version);
        Assert.Equal(
            previous.GetSubTypesOf<TestBase>().OrderBy(static type => type.FullName),
            current.GetSubTypesOf<TestBase>().OrderBy(static type => type.FullName));
    }

    [Fact]
    public void NewlyLoadedHostAssemblyIsVisibleWithoutLoadingApiOnTypeCache()
    {
        Assembly assembly = Assembly.Load("Inno.Core.Reflection.TestAssemblyA");
        Type marker = assembly.GetType(
            "Inno.Core.Reflection.TestAssets.A.AssemblyAMarkerAttribute",
            throwOnError: true)!;
        Type marked = assembly.GetType(
            "Inno.Core.Reflection.TestAssets.A.AssemblyAMarkedType",
            throwOnError: true)!;

        IReadOnlyList<Type> discovered = GetTypesWithAttribute(marker);

        Assert.Contains(marked, discovered);
        Assert.DoesNotContain(
            typeof(TypeCache).GetMethods(BindingFlags.Public | BindingFlags.Static),
            static method => method.Name is "LoadAssembly" or "Rebuild" or "Initialize" or "Shutdown");
    }

    [Fact]
    public void RemovedHookAttributesAreNotPartOfPublicContract()
    {
        Assembly reflectionAssembly = typeof(TypeCache).Assembly;

        Assert.Null(reflectionAssembly.GetType(
            "Inno.Core.Reflection.TypeCacheInitializeAttribute",
            throwOnError: false));
        Assert.Null(reflectionAssembly.GetType(
            "Inno.Core.Reflection.TypeCacheRebuildAttribute",
            throwOnError: false));
        Type? manager = reflectionAssembly.GetType(
            "Inno.Core.Reflection.TypeCacheManager",
            throwOnError: false);
        Assert.NotNull(manager);
        Assert.False(manager!.IsPublic);
    }

    [Fact]
    public void UnknownIdentityAndNullArgumentsFailSafely()
    {
        Assert.False(TypeCache.TryResolveType(int.MaxValue, out _));
        Assert.False(TypeCache.TryResolveType(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), out _));
        Assert.Throws<ArgumentNullException>(() => TypeCache.TryGetRuntimeTypeId(null!, out _));
        Assert.Throws<ArgumentNullException>(() => TypeCache.TryGetStableTypeId(null!, out _));
    }

    private static IReadOnlyList<Type> GetTypesWithAttribute(Type attributeType)
    {
        MethodInfo method = typeof(TypeCache)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Single(static candidate =>
                candidate.Name == nameof(TypeCache.GetTypesWithAttribute) &&
                candidate.IsGenericMethodDefinition);
        return (IReadOnlyList<Type>)method.MakeGenericMethod(attributeType).Invoke(null, null)!;
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

public sealed class DeterministicStableType;
