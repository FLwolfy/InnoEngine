using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

using ExternalNamespace;
using Inno.Extensibility.Modules;
using Inno.Extensibility.Types;

using Xunit;

namespace Inno.Extensibility.Types.Tests;

[Collection(TypeCacheCollection.NAME)]
public sealed class TypeCacheTests : IDisposable
{
    private readonly string m_cacheDirectory = Path.Combine(
        Path.GetTempPath(),
        "InnoTypeCacheTests",
        Guid.NewGuid().ToString("N"));
    private readonly ModuleHost m_modules;
    private readonly TypeCatalog m_types;

    public TypeCacheTests()
    {
        m_modules = new ModuleHost(new ModuleHostOptions { cacheDirectory = m_cacheDirectory });
        m_types = new TypeCatalog(m_modules);
    }

    public void Dispose()
    {
        m_types.Dispose();
        m_modules.Dispose();
        if (Directory.Exists(m_cacheDirectory))
            Directory.Delete(m_cacheDirectory, recursive: true);
    }

    [Fact]
    public void QueriesReturnOnlyConcreteMatchingTypes()
    {
        Assert.Contains(m_types.GetTypeRef(typeof(TestDerived)), m_types.GetSubTypesOf<TestBase>());
        Assert.DoesNotContain(
            m_types.GetTypeRef(typeof(TestAbstractDerived)),
            m_types.GetSubTypesOf<TestBase>());
        Assert.Contains(
            m_types.GetTypeRef(typeof(TestContractImpl)),
            m_types.GetTypesImplementing<ITestContract>());
        Assert.DoesNotContain(
            m_types.GetTypeRef(typeof(TestAbstractContractImpl)),
            m_types.GetTypesImplementing<ITestContract>());
        Assert.Contains(
            m_types.GetTypeRef(typeof(AttributedType)),
            m_types.GetTypesWithAttribute<TestMarkerAttribute>());
        Assert.DoesNotContain(
            m_types.GetTypeRef(typeof(AbstractAttributedType)),
            m_types.GetTypesWithAttribute<TestMarkerAttribute>());
    }

    [Fact]
    public void StableAndRuntimeIdentitySurviveOrdinaryRebuild()
    {
        TypeRef first = m_types.GetTypeRef(typeof(TestDerived));
        TypeCacheSnapshot previous = m_types.current;

        m_types.Rebuild();

        TypeRef second = m_types.GetTypeRef(typeof(TestDerived));
        Assert.Equal(typeof(TestDerived), first.Resolve(m_types));
        Assert.Equal(first.stableId, second.stableId);
        Assert.Equal(first.runtimeId, second.runtimeId);
        Assert.True(m_types.current.version > previous.version);
    }

    [Fact]
    public void ExplicitAndGeneratedStableIdentitiesAreCanonical()
    {
        Assert.Equal(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            m_types.GetTypeRef(typeof(StableAnnotatedTypeA)).stableId);
        Assert.Equal(
            Guid.Parse("44444444-4444-4444-4444-444444444444"),
            m_types.GetTypeRef(typeof(GeneratedMappedType)).stableId);

        Guid first = m_types.GetTypeRef(typeof(DeterministicStableType)).stableId;
        m_types.Rebuild();
        Assert.Equal(first, m_types.GetTypeRef(typeof(DeterministicStableType)).stableId);
    }

    [Fact]
    public void DiscoveryIncludesTypesOutsideInnoNamespace()
    {
        Assert.True(m_types.GetTypeRef(typeof(OutsideNamespaceType)).runtimeId > 0);
        Assert.Equal(
            Guid.Parse("33333333-3333-3333-3333-333333333333"),
            m_types.GetTypeRef(typeof(OutsideStableAnnotatedType)).stableId);
    }

    [Fact]
    public void NewlyLoadedHostAssemblyAppearsAfterCatalogRefresh()
    {
        Assembly assembly = Assembly.Load("Inno.Extensibility.Types.TestAssemblyA");
        Type marked = assembly.GetType(
            "Inno.Extensibility.Types.TestAssets.A.AssemblyAMarkedType",
            throwOnError: true)!;

        m_types.Rebuild();

        Assert.Contains(m_types.current.types, type => type.Resolve(m_types) == marked);
    }

