using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Inno.Assets;
using Inno.Assets.Pipeline;
using Inno.Extensibility.Modules;
using Inno.Core.Diagnostics;
using Inno.Core.Execution;
using Inno.Core.Identity;
using Inno.Core.Logging;
using Inno.Extensibility.Types;
using Inno.Core.Serialization;
using Inno.References;

using Xunit;

namespace Inno.Assets.Pipeline.Tests;

public sealed class AssetLoaderTests : IDisposable
{
    private readonly ModuleHost m_modules;
    private readonly TypeCatalog m_types;
    private readonly SerializationRegistry m_serialization;
    private readonly LogRouter m_logs = new();
    private readonly IdentityAllocator m_identities = new();
    private readonly DiagnosticHub m_diagnostics = new();
    private readonly IDisposable m_diagnosticScope;
    private readonly IDisposable m_identityScope;

    public AssetLoaderTests()
    {
        _ = typeof(PrivateConstructorAssetImporter);
        _ = typeof(TestBuildProcessor);
        m_identityScope = m_identities.EnterScope();
        m_diagnosticScope = m_diagnostics.EnterScope();
        m_modules = new ModuleHost(new ModuleHostOptions
        {
            cacheDirectory = Path.Combine(Path.GetTempPath(), "InnoAssetLoaderTests", "Assemblies")
        });
        m_types = new TypeCatalog(m_modules);
        m_serialization = new SerializationRegistry(m_types);
        SlowAssetImporter.Reset();
        ImporterConflictProbe.mode = ImporterConflictMode.None;
    }

    public void Dispose()
    {
        m_serialization.Dispose();
        m_types.Dispose();
        m_modules.Dispose();
        m_logs.Dispose();
        m_diagnosticScope.Dispose();
        m_identityScope.Dispose();
    }

