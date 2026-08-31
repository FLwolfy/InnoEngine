using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

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
        Assert.Contains(TypeCacheManager.GetTypeRef(typeof(TestDerived)), TypeCacheManager.GetSubTypesOf<TestBase>());
        Assert.DoesNotContain(TypeCacheManager.GetTypeRef(typeof(TestAbstractDerived)), TypeCacheManager.GetSubTypesOf<TestBase>());
        Assert.Contains(TypeCacheManager.GetTypeRef(typeof(TestContractImpl)), TypeCacheManager.GetTypesImplementing<ITestContract>());
        Assert.DoesNotContain(TypeCacheManager.GetTypeRef(typeof(TestAbstractContractImpl)), TypeCacheManager.GetTypesImplementing<ITestContract>());
        Assert.Contains(TypeCacheManager.GetTypeRef(typeof(AttributedType)), TypeCacheManager.GetTypesWithAttribute<TestMarkerAttribute>());
        Assert.DoesNotContain(TypeCacheManager.GetTypeRef(typeof(AbstractAttributedType)), TypeCacheManager.GetTypesWithAttribute<TestMarkerAttribute>());
    }

    [Fact]
    public void RuntimeIdentityRoundTripsAndSurvivesOrdinaryRebuild()
    {
        TypeRef first = TypeCacheManager.GetTypeRef(typeof(TestDerived));
        Assert.Equal(typeof(TestDerived), first.Resolve());

        TypeCacheManager.Rebuild();

        TypeRef second = TypeCacheManager.GetTypeRef(typeof(TestDerived));
        Assert.Equal(first.runtimeId, second.runtimeId);
    }

    [Fact]
    public void StableIdentityUsesExplicitAttributeAndFallbackIsDeterministic()
    {
        TypeRef explicitType = TypeCacheManager.GetTypeRef(typeof(StableAnnotatedTypeA));
        Assert.Equal(Guid.Parse("11111111-1111-1111-1111-111111111111"), explicitType.stableId);
        Assert.Equal(typeof(StableAnnotatedTypeA), explicitType.Resolve());

        Guid first = TypeCacheManager.GetTypeRef(typeof(DeterministicStableType)).stableId;
        TypeCacheManager.Rebuild();
        Guid second = TypeCacheManager.GetTypeRef(typeof(DeterministicStableType)).stableId;
        Assert.Equal(first, second);
    }

    [Fact]
    public void GeneratedIdentityAndExplicitAttributeUseOneCurrentIdentity()
    {
        Guid generatedId = Guid.Parse("44444444-4444-4444-4444-444444444444");

        TypeRef generated = TypeCacheManager.GetTypeRef(typeof(GeneratedMappedType));
        Assert.Equal(generatedId, generated.stableId);
        Assert.Equal(typeof(GeneratedMappedType), generated.Resolve());

        TypeRef explicitType = TypeCacheManager.GetTypeRef(typeof(StableAnnotatedTypeA));
        Assert.Equal(Guid.Parse("11111111-1111-1111-1111-111111111111"), explicitType.stableId);
        Assert.False(new TypeRef(Guid.Parse("66666666-6666-6666-6666-666666666666")).isValid);
    }

    [Fact]
    public void AssemblyMetadataIncludesTypesOutsideInnoNamespace()
    {
        Assert.True(TypeCacheManager.GetTypeRef(typeof(OutsideNamespaceType)).runtimeId > 0);
        Assert.Equal(
            Guid.Parse("33333333-3333-3333-3333-333333333333"),
            TypeCacheManager.GetTypeRef(typeof(OutsideStableAnnotatedType)).stableId);
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
            previous.GetSubTypesOf<TestBase>().OrderBy(static type => type.stableId),
            current.GetSubTypesOf<TestBase>().OrderBy(static type => type.stableId));
    }

    [Fact]
    public void OrdinaryRebuildReusesTheExactPerAssemblyDiscoverySlice()
    {
        TypeCacheSnapshot previous = TypeCacheManager.current;
        IReadOnlyDictionary<Assembly, Type[]> previousSlices = GetAssemblySlices(previous);
        Assembly testAssembly = typeof(TypeCacheTests).Assembly;

        TypeCacheManager.Rebuild();

        IReadOnlyDictionary<Assembly, Type[]> currentSlices = GetAssemblySlices(TypeCacheManager.current);
        Assert.True(previousSlices.TryGetValue(testAssembly, out Type[]? previousTypes));
        Assert.True(currentSlices.TryGetValue(testAssembly, out Type[]? currentTypes));
        Assert.Same(previousTypes, currentTypes);
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

        IReadOnlyList<TypeRef> discovered = GetTypesWithAttribute(marker);

        Assert.Contains(discovered, type => type.Resolve() == marked);
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
        TypeRef empty = default;
        TypeRef explicitEmpty = new(Guid.Empty);
        TypeRef unknown = new(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"));
        Assert.False(empty.isValid);
        Assert.False(explicitEmpty.isValid);
        Assert.False(unknown.isValid);
        Assert.Throws<InvalidOperationException>(empty.Resolve);
        Assert.Throws<InvalidOperationException>(explicitEmpty.Resolve);
        Assert.Throws<InvalidOperationException>(unknown.Resolve);
        Assert.Throws<ArgumentNullException>(() => TypeCacheManager.TryGetTypeRef(null!, out _));
    }

    [Fact]
    public void PublicTypeCacheQueriesExposeOnlyTypeRefs()
    {
        string[] queryNames =
        [
            nameof(TypeCacheManager.GetSubTypesOf),
            nameof(TypeCacheManager.GetTypesImplementing),
            nameof(TypeCacheManager.GetTypesWithAttribute)
        ];
        MethodInfo[] managerQueries = typeof(TypeCacheManager)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(method => queryNames.Contains(method.Name, StringComparer.Ordinal))
            .ToArray();
        MethodInfo[] snapshotQueries = typeof(TypeCacheSnapshot)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(method => queryNames.Contains(method.Name, StringComparer.Ordinal))
            .ToArray();

        Assert.NotEmpty(managerQueries);
        Assert.NotEmpty(snapshotQueries);
        Assert.All(managerQueries.Concat(snapshotQueries), static method =>
            Assert.Equal(typeof(IReadOnlyList<TypeRef>), method.ReturnType));
        Assert.Equal(typeof(IReadOnlyList<TypeRef>), typeof(TypeCacheSnapshot)
            .GetProperty(nameof(TypeCacheSnapshot.types))!.PropertyType);
        Assert.DoesNotContain(
            typeof(TypeCacheManager).GetMethods(BindingFlags.Public | BindingFlags.Static),
            static method => method.Name.Contains("ResolveType", StringComparison.Ordinal) ||
                             method.ReturnType == typeof(Type) ||
                             method.ReturnType == typeof(Type[]));
        Assert.IsNotType<TypeRef[]>(TypeCacheManager.current.types);
        Assert.IsNotType<TypeRef[]>(TypeCacheManager.GetSubTypesOf<TestBase>());
    }

    [Fact]
    public void TypeRefContainsOnlyStableAndRuntimeValueIdentity()
    {
        FieldInfo[] fields = typeof(TypeRef).GetFields(BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.Equal(2, fields.Length);
        Assert.Contains(fields, static field => field.FieldType == typeof(Guid));
        Assert.Contains(fields, static field => field.FieldType == typeof(int));
        Assert.DoesNotContain(fields, static field =>
            field.FieldType == typeof(Type) ||
            field.FieldType == typeof(Assembly) ||
            typeof(Delegate).IsAssignableFrom(field.FieldType));
    }

    [Fact]
    public void LaterRegistryActivationFailureRestoresEveryEarlierRegistry()
    {
        using var first = new TransactionalTestRegistry();
        using var second = new TransactionalTestRegistry();
        first.Initialize();
        second.Initialize();
        int firstPrevious = first.publishedSnapshotId;
        int secondPrevious = second.publishedSnapshotId;
        second.failActivation = true;

        Assert.Throws<InvalidOperationException>(TypeCacheManager.Rebuild);

        Assert.Equal(firstPrevious, first.publishedSnapshotId);
        Assert.Equal(secondPrevious, second.publishedSnapshotId);
        Assert.Equal(1, first.rollbackCount);
        Assert.Equal(1, second.rollbackCount);
        Assert.Contains(firstPrevious + 1, first.disposedSnapshotIds);
        Assert.Contains(secondPrevious + 1, second.disposedSnapshotIds);
        Assert.DoesNotContain(firstPrevious, first.disposedSnapshotIds);
        Assert.DoesNotContain(secondPrevious, second.disposedSnapshotIds);
    }

    [Fact]
    public void SnapshotCleanupFailureDoesNotRollBackCommittedCandidate()
    {
        using var registry = new TransactionalTestRegistry();
        registry.Initialize();
        int previous = registry.publishedSnapshotId;
        registry.failSnapshotCleanup = true;

        TypeCacheManager.Rebuild();

        Assert.Equal(previous + 1, registry.publishedSnapshotId);
        Assert.Equal(0, registry.rollbackCount);
        Assert.Equal(1, registry.cleanupFailureCount);
        Assert.Contains(previous, registry.disposedSnapshotIds);
    }

    [Fact]
    public void QueuedRebuildFailureRollsBackOnlyItsOwnRegistryTransaction()
    {
        using var registry = new TransactionalTestRegistry();
        registry.Initialize();
        int previous = registry.publishedSnapshotId;
        registry.rebuildDuringActivation = true;

        Assert.Throws<InvalidOperationException>(TypeCacheManager.Rebuild);

        Assert.Equal(previous + 1, registry.publishedSnapshotId);
        Assert.Equal(1, registry.rollbackCount);
        Assert.Equal(0, registry.cleanupFailureCount);
        Assert.DoesNotContain("deferred refresh", registry.cleanupFailurePhases);

        registry.failActivation = false;
        TypeCacheManager.Rebuild();
        Assert.True(registry.publishedSnapshotId > previous + 1);
    }

    [Fact]
    public void StandaloneRefreshReconcilesAReentrantTypeChangeAsASeparateTransaction()
    {
        using var registry = new TransactionalTestRegistry
        {
            rebuildDuringActivation = true
        };

        Assert.Throws<InvalidOperationException>(registry.Initialize);

        Assert.Equal(1, registry.publishedSnapshotId);
        Assert.Equal(1, registry.rollbackCount);
        Assert.Equal(0, registry.cleanupFailureCount);

        registry.failActivation = false;
        registry.Initialize();

        Assert.True(registry.publishedSnapshotId > 1);
    }

    [Fact]
    public async Task PreparedRegistryTransactionReservesTheRegistryUntilCompletion()
    {
        using var registry = new ReservationTestRegistry();
        MethodInfo prepare = typeof(TypeRegistry<ReservationTestRegistry.Snapshot>)
            .GetMethod("Prepare", BindingFlags.Instance | BindingFlags.NonPublic)!;
        object first = prepare.Invoke(
            registry,
            [TypeCacheManager.current, false])!;

        Task[] overlappingRefreshes = Enumerable.Range(0, 8)
            .Select(_ => Task.Run(registry.Refresh))
            .ToArray();
        await Task.WhenAll(overlappingRefreshes);

        Assert.Equal(1, registry.buildCount);
        first.GetType().GetMethod("Activate")!.Invoke(first, null);
        first.GetType().GetMethod("Complete")!.Invoke(first, null);
        Assert.Equal(1, registry.buildCount);
        Assert.True(registry.isInitialized);
    }

    [Fact]
    public void DisposingAnActivatedRegistryDoesNotMakeGlobalCompletionFallible()
    {
        var registry = new TransactionalTestRegistry();
        MethodInfo prepare = typeof(TypeRegistry<TransactionalTestRegistry.Snapshot>)
            .GetMethod("Prepare", BindingFlags.Instance | BindingFlags.NonPublic)!;
        object transaction = prepare.Invoke(
            registry,
            [TypeCacheManager.current, false])!;

        transaction.GetType().GetMethod("Activate")!.Invoke(transaction, null);
        registry.Dispose();
        transaction.GetType().GetMethod("Complete")!.Invoke(transaction, null);

        Assert.False(registry.isInitialized);
        Assert.Equal(0, registry.rollbackCount);
        Assert.Equal([1], registry.disposedSnapshotIds);
    }

    private static IReadOnlyList<TypeRef> GetTypesWithAttribute(Type attributeType)
    {
        MethodInfo method = typeof(TypeCacheManager)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Single(static candidate =>
                candidate.Name == nameof(TypeCacheManager.GetTypesWithAttribute) &&
                candidate.IsGenericMethodDefinition);
        return (IReadOnlyList<TypeRef>)method.MakeGenericMethod(attributeType).Invoke(null, null)!;
    }

    private static IReadOnlyDictionary<Assembly, Type[]> GetAssemblySlices(TypeCacheSnapshot snapshot)
    {
        FieldInfo field = typeof(TypeCacheSnapshot).GetField(
            "m_typesByAssembly",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        return (IReadOnlyDictionary<Assembly, Type[]>)field.GetValue(snapshot)!;
    }
}

internal sealed class TransactionalTestRegistry : TypeRegistry<TransactionalTestRegistry.Snapshot>
{
    private int m_nextSnapshotId;

    internal bool failActivation;
    internal bool failSnapshotCleanup;
    internal bool rebuildDuringActivation;
    internal int rollbackCount;
    internal int cleanupFailureCount;
    internal int publishedSnapshotId;
    internal List<int> disposedSnapshotIds { get; } = [];
    internal List<string> cleanupFailurePhases { get; } = [];

    internal void Initialize() => _ = current;

    protected override Snapshot Build(TypeCacheSnapshot types) => new(++m_nextSnapshotId);

    protected override void OnActivating(Snapshot? previous, Snapshot candidate)
    {
        publishedSnapshotId = candidate.id;
        if (rebuildDuringActivation)
        {
            rebuildDuringActivation = false;
            TypeCacheManager.Rebuild();
            failActivation = true;
            return;
        }
        if (failActivation)
            throw new InvalidOperationException("Injected registry activation failure.");
    }

    protected override void OnActivationRolledBack(Snapshot? previous, Snapshot candidate)
    {
        publishedSnapshotId = previous?.id ?? 0;
        rollbackCount++;
    }

    protected override void DisposeSnapshot(Snapshot snapshot)
    {
        disposedSnapshotIds.Add(snapshot.id);
        if (failSnapshotCleanup)
            throw new InvalidOperationException("Injected snapshot cleanup failure.");
    }

    protected override void OnCleanupFailed(string phase, Exception exception)
    {
        cleanupFailureCount++;
        cleanupFailurePhases.Add(phase);
    }

    internal sealed record Snapshot(int id);
}

internal sealed class ReservationTestRegistry : TypeRegistry<ReservationTestRegistry.Snapshot>
{
    internal int buildCount;

    protected override Snapshot Build(TypeCacheSnapshot types)
        => new(++buildCount);

    internal sealed record Snapshot(int id);
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
