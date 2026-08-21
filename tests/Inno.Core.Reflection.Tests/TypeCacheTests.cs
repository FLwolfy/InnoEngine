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
        TypeCacheManager.Initialize();
    }

    public void Dispose()
    {
        TypeCacheManager.Shutdown();
        AssemblyManager.Shutdown();
        if (Directory.Exists(m_cacheDirectory))
            Directory.Delete(m_cacheDirectory, recursive: true);
    }

    [Fact]
    public void QueriesReturnOnlyConcreteMatchingTypes()
    {
        Assert.Contains(typeof(TestDerived), TypeCacheManager.GetSubTypesOf<TestBase>());
        Assert.DoesNotContain(typeof(TestAbstractDerived), TypeCacheManager.GetSubTypesOf<TestBase>());
        Assert.Contains(typeof(TestContractImpl), TypeCacheManager.GetTypesImplementing<ITestContract>());
        Assert.DoesNotContain(typeof(TestAbstractContractImpl), TypeCacheManager.GetTypesImplementing<ITestContract>());
        Assert.Contains(typeof(AttributedType), TypeCacheManager.GetTypesWithAttribute<TestMarkerAttribute>());
        Assert.DoesNotContain(typeof(AbstractAttributedType), TypeCacheManager.GetTypesWithAttribute<TestMarkerAttribute>());
    }

    [Fact]
    public void RuntimeIdentityRoundTripsAndSurvivesOrdinaryRebuild()
    {
        Assert.True(TypeCacheManager.TryGetRuntimeTypeId(typeof(TestDerived), out int first));
        Assert.True(TypeCacheManager.TryResolveType(first, out Type? resolved));
        Assert.Equal(typeof(TestDerived), resolved);

        TypeCacheManager.Rebuild();

        Assert.True(TypeCacheManager.TryGetRuntimeTypeId(typeof(TestDerived), out int second));
        Assert.Equal(first, second);
    }

    [Fact]
    public void StableIdentityUsesExplicitAttributeAndFallbackIsDeterministic()
    {
        Assert.True(TypeCacheManager.TryGetStableTypeId(typeof(StableAnnotatedTypeA), out Guid explicitId));
        Assert.Equal(Guid.Parse("11111111-1111-1111-1111-111111111111"), explicitId);
        Assert.True(TypeCacheManager.TryResolveType(explicitId, out Type? resolved));
        Assert.Equal(typeof(StableAnnotatedTypeA), resolved);

        Assert.True(TypeCacheManager.TryGetStableTypeId(typeof(DeterministicStableType), out Guid first));
        TypeCacheManager.Rebuild();
        Assert.True(TypeCacheManager.TryGetStableTypeId(typeof(DeterministicStableType), out Guid second));
        Assert.Equal(first, second);
    }

    [Fact]
    public void GeneratedIdentityAndExplicitAttributeUseOneCurrentIdentity()
    {
        Guid generatedId = Guid.Parse("44444444-4444-4444-4444-444444444444");

        Assert.True(TypeCacheManager.TryGetStableTypeId(typeof(GeneratedMappedType), out Guid stableId));
        Assert.Equal(generatedId, stableId);
        Assert.True(TypeCacheManager.TryResolveType(generatedId, out Type? generatedResolved));
        Assert.Equal(typeof(GeneratedMappedType), generatedResolved);

        Assert.True(TypeCacheManager.TryGetStableTypeId(typeof(StableAnnotatedTypeA), out Guid explicitId));
        Assert.Equal(Guid.Parse("11111111-1111-1111-1111-111111111111"), explicitId);
        Assert.False(TypeCacheManager.TryResolveType(
            Guid.Parse("66666666-6666-6666-6666-666666666666"),
            out _));
    }

    [Fact]
    public void AssemblyGroupIncludesTypesOutsideInnoNamespace()
    {
        Assert.True(TypeCacheManager.TryGetRuntimeTypeId(typeof(OutsideNamespaceType), out _));
        Assert.True(TypeCacheManager.TryGetStableTypeId(typeof(OutsideStableAnnotatedType), out Guid stableId));
        Assert.Equal(Guid.Parse("33333333-3333-3333-3333-333333333333"), stableId);
    }

    [Fact]
    public void RebuildPublishesOneNewImmutableSnapshot()
    {
        TypeCacheSnapshot previous = TypeCacheManager.current;

        TypeCacheManager.Rebuild();

        TypeCacheSnapshot current = TypeCacheManager.current;
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
            typeof(TypeCacheManager).GetMethods(BindingFlags.Public | BindingFlags.Static),
            static method => method.Name == "LoadAssembly");
        Assert.Contains(
            typeof(TypeCacheManager).GetMethods(BindingFlags.Public | BindingFlags.Static),
            static method => method.Name is "Rebuild" or "Initialize" or "Shutdown");
    }

    [Fact]
    public void RemovedHookAttributesAreNotPartOfPublicContract()
    {
        Assembly reflectionAssembly = typeof(TypeCacheManager).Assembly;

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
        Assert.True(manager!.IsPublic);
        Assert.Null(reflectionAssembly.GetType(
            "Inno.Core.Reflection.TypeCache",
            throwOnError: false));
    }

    [Fact]
    public void UnknownIdentityAndNullArgumentsFailSafely()
    {
        Assert.False(TypeCacheManager.TryResolveType(int.MaxValue, out _));
        Assert.False(TypeCacheManager.TryResolveType(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), out _));
        Assert.Throws<ArgumentNullException>(() => TypeCacheManager.TryGetRuntimeTypeId(null!, out _));
        Assert.Throws<ArgumentNullException>(() => TypeCacheManager.TryGetStableTypeId(null!, out _));
    }

    private static IReadOnlyList<Type> GetTypesWithAttribute(Type attributeType)
    {
        MethodInfo method = typeof(TypeCacheManager)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Single(static candidate =>
                candidate.Name == nameof(TypeCacheManager.GetTypesWithAttribute) &&
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
public sealed class GeneratedMappedType;