    [Fact]
    public void ReadOnlyImportFailureDoesNotRewriteMetadataOrThrowAgainWhileRecordingFailure()
    {
        using TestWorkspace workspace = new();
        using TestWorkspace package = new();
        package.WriteText("README.md", "Documentation");
        byte[] metadata = m_serialization.Serialize(new MountedSourceMetadata
        {
            persistentId = Guid.NewGuid(),
            sourceKind = (int)AssetSourceKind.File,
            importerId = "tests.unavailable.importer"
        });
        System.IO.File.WriteAllBytes(package.SourcePath("README.md.imeta"), metadata);
        var source = new AssetSourceId("tests.readonly");
        using var loader = new AssetLoader(m_types, m_serialization, m_identities, m_diagnostics, m_logs,
            [new AssetSourceMount(AssetSourceId.project, workspace.assetRoot, false),
             new AssetSourceMount(source, package.assetRoot, true)], workspace.libraryRoot);

        Assert.False(loader.Import(new AssetPath(source, "README.md")));
        Assert.Equal(metadata, System.IO.File.ReadAllBytes(package.SourcePath("README.md.imeta")));
        Assert.True(loader.TryGetInfo(new AssetPath(source, "README.md"), out AssetInfo? failure));
        Assert.Equal(AssetImportStatus.Failed, failure!.status);
        Assert.Contains(failure.diagnostics, value => value.Contains("Read-only source metadata", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AssemblyCatalogPublishesAnIsolatedLoaderAndRollsBackWithoutTouchingLastGood(bool reject)
    {
        using TestWorkspace workspace = new();
        workspace.WriteText("value.hookasset", "previous");
        using var pipeline = new AssetPipeline(m_modules, m_types, m_serialization, m_identities,
            m_diagnostics, m_logs, new AssetPipelineOptions
            { assetRoot = workspace.assetRoot, libraryRoot = workspace.libraryRoot });
        HookAsset previous = pipeline.Load<HookAsset>(AssetPath.Project("value.hookasset"));
        byte[] previousMeta = System.IO.File.ReadAllBytes(workspace.SourcePath("value.hookasset.imeta"));
        var observer = new CatalogObserver();
        using IDisposable registration = m_modules.RegisterCatalogParticipant(observer);
        workspace.WriteText("value.hookasset", "candidate");
        workspace.WriteText("new.txt", "new candidate source");
        observer.prepare = () =>
        {
            Assert.Same(previous, m_identities.Get<AssetObject>(previous.identity.persistentId));
            Assert.Equal("previous", Encoding.UTF8.GetString(previous.runtimePayload.Span));
            Assert.False(pipeline.TryGetInfo(AssetPath.Project("new.txt"), out _));
            Assert.False(System.IO.File.Exists(workspace.SourcePath("new.txt.imeta")));
        };
        observer.activate = () =>
        {
            AssetObject published = m_identities.Get<AssetObject>(previous.identity.persistentId)!;
            Assert.NotSame(previous, published);
            Assert.Equal("candidate", Encoding.UTF8.GetString(published.runtimePayload.Span));
            Assert.Equal("previous", Encoding.UTF8.GetString(previous.runtimePayload.Span));
            Assert.False(System.IO.File.Exists(workspace.SourcePath("new.txt.imeta")));
            if (reject)
                throw new InvalidOperationException("Expected later participant rejection.");
        };
        if (reject)
        {
            Assert.Throws<InvalidOperationException>(m_modules.Rebuild);
            Assert.Same(previous, m_identities.Get<AssetObject>(previous.identity.persistentId));
            Assert.Equal(0, previous.unloadingCount);
            Assert.Equal("previous", Encoding.UTF8.GetString(previous.runtimePayload.Span));
            Assert.Equal(previousMeta, System.IO.File.ReadAllBytes(workspace.SourcePath("value.hookasset.imeta")));
            Assert.False(System.IO.File.Exists(workspace.SourcePath("new.txt.imeta")));
            Assert.False(pipeline.TryGetInfo(AssetPath.Project("new.txt"), out _));
        }
        else
        {
            m_modules.Rebuild();
            Assert.NotSame(previous, pipeline.Load<HookAsset>(previous.identity.persistentId));
            Assert.Equal(1, previous.unloadingCount);
            Assert.True(System.IO.File.Exists(workspace.SourcePath("new.txt.imeta")));
        }
        observer.prepare = null;
        observer.activate = null;
        if (reject)
        {
            m_modules.Rebuild();
            Assert.NotSame(previous, pipeline.Load<HookAsset>(previous.identity.persistentId));
            Assert.Equal(1, previous.unloadingCount);
            Assert.True(System.IO.File.Exists(workspace.SourcePath("new.txt.imeta")));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AssemblyCatalogJoinsAnExistingSourceCandidateWithoutTakingItsPublicationOwnership(bool reject)
    {
        using TestWorkspace workspace = new();
        workspace.WriteText("value.txt", "previous");
        using var pipeline = new AssetPipeline(m_modules, m_types, m_serialization, m_identities,
            m_diagnostics, m_logs, new AssetPipelineOptions
            { assetRoot = workspace.assetRoot, libraryRoot = workspace.libraryRoot });
        TextAsset previous = pipeline.Load<TextAsset>(AssetPath.Project("value.txt"));
        var observer = new CatalogObserver();
        using IDisposable registration = m_modules.RegisterCatalogParticipant(observer);
        workspace.WriteText("value.txt", "candidate");
        using AssetSourceMountTransaction candidate = pipeline.PrepareSourceMounts(pipeline.sourceMounts);
        TextAsset replacement = candidate.Load<TextAsset>(AssetPath.Project("value.txt"));
        observer.activate = () =>
        {
            Assert.Same(previous, m_identities.Get<AssetObject>(previous.identity.persistentId));
            if (reject) throw new InvalidOperationException("Expected joined candidate rejection.");
        };
        if (reject)
            Assert.Throws<InvalidOperationException>(m_modules.Rebuild);
        else
            m_modules.Rebuild();
        Assert.Same(previous, m_identities.Get<AssetObject>(previous.identity.persistentId));
        Assert.True(candidate.TryGetInfo(AssetPath.Project("value.txt"), out _));
        observer.activate = null;
        if (reject)
            candidate.Rollback();
        else
        {
            candidate.Activate();
            candidate.Complete();
            Assert.Equal("candidate", Encoding.UTF8.GetString(
                pipeline.Load<TextAsset>(replacement.identity.persistentId).runtimePayload.Span));
        }
    }

    [Fact]
    public void IntroducedImportFailureLeavesTheLiveAssetAndCatalogUntouched()
    {
        using TestWorkspace workspace = new();
        workspace.WriteText("value.mutableasset", "previous");
        using var pipeline = new AssetPipeline(m_modules, m_types, m_serialization, m_identities,
            m_diagnostics, m_logs, new AssetPipelineOptions
            { assetRoot = workspace.assetRoot, libraryRoot = workspace.libraryRoot });
        MutableAsset previous = pipeline.Load<MutableAsset>(AssetPath.Project("value.mutableasset"));
        byte[] sidecar = System.IO.File.ReadAllBytes(workspace.SourcePath("value.mutableasset.imeta"));
        workspace.WriteText("value.mutableasset", "!invalid!");
        Assert.Throws<InvalidDataException>(m_modules.Rebuild);
        Assert.Same(previous, m_identities.Get<AssetObject>(previous.identity.persistentId));
        Assert.Equal("previous", previous.value);
        Assert.True(pipeline.TryGetInfo(previous.identity.persistentId, out AssetInfo? info));
        Assert.Equal(AssetImportStatus.Imported, info!.status);
        Assert.Equal(sidecar, System.IO.File.ReadAllBytes(workspace.SourcePath("value.mutableasset.imeta")));
        workspace.WriteText("value.mutableasset", "recovered");
        m_modules.Rebuild();
        MutableAsset recovered = pipeline.Load<MutableAsset>(previous.identity.persistentId);
        Assert.NotSame(previous, recovered);
        Assert.Equal("recovered", recovered.value);
    }

    [Fact]
    public void CandidateMetadataRejectsExternalChangesWithoutOverwritingThem()
    {
        using TestWorkspace workspace = new();
        using var loader = workspace.CreateLoader(m_types, m_serialization, m_identities, m_diagnostics, m_logs);
        workspace.WriteText("new.txt", "candidate");
        using AssetCatalogCandidate candidate = loader.PrepareCatalogCandidate(
            [new AssetSourceMount(AssetSourceId.project, workspace.assetRoot, false)]);
        using AssetLoader candidateLoader = candidate.loader;
        candidateLoader.Rescan();
        string sidecarPath = workspace.SourcePath("new.txt.imeta");
        Assert.False(System.IO.File.Exists(sidecarPath));
        byte[] external = m_serialization.Serialize(new MountedSourceMetadata
        {
            persistentId = Guid.NewGuid(), sourceKind = (int)AssetSourceKind.File,
            importerId = "external.importer"
        });
        System.IO.File.WriteAllBytes(sidecarPath, external);
        Assert.Throws<IOException>(candidate.Commit);
        Assert.Equal(external, System.IO.File.ReadAllBytes(sidecarPath));
    }

    [Fact]
    public void CandidateMetadataCompensatesEarlierSidecarsWhenALaterWriteFails()
    {
        using TestWorkspace workspace = new();
        using var loader = workspace.CreateLoader(m_types, m_serialization, m_identities, m_diagnostics, m_logs);
        workspace.WriteText("a.txt", "first");
        workspace.WriteText("z.txt", "last");
        using AssetCatalogCandidate candidate = loader.PrepareCatalogCandidate(
            [new AssetSourceMount(AssetSourceId.project, workspace.assetRoot, false)]);
        using AssetLoader candidateLoader = candidate.loader;
        candidateLoader.Rescan();
        Directory.CreateDirectory(workspace.SourcePath("z.txt.imeta"));
        Assert.ThrowsAny<IOException>(candidate.Commit);
        Assert.False(System.IO.File.Exists(workspace.SourcePath("a.txt.imeta")));
        Assert.True(Directory.Exists(workspace.SourcePath("z.txt.imeta")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CandidateDiagnosticsPublishOnlyWithTheirSourceGenerationAndRetireWithoutErasingAnotherOwner(bool commit)
    {
        using TestWorkspace workspace = new();
        workspace.WriteText("previous.mutableasset", "!invalid!");
        var sink = new TestDiagnosticSink();
        m_diagnostics.RegisterSink(sink);
        using var pipeline = new AssetPipeline(m_modules, m_types, m_serialization, m_identities,
            m_diagnostics, m_logs, new AssetPipelineOptions
            { assetRoot = workspace.assetRoot, libraryRoot = workspace.libraryRoot });
        Assert.Single(sink.reports.Values);
        string previousSource = Assert.Single(sink.reports.Keys);
        workspace.WriteText("new.mutableasset", "!invalid!");
        using AssetSourceMountTransaction candidate = pipeline.PrepareSourceMounts(pipeline.sourceMounts);
        Assert.Equal(previousSource, Assert.Single(sink.reports.Keys));
        candidate.Activate();
        Assert.Equal(2, sink.reports.Count);
        if (commit)
        {
            candidate.Complete();
            Assert.Equal(2, sink.reports.Count);
            Assert.Contains(previousSource, sink.reports.Keys);
        }
        else
        {
            candidate.Rollback();
            Assert.Equal(previousSource, Assert.Single(sink.reports.Keys));
        }
        pipeline.Dispose();
        Assert.Empty(sink.reports);
        m_diagnostics.UnregisterSink(sink);
    }

    [Fact]
    public void CandidateCommitRestoresCatalogAndSidecarsWhenJournalRetirementFails()
    {
        using TestWorkspace workspace = new();
        workspace.WriteText("previous.txt", "last good");
        using var loader = workspace.CreateLoader(m_types, m_serialization, m_identities, m_diagnostics, m_logs);
        loader.Rescan();
        string snapshotPath = Path.Combine(workspace.libraryRoot, "AssetDatabase", "Catalog.snapshot");
        string journalPath = Path.Combine(workspace.libraryRoot, "AssetDatabase", "Catalog.journal");
        byte[] previous = System.IO.File.ReadAllBytes(snapshotPath);
        workspace.WriteText("new.txt", "candidate");
        using AssetCatalogCandidate candidate = loader.PrepareCatalogCandidate(
            [new AssetSourceMount(AssetSourceId.project, workspace.assetRoot, false)]);
        using AssetLoader candidateLoader = candidate.loader;
        candidateLoader.Rescan();
        Directory.CreateDirectory(journalPath);

        Exception? failure = Record.Exception(candidate.Commit);
        Assert.True(failure is IOException or UnauthorizedAccessException, failure?.ToString());
        Assert.Equal(previous, System.IO.File.ReadAllBytes(snapshotPath));
        Assert.False(System.IO.File.Exists(workspace.SourcePath("new.txt.imeta")));
        Assert.True(Directory.Exists(journalPath));
        Assert.False(loader.TryGetInfo(AssetPath.Project("new.txt"), out _));
    }

    [Fact]
    public void CandidateDiagnosticRecoveryCanRollBackToThePreviousUnresolvedReport()
    {
        using TestWorkspace workspace = new();
        workspace.WriteText("value.mutableasset", "!invalid!");
        var sink = new TestDiagnosticSink();
        m_diagnostics.RegisterSink(sink);
        using var pipeline = new AssetPipeline(m_modules, m_types, m_serialization, m_identities,
            m_diagnostics, m_logs, new AssetPipelineOptions
            { assetRoot = workspace.assetRoot, libraryRoot = workspace.libraryRoot });
        DiagnosticReport previous = Assert.Single(sink.reports.Values);
        workspace.WriteText("value.mutableasset", "fixed");
        using AssetSourceMountTransaction candidate = pipeline.PrepareSourceMounts(pipeline.sourceMounts);
        Assert.Same(previous, Assert.Single(sink.reports.Values));
        candidate.Activate();
        Assert.Empty(sink.reports);
        candidate.Rollback();
        DiagnosticReport restored = Assert.Single(sink.reports.Values);
        Assert.Equal(previous.source.id, restored.source.id);
        Assert.Equal(previous.diagnostics.Select(static issue => issue.message),
            restored.diagnostics.Select(static issue => issue.message));
        m_diagnostics.UnregisterSink(sink);
    }

    private sealed class CatalogObserver : IAssemblyCatalogParticipant
    {
        internal Action? prepare;
        internal Action? activate;

        public IAssemblyCatalogTransaction Prepare(AssemblyCatalogSnapshot catalog)
        {
            prepare?.Invoke();
            return new Observation(activate);
        }

        private sealed class Observation(Action? activate) : IAssemblyCatalogTransaction
        {
            public object? context => null;
            public void Activate() => activate?.Invoke();
            public void Complete() { }
            public void Rollback() { }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SourceCandidatePublishesCanonicalIdentitiesOnlyDuringSharedRecovery(bool commit)
    {
        using TestWorkspace workspace = new();
        workspace.WriteText("value.txt", "old");
        using var pipeline = new AssetPipeline(m_modules, m_types, m_serialization, m_identities,
            m_diagnostics, m_logs, new AssetPipelineOptions
            {
                assetRoot = workspace.assetRoot,
                libraryRoot = workspace.libraryRoot,
                enableFileSystemWatcher = false
            });
        TextAsset previous = pipeline.Load<TextAsset>(AssetPath.Project("value.txt"));
        RuntimeIdentity previousIdentity = previous.identity.runtimeIdentity!.Value;
        workspace.WriteText("value.txt", "candidate");
        using AssetSourceMountTransaction transaction = pipeline.PrepareSourceMounts(pipeline.sourceMounts);
        TextAsset candidate = transaction.Load<TextAsset>(AssetPath.Project("value.txt"));

        Assert.Equal(previous.identity.persistentId, candidate.identity.persistentId);
        Assert.Null(candidate.identity.runtimeId);
        Assert.Same(previous, m_identities.Get<AssetObject>(previousIdentity));
        Assert.Empty(transaction.recoveryChanges);
        transaction.Activate();
        ReferenceRecoveryChange recovered = Assert.Single(transaction.recoveryChanges);
        Assert.Equal(ReferenceResolutionState.Resolved, recovered.resolution.state);
        Assert.Equal(candidate.identity.runtimeIdentity, recovered.resolution.runtimeIdentity);
        Assert.Same(candidate, m_identities.Get<AssetObject>(previous.identity.persistentId));
        Assert.Null(m_identities.Get<AssetObject>(previousIdentity));

        if (commit)
        {
            transaction.Complete();
            Assert.Same(candidate, pipeline.Load<TextAsset>(candidate.identity.persistentId));
            Assert.NotNull(candidate.identity.runtimeId);
        }
        else
        {
            RuntimeIdentity candidateIdentity = candidate.identity.runtimeIdentity!.Value;
            transaction.Rollback();
            Assert.Same(previous, pipeline.Load<TextAsset>(previous.identity.persistentId));
            Assert.Same(previous, m_identities.Get<AssetObject>(previous.identity.persistentId));
            Assert.Null(m_identities.Get<AssetObject>(candidateIdentity));
            Assert.Empty(transaction.recoveryChanges);
        }
    }

    [Fact]
    public void SourceCandidateIdentityConflictRestoresThePreviousDomain()
    {
        using TestWorkspace workspace = new();
        workspace.WriteText("previous.txt", "old");
        using var pipeline = new AssetPipeline(m_modules, m_types, m_serialization, m_identities,
            m_diagnostics, m_logs, new AssetPipelineOptions
            { assetRoot = workspace.assetRoot, libraryRoot = workspace.libraryRoot });
        TextAsset previous = pipeline.Load<TextAsset>(AssetPath.Project("previous.txt"));
        workspace.WriteText("candidate.txt", "new");
        using AssetSourceMountTransaction transaction = pipeline.PrepareSourceMounts(pipeline.sourceMounts);
        TextAsset candidate = transaction.Load<TextAsset>(AssetPath.Project("candidate.txt"));
        var unrelated = new IdentityConflict();
        m_identities.Register(unrelated, candidate.identity.persistentId);
        try
        {
            Assert.Throws<InvalidOperationException>(transaction.Activate);
            Assert.Same(previous, m_identities.Get<AssetObject>(previous.identity.persistentId));
            Assert.Same(unrelated, m_identities.Get<IdentityObject>(candidate.identity.persistentId));
            transaction.Rollback();
            Assert.Same(previous, pipeline.Load<TextAsset>(previous.identity.persistentId));
        }
        finally { m_identities.Unregister(unrelated); }
    }

    [Fact]
    public void SourceRemovalAndReturnKeepTheSameRecoverySlotAndNeutralState()
    {
        using TestWorkspace workspace = new();
        workspace.WriteText("value.txt", "preserved");
        using var pipeline = new AssetPipeline(m_modules, m_types, m_serialization, m_identities,
            m_diagnostics, m_logs, new AssetPipelineOptions
            { assetRoot = workspace.assetRoot, libraryRoot = workspace.libraryRoot });
        TextAsset previous = pipeline.Load<TextAsset>(AssetPath.Project("value.txt"));
        Guid id = previous.identity.persistentId;
        byte[] metadata = System.IO.File.ReadAllBytes(workspace.SourcePath("value.txt.imeta"));
        System.IO.File.Delete(workspace.SourcePath("value.txt"));
        using AssetSourceMountTransaction removal = pipeline.PrepareSourceMounts(pipeline.sourceMounts);
        removal.Activate();
        ReferenceRecoveryChange missing = Assert.Single(removal.recoveryChanges);
        Assert.Equal(id, missing.resolution.descriptor.targetPersistentId);
        Assert.Equal(ReferenceResolutionState.Missing, missing.resolution.state);
        removal.Complete();
        workspace.WriteText("value.txt", "preserved");
        System.IO.File.WriteAllBytes(workspace.SourcePath("value.txt.imeta"), metadata);
        using AssetSourceMountTransaction recovery = pipeline.PrepareSourceMounts(pipeline.sourceMounts);
        recovery.Activate();
        ReferenceRecoveryChange restored = Assert.Single(recovery.recoveryChanges);
        Assert.Equal(missing.missingState.key, restored.missingState.key);
        Assert.Equal(missing.missingState.payload.ToArray(), restored.missingState.payload.ToArray());
        Assert.Equal(ReferenceResolutionState.Resolved, restored.resolution.state);
        recovery.Complete();
        Assert.Equal(id, pipeline.Load<TextAsset>(AssetPath.Project("value.txt")).identity.persistentId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SourceRetirementDrainsPendingHooksBeforeCompletingTheTransaction(bool commit)
    {
        using TestWorkspace workspace = new();
        workspace.WriteText("value.hookasset", "previous");
        using var pipeline = new AssetPipeline(m_modules, m_types, m_serialization, m_identities,
            m_diagnostics, m_logs, new AssetPipelineOptions
            { assetRoot = workspace.assetRoot, libraryRoot = workspace.libraryRoot });
        HookAsset previous = pipeline.Load<HookAsset>(AssetPath.Project("value.hookasset"));
        workspace.WriteText("value.hookasset", "candidate");
        using AssetSourceMountTransaction transaction = pipeline.PrepareSourceMounts(pipeline.sourceMounts);
        HookAsset candidate = transaction.Load<HookAsset>(AssetPath.Project("value.hookasset"));
        HookAsset retiring = commit ? previous : candidate;
        HookAsset retained = commit ? candidate : previous;
        retiring.pendingUnloads = 2;
        transaction.Activate();

        if (commit) transaction.Complete();
        else transaction.Rollback();

        Assert.Equal(3, retiring.unloadingCount);
        Assert.True(retiring.runtimePayload.IsEmpty);
        Assert.Null(retiring.identity.runtimeId);
        Assert.Same(retained, pipeline.Load<HookAsset>(retained.identity.persistentId));
        Assert.Same(retained, m_identities.Get<AssetObject>(retained.identity.persistentId));
        Assert.Equal(0, retained.unloadingCount);
        Assert.False(retained.runtimePayload.IsEmpty);
        using AssetSourceMountTransaction next = pipeline.PrepareSourceMounts(pipeline.sourceMounts);
    }

    [Fact]
    public void LoaderRetirementRetainsPendingAssetPayloadAndRegistration()
    {
        using TestWorkspace workspace = new();
        workspace.WriteText("value.hookasset", "payload");
        var loader = workspace.CreateLoader(m_types, m_serialization, m_identities, m_diagnostics, m_logs);
        HookAsset asset = Assert.IsType<HookAsset>(loader.Load(AssetPath.Project("value.hookasset"), typeof(HookAsset)));
        RuntimeIdentity identity = asset.identity.runtimeIdentity!.Value;
        asset.pendingUnloads = 1;

        Assert.Throws<RetirementPendingException>(loader.Dispose);
        Assert.Same(asset, m_identities.Get<AssetObject>(identity));
        Assert.Equal("payload", Encoding.UTF8.GetString(asset.runtimePayload.Span));
        Assert.Throws<ObjectDisposedException>(() => loader.Load(AssetPath.Project("value.hookasset"), typeof(HookAsset)));
        loader.Dispose();
        loader.Dispose();
        Assert.Null(m_identities.Get<AssetObject>(identity));
        Assert.Equal(2, asset.unloadingCount);
        Assert.True(asset.runtimePayload.IsEmpty);
    }

    [Fact]
    public void PipelineShutdownDrainsCanonicalAssetsBeforeReleasingTheirOwners()
    {
        using TestWorkspace workspace = new();
        workspace.WriteText("value.hookasset", "payload");
        var pipeline = new AssetPipeline(m_modules, m_types, m_serialization, m_identities,
            m_diagnostics, m_logs, new AssetPipelineOptions
            { assetRoot = workspace.assetRoot, libraryRoot = workspace.libraryRoot });
        HookAsset asset = pipeline.Load<HookAsset>(AssetPath.Project("value.hookasset"));
        asset.pendingUnloads = 1;
        pipeline.Dispose();
        pipeline.Dispose();
        Assert.Equal(2, asset.unloadingCount);
        Assert.Null(asset.identity.runtimeId);
        Assert.False(pipeline.isInitialized);
    }

    [Fact]
    public void LoaderRetirementKeepsEarlierFailuresAcrossPendingResources()
    {
        using TestWorkspace workspace = new();
        workspace.WriteText("a.hookasset", "pending");
        workspace.WriteText("b.hookasset", "failure");
        var loader = workspace.CreateLoader(m_types, m_serialization, m_identities, m_diagnostics, m_logs);
        HookAsset pending = Assert.IsType<HookAsset>(loader.Load(AssetPath.Project("a.hookasset"), typeof(HookAsset)));
        HookAsset failing = Assert.IsType<HookAsset>(loader.Load(AssetPath.Project("b.hookasset"), typeof(HookAsset)));
        pending.pendingUnloads = 1;
        failing.failUnloading = true;

        Assert.Throws<RetirementPendingException>(loader.Dispose);
        AggregateException failure = Assert.Throws<AggregateException>(loader.Dispose);
        Assert.Contains("The test asset release failed.", failure.ToString(), StringComparison.Ordinal);
        loader.Dispose();
        Assert.Equal(2, pending.unloadingCount);
        Assert.Equal(1, failing.unloadingCount);
        Assert.Null(pending.identity.runtimeId);
        Assert.Null(failing.identity.runtimeId);
    }

    [Fact]
    public void LoaderShutdownIncludesCanonicalTombstones()
    {
        using TestWorkspace workspace = new();
        workspace.WriteText("value.hookasset", "payload");
        var loader = workspace.CreateLoader(m_types, m_serialization, m_identities, m_diagnostics, m_logs);
        HookAsset asset = Assert.IsType<HookAsset>(loader.Load(AssetPath.Project("value.hookasset"), typeof(HookAsset)));
        System.IO.File.Delete(workspace.SourcePath("value.hookasset"));
        loader.Rescan();
        Assert.True(asset.isMissing);
        loader.Dispose();
        Assert.Equal(1, asset.unloadingCount);
        Assert.Null(asset.identity.runtimeId);
    }

    [Fact]
    public async Task LoaderDisposalDoesNotBlockAnInFlightLoadOrAdmitNewWork()
    {
        using TestWorkspace workspace = new();
        workspace.WriteText("value.slowasset", "payload");
        var loader = workspace.CreateLoader(m_types, m_serialization, m_identities, m_diagnostics, m_logs);
        Task<AssetObject?> loading = loader.LoadAsync(AssetPath.Project("value.slowasset"), typeof(SlowAsset)).AsTask();
        try
        {
            Assert.True(SlowAssetImporter.importStarted.Wait(TimeSpan.FromSeconds(3)));
            Assert.Throws<RetirementPendingException>(loader.Dispose);
            Assert.Throws<ObjectDisposedException>(() => loader.LoadAsync(AssetPath.Project("value.slowasset"), typeof(SlowAsset)));
        }
        finally { SlowAssetImporter.allowImport.Set(); }
        AssetObject asset = Assert.IsType<SlowAsset>(await loading);
        Assert.NotNull(asset.identity.runtimeId);
        loader.Dispose();
        Assert.Null(asset.identity.runtimeId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SourceRetirementTimeoutFaultsAdmissionAndKeepsBothGenerationsOwned(bool commit)
    {
        using TestWorkspace workspace = new();
        workspace.WriteText("value.hookasset", "previous");
        var modules = new ModuleHost(new ModuleHostOptions
        { cacheDirectory = Path.Combine(workspace.libraryRoot, "FaultedModules") });
        var types = new TypeCatalog(modules);
        var serialization = new SerializationRegistry(types);
        var pipeline = new AssetPipeline(modules, types, serialization, m_identities,
            m_diagnostics, m_logs, new AssetPipelineOptions
            { assetRoot = workspace.assetRoot, libraryRoot = workspace.libraryRoot });
        HookAsset previous = pipeline.Load<HookAsset>(AssetPath.Project("value.hookasset"));
        workspace.WriteText("value.hookasset", "candidate");
        AssetSourceMountTransaction transaction = pipeline.PrepareSourceMounts(pipeline.sourceMounts);
        HookAsset candidate = transaction.Load<HookAsset>(AssetPath.Project("value.hookasset"));
        HookAsset retiring = commit ? previous : candidate;
        HookAsset retained = commit ? candidate : previous;
        retiring.retirementExpired = true;
        transaction.Activate();

        if (commit) Assert.Throws<RetirementTimeoutException>(transaction.Complete);
        else Assert.Throws<RetirementTimeoutException>(transaction.Rollback);

        Assert.False(retiring.runtimePayload.IsEmpty);
        Assert.False(retained.runtimePayload.IsEmpty);
        Assert.Equal(0, retained.unloadingCount);
        Assert.ThrowsAny<RetirementPendingException>(() => pipeline.Load<HookAsset>(retained.identity.persistentId));
        retiring.retirementExpired = false;
        Assert.ThrowsAny<RetirementPendingException>(transaction.Dispose);
        Assert.ThrowsAny<RetirementPendingException>(pipeline.Dispose);
        Assert.ThrowsAny<RetirementPendingException>(types.Dispose);
        Assert.ThrowsAny<RetirementPendingException>(modules.Dispose);
        Assert.Equal(1, retiring.unloadingCount);
        Assert.Equal(0, retained.unloadingCount);
        GC.KeepAlive(serialization);
    }

    [Fact]
    public void RuntimeDatabaseRetainsPendingPayloadAndRegistrationUntilShutdownCompletes()
    {
        using TestWorkspace workspace = new();
        workspace.WriteText("value.hookasset", "runtime payload");
        using var loader = workspace.CreateLoader(m_types, m_serialization, m_identities, m_diagnostics, m_logs);
        Assert.NotNull(loader.Load(AssetPath.Project("value.hookasset"), typeof(HookAsset)));
        string contentRoot = Path.Combine(workspace.libraryRoot, "Runtime");
        loader.ExportRuntimeArtifacts(contentRoot);
        using SerializationGeneration serialization = m_serialization.CaptureGeneration();
        var identities = new IdentityAllocator();
        var database = new AssetDatabase(contentRoot, serialization, m_types.current, identities);
        HookAsset asset = database.Load<HookAsset>(AssetPath.Project("value.hookasset"));
        RuntimeIdentity identity = asset.identity.runtimeIdentity!.Value;
        asset.pendingUnloads = 1;

        Assert.Throws<RetirementPendingException>(database.Dispose);
        Assert.Same(asset, identities.Get<AssetObject>(identity));
        Assert.Equal("runtime payload", Encoding.UTF8.GetString(asset.runtimePayload.Span));
        Assert.Throws<ObjectDisposedException>(() => database.Load<HookAsset>(asset.identity.persistentId));
        database.Dispose();
        database.Dispose();
        Assert.Null(identities.Get<AssetObject>(identity));
        Assert.Equal(2, asset.unloadingCount);
        Assert.True(asset.runtimePayload.IsEmpty);
    }

    [Fact]
    public async Task RuntimeLeaseRetriesPendingBudgetEvictionWithoutReleasingAnotherLease()
    {
        using TestWorkspace workspace = new();
        workspace.WriteText("value.hookasset", "leased payload");
        using var loader = workspace.CreateLoader(m_types, m_serialization, m_identities, m_diagnostics, m_logs);
        Assert.NotNull(loader.Load(AssetPath.Project("value.hookasset"), typeof(HookAsset)));
        string contentRoot = Path.Combine(workspace.libraryRoot, "Runtime");
        loader.ExportRuntimeArtifacts(contentRoot);
        using SerializationGeneration serialization = m_serialization.CaptureGeneration();
        var identities = new IdentityAllocator();
        using var database = new AssetDatabase(contentRoot, serialization, m_types.current, identities,
            residencyBudgetBytes: 0);
        Task<AssetLease<HookAsset>> pending = database.AcquireAsync<HookAsset>(
            AssetPath.Project("value.hookasset")).AsTask();
        Assert.True(SpinWait.SpinUntil(() =>
        {
            database.CompletePendingLoads();
            return pending.IsCompleted;
        }, TimeSpan.FromSeconds(5)));
        using AssetLease<HookAsset> first = await pending;
        HookAsset asset = first.asset;
        RuntimeIdentity identity = asset.identity.runtimeIdentity!.Value;
        using AssetLease<HookAsset> last = await database.AcquireAsync<HookAsset>(asset.identity.persistentId);
        asset.pendingUnloads = 1;
        first.Dispose();
        first.Dispose();
        Assert.Equal(0, asset.unloadingCount);
        Assert.Same(asset, last.asset);

        Assert.Throws<RetirementPendingException>(last.Dispose);
        Assert.Same(asset, last.asset);
        Assert.Same(asset, identities.Get<AssetObject>(identity));
        Assert.Equal("leased payload", Encoding.UTF8.GetString(asset.runtimePayload.Span));
        last.Dispose();
        last.Dispose();
        Assert.Equal(2, asset.unloadingCount);
        Assert.Equal(0, database.residencyStatistics.residentAssetCount);
        Assert.Null(identities.Get<AssetObject>(identity));
        Assert.Throws<ObjectDisposedException>(() => last.asset);
    }

    private sealed class IdentityConflict : IdentityObject;

    private sealed class MountedSourceMetadata : ISerializable
    {
        [SerializableProperty] public Guid persistentId { get; set; }
        [SerializableProperty] public int sourceKind { get; set; }
        [SerializableProperty] public string importerId { get; set; } = string.Empty;
        [SerializableProperty] public byte[] importerSettingsBytes { get; set; } = [];
    }

    [Fact]
    public void MissingReferenceNeverBindsAnotherIdentityAtItsLastKnownPath()
    {
        using TestWorkspace workspace = new();
        workspace.WriteText("Text/replaced.txt", "replacement");
        using var loader = workspace.CreateLoader(m_types, m_serialization, m_identities, m_diagnostics, m_logs);
        var replacement = Assert.IsType<TextAsset>(loader.Load(AssetPath.Project("Text/replaced.txt"), typeof(TextAsset)));
        Guid intended = Guid.NewGuid();
        Assert.True(m_types.TryGetTypeRef(typeof(TextAsset), out TypeRef type));

        AssetObject missing = loader.ResolveReference(intended, type.stableId, "Text/replaced.txt", typeof(TextAsset));

        Assert.True(missing.isMissing);
        Assert.Equal(intended, missing.identity.persistentId);
        Assert.NotSame(replacement, missing);
        Assert.Same(missing, loader.ResolveReference(intended, type.stableId, "Text/replaced.txt", typeof(TextAsset)));
    }

    [Fact]
    public void ExtensionContractsUseImplementationIdentityWithoutManualVersions()
    {
        Assert.Null(typeof(AssetImporter).GetProperty("version"));
        Assert.Null(typeof(AssetBuildProcessor).GetProperty("version"));
    }

    [Fact]
    public void Import_WritesCacheWithoutCreatingCanonicalInstance()
    {
        using TestWorkspace workspace = new();
        workspace.WriteText("Config/game.txt", "one");
        using var loader = workspace.CreateLoader(m_types, m_serialization, m_identities, m_diagnostics, m_logs);

        Assert.True(loader.Import(AssetPath.Project("Config/game.txt")));
        Assert.True(System.IO.File.Exists(workspace.SourcePath("Config/game.txt.imeta")));
        Assert.True(loader.TryGetPersistentId(AssetPath.Project("Config/game.txt"), out Guid persistentId));
        Assert.NotEqual(Guid.Empty, persistentId);
        Assert.True(loader.TryGetArtifact(persistentId, "runtime", out AssetArtifactInfo? artifact));
        Assert.NotNull(artifact);
        Assert.True(System.IO.File.Exists(artifact.absolutePath));
        Assert.True(loader.TryGetAssetType(AssetPath.Project("Config/game.txt"), out Type? assetType));
        Assert.Equal(typeof(TextAsset), assetType);
        Assert.Empty(loader.GetLoadedPaths());
    }

    [Fact]
    public void AutomaticallyDiscoveredPrivateImporter_LoadsWithoutRegistrationApi()
    {
        using TestWorkspace workspace = new();
        workspace.WriteText("Private/item.privateasset", "private");
        using var loader = workspace.CreateLoader(m_types, m_serialization, m_identities, m_diagnostics, m_logs);

        PrivateConstructorAsset asset = Assert.IsType<PrivateConstructorAsset>(
            loader.Load(AssetPath.Project("Private/item.privateasset"), typeof(PrivateConstructorAsset)));

        Assert.Equal("private", asset.value);
        m_types.Rebuild();
        Assert.Same(asset, loader.Load(AssetPath.Project("Private/item.privateasset"), typeof(PrivateConstructorAsset)));
    }

    [Fact]
    public void SameAssetType_CanUseMultipleImportersForDifferentExtensions()
    {
        using TestWorkspace workspace = new();
        workspace.WriteText("Graphs/a.depgraph", string.Empty);
        workspace.WriteText("Graphs/b.depgraph2", string.Empty);
        using var loader = workspace.CreateLoader(m_types, m_serialization, m_identities, m_diagnostics, m_logs);

        Assert.IsType<DependencyAsset>(loader.Load(AssetPath.Project("Graphs/a.depgraph"), typeof(DependencyAsset)));
        Assert.IsType<DependencyAsset>(loader.Load(AssetPath.Project("Graphs/b.depgraph2"), typeof(DependencyAsset)));
    }

    [Fact]
    public void DuplicateImporterId_IsRejectedDuringAutomaticDiscovery()
    {
        using TestWorkspace workspace = new();
        workspace.WriteText("Conflict/value.probea", "value");
        using var loader = workspace.CreateLoader(m_types, m_serialization, m_identities, m_diagnostics, m_logs);
        ImporterConflictProbe.mode = ImporterConflictMode.DuplicateId;

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => loader.Import(AssetPath.Project("Conflict/value.probea")));

        Assert.Contains("importer id", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DuplicateImporterExtension_IsRejectedDuringAutomaticDiscovery()
    {
        using TestWorkspace workspace = new();
        workspace.WriteText("Conflict/value.conflict", "value");
        using var loader = workspace.CreateLoader(m_types, m_serialization, m_identities, m_diagnostics, m_logs);
        ImporterConflictProbe.mode = ImporterConflictMode.DuplicateExtension;

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => loader.Import(AssetPath.Project("Conflict/value.conflict")));

        Assert.Contains("extension", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PathAndPersistentIdLoads_ReturnCanonicalInstance()
    {
        using TestWorkspace workspace = new();
        workspace.WriteText("Text/shared.txt", "shared");
        using var loader = workspace.CreateLoader(m_types, m_serialization, m_identities, m_diagnostics, m_logs);

        TextAsset byPath = Assert.IsType<TextAsset>(loader.Load(AssetPath.Project("Text/shared.txt"), typeof(TextAsset)));
        TextAsset byId = Assert.IsType<TextAsset>(loader.Load(byPath.identity.persistentId, typeof(TextAsset)));

        Assert.Same(byPath, byId);
        Assert.Equal(new[] { AssetPath.Project("Text/shared.txt") }, loader.GetLoadedPaths());
    }

    [Fact]
    public void CurrentCatalogStamp_LoadsArtifactWithoutOpeningSourceContent()
    {
        using TestWorkspace workspace = new();
        workspace.WriteText("Text/cached.txt", "cached");
        using (AssetLoader first = workspace.CreateLoader(m_types, m_serialization, m_identities, m_diagnostics, m_logs))
            first.Rescan();
        using AssetLoader loader = workspace.CreateLoader(m_types, m_serialization, m_identities, m_diagnostics, m_logs);
        loader.Rescan();
        using var sourceLock = new FileStream(
            workspace.SourcePath("Text/cached.txt"),
            FileMode.Open,
            FileAccess.ReadWrite,
            FileShare.None);

        TextAsset asset = Assert.IsType<TextAsset>(
            loader.Load(AssetPath.Project("Text/cached.txt"), typeof(TextAsset)));

        Assert.Equal("cached", asset.content);
    }

    [Fact]
    public void ChangedStampWithEqualContent_DoesNotReimportCanonicalAsset()
    {
        using TestWorkspace workspace = new();
        workspace.WriteText("Text/touched.txt", "value");
        using var loader = workspace.CreateLoader(m_types, m_serialization, m_identities, m_diagnostics, m_logs);
        TextAsset before = Assert.IsType<TextAsset>(
            loader.Load(AssetPath.Project("Text/touched.txt"), typeof(TextAsset)));
        long version = before.contentVersion;
        string sourcePath = workspace.SourcePath("Text/touched.txt");
        System.IO.File.SetLastWriteTimeUtc(
            sourcePath,
            System.IO.File.GetLastWriteTimeUtc(sourcePath).AddSeconds(1));

        TextAsset after = Assert.IsType<TextAsset>(
            loader.Load(AssetPath.Project("Text/touched.txt"), typeof(TextAsset)));

        Assert.Same(before, after);
        Assert.Equal(version, after.contentVersion);
    }

    [Fact]
    public void CurrentDependencyStamp_DoesNotOpenSourceDependencyContent()
    {
        using TestWorkspace workspace = new();
        workspace.WriteText("Import/schema.inc", "schema");
        workspace.WriteText("Import/root.importgraph", "Import/schema.inc");
        using var loader = workspace.CreateLoader(m_types, m_serialization, m_identities, m_diagnostics, m_logs);
        Assert.True(loader.Import(AssetPath.Project("Import/root.importgraph")));
        using var sourceLock = new FileStream(
            workspace.SourcePath("Import/schema.inc"),
            FileMode.Open,
            FileAccess.ReadWrite,
            FileShare.None);

        ImportGraphAsset asset = Assert.IsType<ImportGraphAsset>(
            loader.Load(AssetPath.Project("Import/root.importgraph"), typeof(ImportGraphAsset)));

        Assert.False(asset.isMissing);
    }

    [Fact]
    public async Task ConcurrentAsyncLoads_ShareImportAndCancellationOnlyCancelsOneWaiter()
    {
        using TestWorkspace workspace = new();
        workspace.WriteText("Slow/item.slowasset", "value");
        using var loader = workspace.CreateLoader(m_types, m_serialization, m_identities, m_diagnostics, m_logs);
        using var cancellation = new CancellationTokenSource();

        ValueTask<AssetObject?> first = loader.LoadAsync(AssetPath.Project("Slow/item.slowasset"), typeof(SlowAsset));
        Assert.True(SlowAssetImporter.importStarted.Wait(TimeSpan.FromSeconds(3)));
        ValueTask<AssetObject?> second = loader.LoadAsync(
            AssetPath.Project("Slow/item.slowasset"),
            typeof(SlowAsset),
            cancellation.Token);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await second);
        SlowAssetImporter.allowImport.Set();
        SlowAsset loaded = Assert.IsType<SlowAsset>(await first);

        Assert.Equal("value", loaded.value);
        Assert.Equal(1, SlowAssetImporter.importCount);
    }

    [Fact]
    public void StaleSource_ReimportsAndUpdatesCanonicalInstanceInPlace()
    {
        using TestWorkspace workspace = new();
        workspace.WriteText("Text/reload.txt", "one");
        using var loader = workspace.CreateLoader(m_types, m_serialization, m_identities, m_diagnostics, m_logs);
        TextAsset before = Assert.IsType<TextAsset>(loader.Load(AssetPath.Project("Text/reload.txt"), typeof(TextAsset)));
        Guid persistentId = before.identity.persistentId;
        long version = before.contentVersion;

        workspace.WriteText("Text/reload.txt", "two");
        TextAsset after = Assert.IsType<TextAsset>(loader.Load(AssetPath.Project("Text/reload.txt"), typeof(TextAsset)));

        Assert.Same(before, after);
        Assert.Equal(persistentId, after.identity.persistentId);
        Assert.Equal("two", after.content);
        Assert.Equal(version + 1, after.contentVersion);
    }

    [Fact]
    public void RuntimeDependencyCycle_IsAllowedAndLoadsAsOneCanonicalSubgraph()
    {
        using TestWorkspace workspace = new();
        workspace.WriteText("Graphs/a.depgraph", "Graphs/b.depgraph");
        workspace.WriteText("Graphs/b.depgraph", "Graphs/a.depgraph");
        using var loader = workspace.CreateLoader(m_types, m_serialization, m_identities, m_diagnostics, m_logs);

        DependencyAsset root = Assert.IsType<DependencyAsset>(
            loader.Load(AssetPath.Project("Graphs/a.depgraph"), typeof(DependencyAsset)));
        IReadOnlyList<AssetDependency> direct = loader.GetDependencies(root);
        IReadOnlyList<AssetDependency> recursive = loader.GetDependencies(root, recursive: true);

        Assert.Single(direct);
        Assert.Single(recursive);
        Assert.Equal("Graphs/b.depgraph", direct[0].lastKnownPath);
        Assert.Equal(2, loader.GetLoadedPaths().Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ColdRootsShareDependencyIoAndRetireReservationsAcrossRepeatedCycles(bool cancelFirst)
    {
        using TestWorkspace workspace = new();
        workspace.WriteText("Shared.txt", new string('s', 65536));
        workspace.WriteText("A.depgraph", "Shared.txt");
        workspace.WriteText("B.depgraph", "Shared.txt");
        using var loader = workspace.CreateLoader(m_types, m_serialization, m_identities, m_diagnostics, m_logs);
        Assert.NotNull(loader.Load(AssetPath.Project("A.depgraph"), typeof(DependencyAsset)));
        Assert.NotNull(loader.Load(AssetPath.Project("B.depgraph"), typeof(DependencyAsset)));
        string contentRoot = Path.Combine(workspace.libraryRoot, "Runtime");
        loader.ExportRuntimeArtifacts(contentRoot);
        using SerializationGeneration serialization = m_serialization.CaptureGeneration();
        using var database = new AssetDatabase(contentRoot, serialization, m_types.current,
            new IdentityAllocator(), residencyBudgetBytes: 0, preparationBudgetBytes: 100000);
        for (int cycle = 0; cycle < 32; cycle++)
        {
            using var cancellation = new CancellationTokenSource();
            Task<AssetLease<DependencyAsset>> first = database.AcquireAsync<DependencyAsset>(
                AssetPath.Project("A.depgraph"), cancellation.Token).AsTask();
            long firstBytes = database.preparingBytes;
            Task<AssetLease<DependencyAsset>> second = database.AcquireAsync<DependencyAsset>(
                AssetPath.Project("B.depgraph")).AsTask();
            Assert.Equal(3, database.preparationStatistics.pendingPayloads);
            Assert.True(database.preparingBytes < firstBytes * 2);
            if (cancelFirst)
                cancellation.Cancel();
            Assert.True(SpinWait.SpinUntil(() =>
            {
                database.CompletePendingLoads();
                return first.IsCompleted && second.IsCompleted;
            }, TimeSpan.FromSeconds(5)));
            if (cancelFirst)
                Assert.True(first.IsCanceled);
            else
                (await first).Dispose();
            (await second).Dispose();
            Assert.Equal(0, database.preparationStatistics.pendingPayloads);
            Assert.Equal(0, database.preparingBytes);
            Assert.Equal(0, database.preparationStatistics.pendingRequests);
            Assert.Equal(0, database.residencyStatistics.residentAssetCount);
        }
        AssetPreparationStatistics statistics = database.preparationStatistics;
        Assert.Equal(96, statistics.payloadReadsStarted);
        Assert.Equal(32, statistics.sharedPayloadReads);
        Assert.Equal(2, statistics.peakPendingRequests);
        Assert.InRange(statistics.peakReservedBytes, 65536, 100000);
        Assert.Equal(0, statistics.rejectedRequests);
    }

    [Fact]
    public void ColdAdmissionCountersRemainBoundedAndDisposeDrainsEveryCanceledWaiter()
    {
        using TestWorkspace workspace = new();
        workspace.WriteText("Shared.txt", "value");
        using var loader = workspace.CreateLoader(m_types, m_serialization, m_identities, m_diagnostics, m_logs);
        Assert.NotNull(loader.Load(AssetPath.Project("Shared.txt"), typeof(TextAsset)));
        string contentRoot = Path.Combine(workspace.libraryRoot, "Runtime");
        loader.ExportRuntimeArtifacts(contentRoot);
        using SerializationGeneration serialization = m_serialization.CaptureGeneration();
        using var database = new AssetDatabase(contentRoot, serialization, m_types.current, new IdentityAllocator());
        Task<AssetLease<TextAsset>>[] requests = Enumerable.Range(0, 32).Select(_ =>
            database.AcquireAsync<TextAsset>(AssetPath.Project("Shared.txt")).AsTask()).ToArray();
        Assert.Throws<InvalidOperationException>(() => database.AcquireAsync<TextAsset>(AssetPath.Project("Shared.txt")));
        Assert.Equal(32, database.preparationStatistics.pendingRequests);
        Assert.Equal(32, database.preparationStatistics.peakPendingRequests);
        Assert.Equal(1, database.preparationStatistics.rejectedRequests);
        Assert.Equal(1, database.preparationStatistics.payloadReadsStarted);
        database.Dispose();
        Assert.All(requests, request => Assert.True(request.IsCanceled));
        Assert.Equal(0, database.preparationStatistics.pendingPayloads);
        Assert.Equal(0, database.preparationStatistics.pendingRequests);
        Assert.Equal(0, database.preparationStatistics.reservedBytes);
    }

    [Fact]
    public void CorruptSharedPayloadFailsBothRootsWithoutLeakingReservationsOrPublishingAssets()
    {
        using TestWorkspace workspace = new();
        workspace.WriteText("Shared.txt", "payload");
        workspace.WriteText("A.depgraph", "Shared.txt");
        workspace.WriteText("B.depgraph", "Shared.txt");
        using var loader = workspace.CreateLoader(m_types, m_serialization, m_identities, m_diagnostics, m_logs);
        Assert.NotNull(loader.Load(AssetPath.Project("A.depgraph"), typeof(DependencyAsset)));
        Assert.NotNull(loader.Load(AssetPath.Project("B.depgraph"), typeof(DependencyAsset)));
        AssetObject shared = Assert.IsType<TextAsset>(loader.Load(AssetPath.Project("Shared.txt"), typeof(TextAsset)));
        string contentRoot = Path.Combine(workspace.libraryRoot, "Runtime");
        loader.ExportRuntimeArtifacts(contentRoot);
        using SerializationGeneration serialization = m_serialization.CaptureGeneration();
        using var database = new AssetDatabase(contentRoot, serialization, m_types.current, new IdentityAllocator());
        using (ArtifactLease artifact = database.AcquireArtifact(shared.identity.persistentId, "runtime"))
        {
            string path = artifact.info.absolutePath;
            byte[] bytes = System.IO.File.ReadAllBytes(path);
            bytes[0] ^= 0xff;
            System.IO.File.WriteAllBytes(path, bytes);
        }
        Task<AssetLease<DependencyAsset>> first = database.AcquireAsync<DependencyAsset>(AssetPath.Project("A.depgraph")).AsTask();
        Task<AssetLease<DependencyAsset>> second = database.AcquireAsync<DependencyAsset>(AssetPath.Project("B.depgraph")).AsTask();
        Assert.True(SpinWait.SpinUntil(() =>
        {
            database.CompletePendingLoads();
            return first.IsCompleted && second.IsCompleted;
        }, TimeSpan.FromSeconds(5)));
        Assert.True(first.IsFaulted);
        Assert.True(second.IsFaulted);
        Assert.Contains("integrity", first.Exception!.ToString());
        Assert.Contains("integrity", second.Exception!.ToString());
        Assert.Equal(0, database.preparingBytes);
        Assert.Equal(0, database.preparationStatistics.pendingPayloads);
        Assert.Equal(0, database.residencyStatistics.residentAssetCount);
    }

    [Fact]
    public void ImportDependencyCycle_IsRejectedWithClosedPathChain()
    {
        using TestWorkspace workspace = new();
        workspace.WriteText("Import/a.importgraph", "Import/b.importgraph");
        workspace.WriteText("Import/b.importgraph", "Import/a.importgraph");
        using var loader = workspace.CreateLoader(m_types, m_serialization, m_identities, m_diagnostics, m_logs);

        Assert.True(loader.Import(AssetPath.Project("Import/a.importgraph")));
        Assert.False(loader.Import(AssetPath.Project("Import/b.importgraph")));
        Assert.True(loader.TryGetInfo(AssetPath.Project("Import/b.importgraph"), out AssetInfo? info));
        Assert.NotNull(info);
        Assert.Equal(AssetImportStatus.Failed, info.status);
        string diagnostic = Assert.Single(info.diagnostics);

        Assert.Contains("Import/a.importgraph", diagnostic);
        Assert.Contains("Import/b.importgraph", diagnostic);
        Assert.Contains("->", diagnostic);
    }

    [Fact]
    public void RenameAndDelete_UpdateCanonicalInstanceWithoutChangingIdentity()
    {
        using TestWorkspace workspace = new();
        workspace.WriteText("Text/old.txt", "value");
        using var loader = workspace.CreateLoader(m_types, m_serialization, m_identities, m_diagnostics, m_logs);
        TextAsset asset = Assert.IsType<TextAsset>(loader.Load(AssetPath.Project("Text/old.txt"), typeof(TextAsset)));
        Guid persistentId = asset.identity.persistentId;

        workspace.Move("Text/old.txt", "Text/new.txt");
        loader.ApplySourceChanges([
            new Inno.Assets.Pipeline.AssetChangedEvent("Text/new.txt", WatcherChangeTypes.Renamed, "Text/old.txt")
        ]);

        Assert.Equal("Text/new.txt", asset.assetPath.ToString());
        Assert.Same(asset, loader.Load(persistentId, typeof(TextAsset)));

        workspace.DeleteSource("Text/new.txt");
        loader.ApplySourceChanges([
            new Inno.Assets.Pipeline.AssetChangedEvent("Text/new.txt", WatcherChangeTypes.Deleted)
        ]);

        Assert.True(asset.isMissing);
        Assert.Equal(persistentId, asset.identity.persistentId);
        Assert.True(asset.runtimePayload.IsEmpty);
        Assert.False(System.IO.File.Exists(workspace.SourcePath("Text/new.txt.imeta")));
        Assert.False(loader.TryGetInfo(AssetPath.Project("Text/new.txt"), out _));
        Assert.True(loader.TryGetInfo(persistentId, out AssetInfo? tombstone));
        Assert.NotNull(tombstone);
        Assert.Equal(AssetImportStatus.Missing, tombstone.status);
        Assert.True(tombstone.artifactKey.isEmpty);
        Assert.False(tombstone.lastSuccessfulArtifactKey.isEmpty);
    }

    [Fact]
    public void RecreatedSourceWithoutMetadata_ReceivesNewIdentity()
    {
        using TestWorkspace workspace = new();
        workspace.WriteText("Text/recover.txt", "one");
        using var loader = workspace.CreateLoader(m_types, m_serialization, m_identities, m_diagnostics, m_logs);
        TextAsset asset = Assert.IsType<TextAsset>(loader.Load(AssetPath.Project("Text/recover.txt"), typeof(TextAsset)));

        workspace.DeleteSource("Text/recover.txt");
        loader.ApplySourceChanges([
            new Inno.Assets.Pipeline.AssetChangedEvent("Text/recover.txt", WatcherChangeTypes.Deleted)
        ]);
        workspace.WriteText("Text/recover.txt", "two");
        loader.ApplySourceChanges([
            new Inno.Assets.Pipeline.AssetChangedEvent("Text/recover.txt", WatcherChangeTypes.Created)
        ]);

        TextAsset replacement = Assert.IsType<TextAsset>(
            loader.Load(AssetPath.Project("Text/recover.txt"), typeof(TextAsset)));
        Assert.True(asset.isMissing);
        Assert.False(replacement.isMissing);
        Assert.Equal("two", replacement.content);
        Assert.NotEqual(asset.identity.persistentId, replacement.identity.persistentId);
        Assert.Same(asset, loader.Load(asset.identity.persistentId, typeof(TextAsset)));
    }

    [Fact]
    public void Tombstone_SurvivesCatalogRestartWithoutOccupyingItsFormerPath()
    {
        using TestWorkspace workspace = new();
        workspace.WriteText("Text/value.txt", "one");
        Guid oldId;
        using (AssetLoader loader = workspace.CreateLoader(m_types, m_serialization, m_identities, m_diagnostics, m_logs))
        {
            loader.Rescan();
            Assert.True(loader.TryGetPersistentId(AssetPath.Project("Text/value.txt"), out oldId));
            workspace.DeleteSource("Text/value.txt");
            loader.ApplySourceChanges([
                new Inno.Assets.Pipeline.AssetChangedEvent("Text/value.txt", WatcherChangeTypes.Deleted)
            ]);
        }

        workspace.WriteText("Text/value.txt", "two");
        using AssetLoader restarted = workspace.CreateLoader(m_types, m_serialization, m_identities, m_diagnostics, m_logs);
        restarted.Rescan();

        Assert.True(restarted.TryGetInfo(oldId, out AssetInfo? tombstone));
        Assert.Equal(AssetImportStatus.Missing, tombstone!.status);
        Assert.True(restarted.TryGetPersistentId(AssetPath.Project("Text/value.txt"), out Guid newId));
        Assert.NotEqual(oldId, newId);
    }

    [Fact]
    public void RestoredSourceAndMetadata_ReactivatesOriginalIdentityInPlace()
    {
        using TestWorkspace workspace = new();
        workspace.WriteText("Text/recover.txt", "one");
        using var loader = workspace.CreateLoader(m_types, m_serialization, m_identities, m_diagnostics, m_logs);
        TextAsset asset = Assert.IsType<TextAsset>(loader.Load(AssetPath.Project("Text/recover.txt"), typeof(TextAsset)));
        Guid id = asset.identity.persistentId;
        byte[] metadata = System.IO.File.ReadAllBytes(workspace.SourcePath("Text/recover.txt.imeta"));

        workspace.DeleteSource("Text/recover.txt");
        loader.ApplySourceChanges([
            new Inno.Assets.Pipeline.AssetChangedEvent("Text/recover.txt", WatcherChangeTypes.Deleted)
        ]);
        workspace.WriteText("Text/recover.txt", "two");
        System.IO.File.WriteAllBytes(workspace.SourcePath("Text/recover.txt.imeta"), metadata);
        loader.ApplySourceChanges([
            new Inno.Assets.Pipeline.AssetChangedEvent("Text/recover.txt", WatcherChangeTypes.Created)
        ]);

        TextAsset restored = Assert.IsType<TextAsset>(loader.Load(AssetPath.Project("Text/recover.txt"), typeof(TextAsset)));
        Assert.Same(asset, restored);
        Assert.Equal(id, restored.identity.persistentId);
        Assert.False(restored.isMissing);
        Assert.Equal("two", restored.content);
    }

    [Fact]
    public void DeleteCreateRenameFallback_PreservesIdentityOnlyWhenFingerprintMatchIsUnique()
    {
        using TestWorkspace workspace = new();
        workspace.WriteText("Text/old.txt", "value");
        using var loader = workspace.CreateLoader(m_types, m_serialization, m_identities, m_diagnostics, m_logs);
        loader.Rescan();
        Assert.True(loader.TryGetPersistentId(AssetPath.Project("Text/old.txt"), out Guid id));

        workspace.Move("Text/old.txt", "Text/new.txt");
        loader.ApplySourceChanges([
            new Inno.Assets.Pipeline.AssetChangedEvent("Text/old.txt", WatcherChangeTypes.Deleted),
            new Inno.Assets.Pipeline.AssetChangedEvent("Text/new.txt", WatcherChangeTypes.Created)
        ]);

        Assert.True(loader.TryGetPersistentId(AssetPath.Project("Text/new.txt"), out Guid movedId));
        Assert.Equal(id, movedId);
        Assert.False(System.IO.File.Exists(workspace.SourcePath("Text/old.txt.imeta")));
        Assert.True(System.IO.File.Exists(workspace.SourcePath("Text/new.txt.imeta")));
    }

    [Fact]
    public void AmbiguousDeleteCreateRenameFallback_DoesNotGuessAnIdentity()
    {
        using TestWorkspace workspace = new();
        workspace.WriteText("Text/first.txt", "same");
        workspace.WriteText("Text/second.txt", "same");
        using var loader = workspace.CreateLoader(m_types, m_serialization, m_identities, m_diagnostics, m_logs);
        loader.Rescan();
        Assert.True(loader.TryGetPersistentId(AssetPath.Project("Text/first.txt"), out Guid firstId));
        Assert.True(loader.TryGetPersistentId(AssetPath.Project("Text/second.txt"), out Guid secondId));

        workspace.DeleteSource("Text/first.txt");
        workspace.DeleteSource("Text/second.txt");
        workspace.WriteText("Text/new.txt", "same");
        loader.ApplySourceChanges([
            new Inno.Assets.Pipeline.AssetChangedEvent("Text/first.txt", WatcherChangeTypes.Deleted),
            new Inno.Assets.Pipeline.AssetChangedEvent("Text/second.txt", WatcherChangeTypes.Deleted),
            new Inno.Assets.Pipeline.AssetChangedEvent("Text/new.txt", WatcherChangeTypes.Created)
        ]);

        Assert.True(loader.TryGetPersistentId(AssetPath.Project("Text/new.txt"), out Guid newId));
        Assert.NotEqual(firstId, newId);
        Assert.NotEqual(secondId, newId);
        Assert.True(loader.TryGetInfo(AssetPath.Project("Text/new.txt"), out AssetInfo? newInfo));
        Assert.Contains(newInfo!.diagnostics, diagnostic =>
            diagnostic.Contains("matched 2 removed assets", StringComparison.Ordinal));
        Assert.True(loader.TryGetInfo(firstId, out AssetInfo? firstTombstone));
        Assert.True(loader.TryGetInfo(secondId, out AssetInfo? secondTombstone));
        Assert.Equal(AssetImportStatus.Missing, firstTombstone!.status);
        Assert.Equal(AssetImportStatus.Missing, secondTombstone!.status);
    }

    [Fact]
    public void Save_PreservesIdentityIncrementsVersionAndRejectsDifferentPath()
    {
        using TestWorkspace workspace = new();
        using var loader = workspace.CreateLoader(m_types, m_serialization, m_identities, m_diagnostics, m_logs);
        var asset = new MutableAsset { value = "one" };

        Assert.True(loader.Save(AssetPath.Project("Data/value.mutableasset"), asset));
        Guid persistentId = asset.identity.persistentId;
        long version = asset.contentVersion;
        asset.value = "two";
        Assert.True(loader.Save(asset));

        Assert.Equal(persistentId, asset.identity.persistentId);
        Assert.Equal(version + 1, asset.contentVersion);
        Assert.Equal("two", workspace.ReadText("Data/value.mutableasset"));
        Assert.Same(asset, loader.Load(persistentId, typeof(MutableAsset)));
        Assert.Throws<InvalidOperationException>(() => loader.Save(AssetPath.Project("Data/copy.mutableasset"), asset));
    }

    [Fact]
    public void FailedReimport_KeepsPreviousCanonicalStateAndVersion()
    {
        using TestWorkspace workspace = new();
        workspace.WriteText("Data/stable.mutableasset", "one");
        using var loader = workspace.CreateLoader(m_types, m_serialization, m_identities, m_diagnostics, m_logs);
        MutableAsset asset = Assert.IsType<MutableAsset>(
            loader.Load(AssetPath.Project("Data/stable.mutableasset"), typeof(MutableAsset)));
        long version = asset.contentVersion;

        workspace.WriteText("Data/stable.mutableasset", "!invalid!");
        Assert.False(loader.Import(AssetPath.Project("Data/stable.mutableasset")));
        Assert.True(loader.TryGetInfo(AssetPath.Project("Data/stable.mutableasset"), out AssetInfo? info));
        Assert.NotNull(info);
        Assert.Equal(AssetImportStatus.Failed, info.status);
        Assert.Contains(info.diagnostics, static value => value.Contains("InvalidDataException"));

        Assert.Equal("one", asset.value);
        Assert.Equal(version, asset.contentVersion);
    }

    [Fact]
    public void FailedReimport_PublishesCurrentDiagnosticUntilSuccessfulRetry()
    {
        using TestWorkspace workspace = new();
        workspace.WriteText("Data/diagnostic.mutableasset", "valid");
        using var loader = workspace.CreateLoader(m_types, m_serialization, m_identities, m_diagnostics, m_logs);
        var sink = new TestDiagnosticSink();
        m_diagnostics.RegisterSink(sink);
        try
        {
            MutableAsset asset = Assert.IsType<MutableAsset>(
                loader.Load(AssetPath.Project("Data/diagnostic.mutableasset"), typeof(MutableAsset)));
            workspace.WriteText("Data/diagnostic.mutableasset", "!invalid!");

            Assert.False(loader.Import(AssetPath.Project("Data/diagnostic.mutableasset")));
            DiagnosticReport report = Assert.Single(sink.reports.Values.Where(value =>
                value.source.displayName == "Data/diagnostic.mutableasset"));
            Diagnostic diagnostic = Assert.Single(report.diagnostics);
            Assert.Equal("ASSET-IMPORT", diagnostic.code);
            Assert.Equal(DiagnosticSeverity.Error, diagnostic.severity);

            workspace.WriteText("Data/diagnostic.mutableasset", "recovered");
            Assert.True(loader.Import(AssetPath.Project("Data/diagnostic.mutableasset")));
            Assert.DoesNotContain(
                sink.reports.Values,
                value => value.source.displayName == "Data/diagnostic.mutableasset");
            Assert.Equal("recovered", asset.value);
        }
        finally
        {
            m_diagnostics.UnregisterSink(sink);
        }
    }

    [Fact]
    public void FailedSave_PreservesCommittedSourceMetaArtifactAndVersion()
    {
        using TestWorkspace workspace = new();
        using var loader = workspace.CreateLoader(m_types, m_serialization, m_identities, m_diagnostics, m_logs);
        var asset = new MutableAsset { value = "one" };
        Assert.True(loader.Save(AssetPath.Project("Data/rollback.mutableasset"), asset));
        byte[] sourceBefore = System.IO.File.ReadAllBytes(workspace.SourcePath("Data/rollback.mutableasset"));
        byte[] metaBefore = System.IO.File.ReadAllBytes(workspace.SourcePath("Data/rollback.mutableasset.imeta"));
        Assert.True(loader.TryGetArtifact(asset.identity.persistentId, "runtime", out AssetArtifactInfo? artifact));
        Assert.NotNull(artifact);
        byte[] artifactBefore = System.IO.File.ReadAllBytes(artifact.absolutePath);
        long versionBefore = asset.contentVersion;
        asset.value = "!invalid!";

        Assert.Throws<InvalidDataException>(() => loader.Save(asset));

        Assert.Equal(sourceBefore, System.IO.File.ReadAllBytes(workspace.SourcePath("Data/rollback.mutableasset")));
        Assert.Equal(metaBefore, System.IO.File.ReadAllBytes(workspace.SourcePath("Data/rollback.mutableasset.imeta")));
        Assert.Equal(artifactBefore, System.IO.File.ReadAllBytes(artifact.absolutePath));
        Assert.Equal(versionBefore, asset.contentVersion);
    }

    [Fact]
    public void RuntimeDependencyRetention_KeepsDependencyAliveWhileRootIsReferenced()
    {
        using TestWorkspace workspace = new();
        workspace.WriteText("Text/dependency.txt", "dependency");
        workspace.WriteText("Graphs/root.depgraph", "Text/dependency.txt");
        using var loader = workspace.CreateLoader(m_types, m_serialization, m_identities, m_diagnostics, m_logs);
        DependencyAsset root = Assert.IsType<DependencyAsset>(
            loader.Load(AssetPath.Project("Graphs/root.depgraph"), typeof(DependencyAsset)));

        Assert.Equal(0, loader.UnloadUnusedAssets());
        Assert.Equal(2, loader.GetLoadedPaths().Count);
        GC.KeepAlive(root);
    }

    [Fact]
    public void UnusedRuntimeDependencyCycle_IsCollectedAsAUnit()
    {
        using TestWorkspace workspace = new();
        workspace.WriteText("Graphs/a.depgraph", "Graphs/b.depgraph");
        workspace.WriteText("Graphs/b.depgraph", "Graphs/a.depgraph");
        using var loader = workspace.CreateLoader(m_types, m_serialization, m_identities, m_diagnostics, m_logs);
        (WeakReference first, WeakReference second) = LoadCycleWithoutEscaping(loader);

        int released = loader.UnloadUnusedAssets();

        Assert.Equal(2, released);
        Assert.False(first.IsAlive);
        Assert.False(second.IsAlive);
        Assert.Empty(loader.GetLoadedPaths());
    }

    [Fact]
    public void ReferenceDiagnostics_DoNotKeepAssetsAlive()
    {
        using TestWorkspace workspace = new();
        workspace.WriteText("Text/dependency.txt", "dependency");
        workspace.WriteText("Graphs/root.depgraph", "Text/dependency.txt");
        using var loader = workspace.CreateLoader(m_types, m_serialization, m_identities, m_diagnostics, m_logs);
        WeakReference weak = LoadAndInspectWithoutEscaping(loader);

        Assert.Equal(2, loader.UnloadUnusedAssets());
        Assert.False(weak.IsAlive);
    }

    [Fact]
    public void Dispose_ReleasesRuntimeResourcesExactlyOnce()
    {
        using TestWorkspace workspace = new();
        workspace.WriteText("Hooks/value.hookasset", "value");
        var loader = workspace.CreateLoader(m_types, m_serialization, m_identities, m_diagnostics, m_logs);
        HookAsset asset = Assert.IsType<HookAsset>(loader.Load(AssetPath.Project("Hooks/value.hookasset"), typeof(HookAsset)));

        loader.Dispose();
        loader.Dispose();

        Assert.True(asset.payloadChangeCount >= 1);
        Assert.Equal(1, asset.unloadingCount);
        GC.KeepAlive(asset);
    }

    [Fact]
    public void Rescan_TracksUnsupportedSourcesAndFolderIdentityWithoutFakeArtifacts()
    {
        using TestWorkspace workspace = new();
        Directory.CreateDirectory(workspace.SourcePath("EmptyFolder"));
        workspace.WriteText("Unknown/value.unknown", "value");
        using var loader = workspace.CreateLoader(m_types, m_serialization, m_identities, m_diagnostics, m_logs);

        loader.Rescan();

        Assert.True(loader.TryGetInfo(AssetPath.Project("EmptyFolder"), out AssetInfo? folder));
        Assert.Equal(AssetSourceKind.Directory, folder!.sourceKind);
        Assert.Equal(AssetImportStatus.Imported, folder.status);
        Assert.True(System.IO.File.Exists(workspace.SourcePath("EmptyFolder.imeta")));
        Assert.True(folder.artifactKey.isEmpty);
        Assert.True(loader.TryGetInfo(AssetPath.Project("Unknown/value.unknown"), out AssetInfo? unsupported));
        Assert.Equal(AssetImportStatus.Unsupported, unsupported!.status);
        Assert.Equal(Guid.Empty, unsupported.persistentId);
        Assert.False(System.IO.File.Exists(workspace.SourcePath("Unknown/value.unknown.imeta")));
        Assert.True(unsupported.artifactKey.isEmpty);
    }

    [Fact]
    public void SourceMetadata_RestoresPersistentIdentityWhenLibraryIsRebuilt()
    {
        using TestWorkspace workspace = new();
        workspace.WriteText("Text/value.txt", "value");
        Guid id;
        using (AssetLoader first = workspace.CreateLoader(m_types, m_serialization, m_identities, m_diagnostics, m_logs))
        {
            first.Rescan();
            Assert.True(first.TryGetPersistentId(AssetPath.Project("Text/value.txt"), out id));
        }
        Directory.Delete(workspace.libraryRoot, recursive: true);
        Directory.CreateDirectory(workspace.libraryRoot);

        using AssetLoader rebuilt = workspace.CreateLoader(m_types, m_serialization, m_identities, m_diagnostics, m_logs);
        rebuilt.Rescan();

        Assert.True(rebuilt.TryGetPersistentId(AssetPath.Project("Text/value.txt"), out Guid restored));
        Assert.Equal(id, restored);
    }

    [Fact]
    public void ContentAddressedStore_DeduplicatesEqualImportsAndCollectsUnreachableBundles()
    {
        using TestWorkspace workspace = new();
        workspace.WriteText("Text/first.txt", "same");
        workspace.WriteText("Text/second.txt", "same");
        using var loader = workspace.CreateLoader(m_types, m_serialization, m_identities, m_diagnostics, m_logs);
        loader.Rescan();
        Assert.True(loader.TryGetInfo(AssetPath.Project("Text/first.txt"), out AssetInfo? first));
        Assert.True(loader.TryGetInfo(AssetPath.Project("Text/second.txt"), out AssetInfo? second));
        Assert.Equal(first!.artifactKey, second!.artifactKey);
        Assert.True(loader.TryGetArtifact(first.persistentId, "runtime", out AssetArtifactInfo? oldArtifact));
        Assert.NotNull(oldArtifact);

        workspace.WriteText("Text/first.txt", "changed");
        Assert.True(loader.Import(AssetPath.Project("Text/first.txt")));
        Assert.True(loader.TryGetArtifact(first.persistentId, "runtime", out AssetArtifactInfo? currentArtifact));
        Assert.NotNull(currentArtifact);
        Assert.NotEqual(oldArtifact.key, currentArtifact.key);
        Assert.True(System.IO.File.Exists(oldArtifact.absolutePath));

        Assert.Equal(0, loader.CollectArtifacts(TimeSpan.Zero, maximumSizeBytes: 0));
        Assert.True(System.IO.File.Exists(oldArtifact.absolutePath));
        workspace.DeleteSource("Text/second.txt");
        System.IO.File.Delete(workspace.SourcePath("Text/second.txt.imeta"));
        loader.Rescan();
        Assert.True(loader.CollectArtifacts(TimeSpan.Zero, maximumSizeBytes: 0) >= 1);
        Assert.False(System.IO.File.Exists(oldArtifact.absolutePath));
        Assert.True(System.IO.File.Exists(currentArtifact.absolutePath));
    }

    [Fact]
    public async Task AggregateBuildProcessor_ProducesStableContentAddressedOutput()
    {
        using TestWorkspace workspace = new();
        workspace.WriteText("Text/input.txt", "value");
        using var loader = workspace.CreateLoader(m_types, m_serialization, m_identities, m_diagnostics, m_logs);
        loader.Rescan();
        Assert.True(loader.TryGetInfo(AssetPath.Project("Text/input.txt"), out AssetInfo? input));
        Assert.NotNull(input);
        var definition = new TestBuildDefinitionAsset { label = "bundle" };

        AssetArtifactKey first = await loader.BuildAsync(definition, [input]);
        AssetArtifactKey repeated = await loader.BuildAsync(definition, [input]);

        Assert.False(first.isEmpty);
        Assert.Equal(first, repeated);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (WeakReference first, WeakReference second) LoadCycleWithoutEscaping(AssetLoader loader)
    {
        AssetObject first = loader.Load(AssetPath.Project("Graphs/a.depgraph"), typeof(DependencyAsset))!;
        AssetDependency secondDescriptor = Assert.Single(loader.GetDependencies(first));
        AssetObject second = loader.Load(secondDescriptor.persistentId, typeof(DependencyAsset))!;
        return (new WeakReference(first), new WeakReference(second));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference LoadAndInspectWithoutEscaping(AssetLoader loader)
    {
        AssetObject root = loader.Load(AssetPath.Project("Graphs/root.depgraph"), typeof(DependencyAsset))!;
        AssetDependency descriptor = Assert.Single(loader.GetDependencies(root));
        AssetObject dependency = loader.Load(descriptor.persistentId, typeof(TextAsset))!;
        AssetReferenceInfo info = loader.GetReferenceInfo(dependency);
        Assert.Equal(1, info.knownReferenceCount);
        Assert.Equal(AssetReferenceKind.AssetDependency, info.references[0].kind);
        return new WeakReference(root);
    }

    private sealed class TestWorkspace : IDisposable
    {
        private readonly string m_root = Path.Combine(
            Path.GetTempPath(),
            "InnoAssetLoaderTests",
            Guid.NewGuid().ToString("N"));

        internal TestWorkspace()
        {
            Directory.CreateDirectory(assetRoot);
            Directory.CreateDirectory(libraryRoot);
        }

        internal string assetRoot => Path.Combine(m_root, "Assets");
        internal string libraryRoot => Path.Combine(m_root, "Library");
        internal AssetLoader CreateLoader(
            TypeCatalog types,
            SerializationRegistry serialization,
            IdentityAllocator identities,
            DiagnosticHub diagnostics,
            LogRouter logs)
            => new(types, serialization, identities, diagnostics, logs, assetRoot, libraryRoot);
        internal string SourcePath(string relativePath) => Path.Combine(assetRoot, relativePath);

        internal void WriteText(string relativePath, string content)
        {
            string path = SourcePath(relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            System.IO.File.WriteAllText(path, content, new UTF8Encoding(false));
        }

        internal string ReadText(string relativePath) => System.IO.File.ReadAllText(SourcePath(relativePath));

        internal void Move(string oldRelativePath, string newRelativePath)
        {
            string target = SourcePath(newRelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            System.IO.File.Move(SourcePath(oldRelativePath), target);
        }

        internal void DeleteSource(string relativePath)
        {
            string path = SourcePath(relativePath);
            if (System.IO.File.Exists(path))
                System.IO.File.Delete(path);
        }

        public void Dispose()
        {
            SlowAssetImporter.allowImport.Set();
            if (Directory.Exists(m_root))
                Directory.Delete(m_root, recursive: true);
        }
    }

    private sealed class TestDiagnosticSink : IDiagnosticSink
    {
        internal Dictionary<string, DiagnosticReport> reports { get; } = new(StringComparer.Ordinal);

        public void Replace(DiagnosticReport report)
            => reports[report.source.id] = report;

        public void Clear(DiagnosticSource source)
            => reports.Remove(source.id);
    }
}

[StableTypeId("501988d0-c069-4dcf-97f6-e899b24e5801")]
internal sealed class PrivateConstructorAsset : AssetObject
{
    [SerializableProperty]
    internal string value { get; set; } = string.Empty;
}

[AssetImporterExtension]
internal sealed class PrivateConstructorAssetImporter : AssetImporter<PrivateConstructorAsset>
{
    private static readonly IReadOnlyList<string> s_extensions = [".privateasset"];

    private PrivateConstructorAssetImporter()
    {
    }

    public override string importerId => "inno.tests.private-constructor";
    public override IReadOnlyList<string> supportedExtensions => s_extensions;

    protected override ValueTask ImportAsync(
        AssetImportContext context,
        AssetImportWriter<PrivateConstructorAsset> output,
        CancellationToken cancellationToken)
    {
        output.SetAsset(new PrivateConstructorAsset { value = context.ReadUtf8Text() });
        return output.WriteArtifactAsync("runtime", context.sourceBytes, cancellationToken);
    }
}

[StableTypeId("a49b603c-0f5f-4861-903a-3819513da002")]
internal sealed class DependencyAsset : AssetObject;

[AssetImporterExtension]
internal sealed class DependencyAssetImporter : AssetImporter<DependencyAsset>
{
    public override string importerId => "inno.tests.runtime-dependency";
    public override IReadOnlyList<string> supportedExtensions { get; } = [".depgraph"];

    protected override ValueTask ImportAsync(
        AssetImportContext context,
        AssetImportWriter<DependencyAsset> output,
        CancellationToken cancellationToken)
    {
        string dependency = context.ReadUtf8Text().Trim();
        if (!string.IsNullOrWhiteSpace(dependency))
            output.DependsOnAsset(AssetPath.Parse(dependency));
        output.SetAsset(new DependencyAsset());
        return output.WriteArtifactAsync("runtime", context.sourceBytes, cancellationToken);
    }
}

[AssetImporterExtension]
internal sealed class AlternateDependencyAssetImporter : AssetImporter<DependencyAsset>
{
    public override string importerId => "inno.tests.runtime-dependency-alternate";
    public override IReadOnlyList<string> supportedExtensions { get; } = [".depgraph2"];

    protected override ValueTask ImportAsync(
        AssetImportContext context,
        AssetImportWriter<DependencyAsset> output,
        CancellationToken cancellationToken)
    {
        output.SetAsset(new DependencyAsset());
        return output.WriteArtifactAsync("runtime", context.sourceBytes, cancellationToken);
    }
}

[StableTypeId("a80d363f-8e49-4615-89ee-589613b91c03")]
internal sealed class ImportGraphAsset : AssetObject;

[AssetImporterExtension]
internal sealed class ImportGraphAssetImporter : AssetImporter<ImportGraphAsset>
{
    public override string importerId => "inno.tests.import-dependency";
    public override IReadOnlyList<string> supportedExtensions { get; } = [".importgraph"];

    protected override ValueTask ImportAsync(
        AssetImportContext context,
        AssetImportWriter<ImportGraphAsset> output,
        CancellationToken cancellationToken)
    {
        string dependency = context.ReadUtf8Text().Trim();
        if (!string.IsNullOrWhiteSpace(dependency))
            output.DependsOnSource(AssetPath.Parse(dependency));
        output.SetAsset(new ImportGraphAsset());
        return output.WriteArtifactAsync("runtime", context.sourceBytes, cancellationToken);
    }
}

[StableTypeId("490def27-4cdd-48a0-b324-9b7438600404")]
internal sealed class SlowAsset : AssetObject
{
    [SerializableProperty]
    internal string value { get; set; } = string.Empty;
}

[AssetImporterExtension]
internal sealed class SlowAssetImporter : AssetImporter<SlowAsset>
{
    internal static readonly ManualResetEventSlim importStarted = new(false);
    internal static readonly ManualResetEventSlim allowImport = new(false);
    internal static int importCount;

    public override string importerId => "inno.tests.slow";
    public override IReadOnlyList<string> supportedExtensions { get; } = [".slowasset"];

    internal static void Reset()
    {
        importCount = 0;
        importStarted.Reset();
        allowImport.Reset();
    }

    protected override ValueTask ImportAsync(
        AssetImportContext context,
        AssetImportWriter<SlowAsset> output,
        CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref importCount);
        importStarted.Set();
        if (!allowImport.Wait(TimeSpan.FromSeconds(5)))
            throw new TimeoutException("The slow importer test gate was not released.");
        output.SetAsset(new SlowAsset { value = context.ReadUtf8Text() });
        return output.WriteArtifactAsync("runtime", context.sourceBytes, cancellationToken);
    }
}

[StableTypeId("fa17dc36-b9cc-4187-b5da-72906ad00505")]
internal sealed class MutableAsset : AssetObject
{
    [SerializableProperty]
    internal string value { get; set; } = string.Empty;
}

[AssetImporterExtension]
internal sealed class MutableAssetImporter : AssetImporter<MutableAsset>
{
    public override string importerId => "inno.tests.mutable";
    public override IReadOnlyList<string> supportedExtensions { get; } = [".mutableasset"];

    protected override ValueTask ImportAsync(
        AssetImportContext context,
        AssetImportWriter<MutableAsset> output,
        CancellationToken cancellationToken)
    {
        string value = context.ReadUtf8Text();
        if (value == "!invalid!")
            throw new InvalidDataException("The mutable asset source is invalid.");
        output.SetAsset(new MutableAsset { value = value });
        return output.WriteArtifactAsync("runtime", context.sourceBytes, cancellationToken);
    }

    protected override ValueTask<ReadOnlyMemory<byte>?> ExportAsync(
        AssetExportContext context,
        MutableAsset asset,
        CancellationToken cancellationToken)
        => ValueTask.FromResult<ReadOnlyMemory<byte>?>(Encoding.UTF8.GetBytes(asset.value));
}

[StableTypeId("9f9f5f9f-4d86-414f-9ff9-78ad8ec60606")]
internal sealed class HookAsset : AssetObject
{
    internal int payloadChangeCount;
    internal int unloadingCount;
    internal int pendingUnloads;
    internal bool failUnloading;
    internal bool retirementExpired;

    protected override void OnRuntimePayloadChanged(
        ReadOnlyMemory<byte> previousPayload,
        ReadOnlyMemory<byte> currentPayload)
        => Interlocked.Increment(ref payloadChangeCount);

    protected override void OnUnloading()
    {
        Interlocked.Increment(ref unloadingCount);
        if (retirementExpired)
            throw new RetirementTimeoutException("The test asset work exceeded its retirement deadline.");
        if (pendingUnloads > 0)
        {
            pendingUnloads--;
            throw new RetirementPendingException("The test asset still owns unfinished work.");
        }
        if (failUnloading)
            throw new InvalidOperationException("The test asset release failed.");
    }
}

[AssetImporterExtension]
internal sealed class HookAssetImporter : AssetImporter<HookAsset>
{
    public override string importerId => "inno.tests.hook";
    public override IReadOnlyList<string> supportedExtensions { get; } = [".hookasset"];

    protected override ValueTask ImportAsync(
        AssetImportContext context,
        AssetImportWriter<HookAsset> output,
        CancellationToken cancellationToken)
    {
        output.SetAsset(new HookAsset());
        return output.WriteArtifactAsync("runtime", context.sourceBytes, cancellationToken);
    }
}

internal enum ImporterConflictMode
{
    None,
    DuplicateId,
    DuplicateExtension
}

internal static class ImporterConflictProbe
{
    internal static ImporterConflictMode mode;
}

[StableTypeId("da675da1-9276-40c4-9964-0eb4b8ff9a07")]
internal sealed class ImporterConflictAsset : AssetObject;

[AssetImporterExtension]
internal sealed class ImporterConflictAssetImporterA : AssetImporter<ImporterConflictAsset>
{
    public override string importerId => ImporterConflictProbe.mode == ImporterConflictMode.DuplicateId
        ? "inno.tests.conflict"
        : "inno.tests.conflict-a";

    public override IReadOnlyList<string> supportedExtensions =>
        ImporterConflictProbe.mode == ImporterConflictMode.DuplicateExtension
            ? [".conflict"]
            : [".probea"];

    protected override ValueTask ImportAsync(
        AssetImportContext context,
        AssetImportWriter<ImporterConflictAsset> output,
        CancellationToken cancellationToken)
    {
        output.SetAsset(new ImporterConflictAsset());
        return output.WriteArtifactAsync("runtime", context.sourceBytes, cancellationToken);
    }
}

[AssetImporterExtension]
internal sealed class ImporterConflictAssetImporterB : AssetImporter<ImporterConflictAsset>
{
    public override string importerId => ImporterConflictProbe.mode == ImporterConflictMode.DuplicateId
        ? "inno.tests.conflict"
        : "inno.tests.conflict-b";

    public override IReadOnlyList<string> supportedExtensions =>
        ImporterConflictProbe.mode == ImporterConflictMode.DuplicateExtension
            ? [".conflict"]
            : [".probeb"];

    protected override ValueTask ImportAsync(
        AssetImportContext context,
        AssetImportWriter<ImporterConflictAsset> output,
        CancellationToken cancellationToken)
    {
        output.SetAsset(new ImporterConflictAsset());
        return output.WriteArtifactAsync("runtime", context.sourceBytes, cancellationToken);
    }
}

[StableTypeId("8f87f452-203e-4cd4-9c91-c93af4802141")]
internal sealed class TestBuildDefinitionAsset : AssetObject
{
    [SerializableProperty]
    internal string label { get; set; } = string.Empty;
}

[AssetBuildProcessorExtension]
internal sealed class TestBuildProcessor : AssetBuildProcessor<TestBuildDefinitionAsset>
{
    public override string processorId => "inno.tests.aggregate-build";

    protected override ValueTask BuildAsync(
        AssetBuildContext<TestBuildDefinitionAsset> context,
        AssetArtifactWriter output,
        CancellationToken cancellationToken)
    {
        string value = context.definition.label + ":" + context.inputs.Count;
        return output.WriteAsync("result", Encoding.UTF8.GetBytes(value), cancellationToken);
    }
}