    [Fact]
    public void UnknownAndEmptyReferencesFailAgainstExplicitCatalog()
    {
        TypeRef empty = default;
        TypeRef unknown = new(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"));

        Assert.False(empty.IsValid(m_types));
        Assert.False(unknown.IsValid(m_types));
        Assert.Throws<InvalidOperationException>(() => empty.Resolve(m_types));
        Assert.Throws<InvalidOperationException>(() => unknown.Resolve(m_types));
        Assert.Throws<ArgumentNullException>(() => m_types.TryGetTypeRef(null!, out _));
    }

    [Fact]
    public void CandidateActivationFailureRestoresEveryRegistrySnapshot()
    {
        using var first = new TransactionalTestRegistry(m_types);
        using var second = new TransactionalTestRegistry(m_types);
        first.Refresh();
        second.Refresh();
        int firstPrevious = first.snapshotId;
        int secondPrevious = second.snapshotId;
        second.failActivation = true;

        Assert.Throws<InvalidOperationException>(m_types.Rebuild);

        Assert.Equal(firstPrevious, first.snapshotId);
        Assert.Equal(secondPrevious, second.snapshotId);
        Assert.Equal(1, first.rollbackCount);
        Assert.Equal(1, second.rollbackCount);
        Assert.Contains(firstPrevious + 1, first.disposedSnapshotIds);
        Assert.Contains(secondPrevious + 1, second.disposedSnapshotIds);
    }

    [Fact]
    public void SnapshotCleanupFailureDoesNotUndoCommittedCandidate()
    {
        using var registry = new TransactionalTestRegistry(m_types);
        registry.Refresh();
        int previous = registry.snapshotId;
        registry.failSnapshotCleanup = true;

        m_types.Rebuild();

        Assert.Equal(previous + 1, registry.snapshotId);
        Assert.Equal(0, registry.rollbackCount);
        Assert.Equal(1, registry.cleanupFailureCount);
        Assert.Contains(previous, registry.disposedSnapshotIds);
    }

    [Fact]
    public void RegistryRetiredAfterCandidatePreparationIsExcludedFromActivation()
    {
        using var retiring = new TransactionalTestRegistry(m_types);
        using var survivor = new TransactionalTestRegistry(m_types);
        retiring.Refresh();
        survivor.Refresh();
        int retiringPrevious = retiring.snapshotId;
        int survivorPrevious = survivor.snapshotId;
        retiring.disposeOnActivation = survivor;

        m_types.Rebuild();

        Assert.Equal(retiringPrevious + 1, retiring.snapshotId);
        Assert.Contains(survivorPrevious, survivor.disposedSnapshotIds);
        Assert.Contains(survivorPrevious + 1, survivor.disposedSnapshotIds);
    }

    [Fact]
    public void DisposedCatalogRejectsQueriesWithoutAStaticFallback()
    {
        m_types.Dispose();

        Assert.Throws<InvalidOperationException>(() => _ = m_types.current);
        Assert.Throws<InvalidOperationException>(() => m_types.GetSubTypesOf<TestBase>());
    }

    private sealed class TransactionalTestRegistry(TypeCatalog types)
        : TypeRegistry<TransactionalTestRegistry.Snapshot>(types)
    {
        private int m_nextSnapshotId;

        internal bool failActivation;
        internal bool failSnapshotCleanup;
        internal IDisposable? disposeOnActivation;
        internal int rollbackCount;
        internal int cleanupFailureCount;
        internal int snapshotId;
        internal List<int> disposedSnapshotIds { get; } = [];

        protected override Snapshot Build(TypeCacheSnapshot types) => new(++m_nextSnapshotId);

        protected override void OnActivating(Snapshot? previous, Snapshot candidate)
        {
            snapshotId = candidate.id;
            disposeOnActivation?.Dispose();
            disposeOnActivation = null;
            if (failActivation)
                throw new InvalidOperationException("Injected registry activation failure.");
        }

        protected override void OnActivationRolledBack(Snapshot? previous, Snapshot candidate)
        {
            snapshotId = previous?.id ?? 0;
            rollbackCount++;
        }

        protected override void DisposeSnapshot(Snapshot snapshot)
        {
            disposedSnapshotIds.Add(snapshot.id);
            if (failSnapshotCleanup)
                throw new InvalidOperationException("Injected snapshot cleanup failure.");
        }

        protected override void OnCleanupFailed(string phase, Exception exception)
            => cleanupFailureCount++;

        internal sealed record Snapshot(int id);
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
