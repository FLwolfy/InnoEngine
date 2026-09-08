using System;
using System.Collections.Generic;
using System.Linq;
using Inno.Assets;
using Inno.Assets.Pipeline;
using Inno.Scripting.Api;
using Inno.References;
using Inno.Core.Execution;

namespace Inno.Assets.Pipeline;

/// <summary>
/// Holds an isolated source-mount candidate that can be inspected before it atomically replaces the active Asset Database.
/// </summary>
public sealed class AssetSourceMountTransaction : IDisposable
{
    private readonly IReadOnlyList<AssetSourceMount> m_mounts;
    private readonly ReferenceRecoveryTransaction m_recovery;
    private Exception? m_terminalFailure;
    private bool m_prepared;
    private bool m_activationComplete;

    internal AssetSourceMountTransaction(
        AssetPipeline owner,
        IReadOnlyList<AssetSourceMount> mounts,
        AssetCatalogCandidate catalogCandidate,
        AssetFileSystem candidateFileSystem,
        IReadOnlyList<SerializedMissingState> previousStates,
        long generation)
    {
        m_mounts = Array.AsReadOnly(mounts.ToArray());
        this.catalogCandidate = catalogCandidate;
        this.candidateFileSystem = candidateFileSystem;
        m_recovery = new ReferenceRecoveryTransaction(
            ReferenceCatalog.Create(generation, [candidateLoader]), previousStates,
            [new AssetSourceMountRecovery(owner, this)]);
    }

    internal AssetCatalogCandidate catalogCandidate { get; }

    internal AssetLoader candidateLoader => catalogCandidate.loader;

    internal AssetFileSystem candidateFileSystem { get; }

    internal AssetLoader? previousLoader { get; set; }

    internal AssetFileSystem? previousFileSystem { get; set; }

    internal IReadOnlyList<AssetSourceMount>? previousMounts { get; set; }

    internal AssetPipelineOptions previousOptions { get; set; }

    internal bool isActivated { get; set; }

    internal bool isFinished { get; set; }

    internal LifetimeScope? retirement { get; set; }

    internal RetirementBarrier retirementBarrier { get; } = new("Asset source-mount generation");

    /// <summary>
    /// Gets the complete isolated mount snapshot represented by this candidate.
    /// </summary>
    [ScriptingApiIgnore]
    public IReadOnlyList<AssetSourceMount> sourceMounts => m_mounts;

    /// <summary>
    /// Gets neutral canonical reference outcomes after activation, or an empty set before activation or after rollback.
    /// </summary>
    [ScriptingApiIgnore]
    public IReadOnlyList<ReferenceRecoveryChange> recoveryChanges => m_recovery.changes;

    /// <summary>
    /// Gets candidate source entries without publishing them to active AssetPipeline consumers.
    /// </summary>
    /// <param name="includeDirectories">
    /// Whether directory entries should be included.
    /// </param>
    /// <returns>
    /// A stable candidate entry snapshot.
    /// </returns>
    [ScriptingApiIgnore]
    public IReadOnlyList<AssetFileEntry> GetFileSystemEntries(bool includeDirectories = true)
    {
        EnsureOpen();
        return candidateFileSystem.GetEntries(includeDirectories);
    }

    /// <summary>
    /// Loads one candidate asset by isolated source path.
    /// </summary>
    /// <typeparam name="TAsset">
    /// Required asset type.
    /// </typeparam>
    /// <param name="path">
    /// Candidate source path.
    /// </param>
    /// <returns>
    /// The candidate asset instance.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no compatible candidate asset exists.
    /// </exception>
    [ScriptingApiIgnore]
    public TAsset Load<TAsset>(AssetPath path) where TAsset : AssetObject
    {
        EnsureOpen();
        AssetObject? asset = candidateLoader.Load(path, typeof(TAsset));
        return asset as TAsset ?? throw new InvalidOperationException(
            $"Candidate asset '{path}' cannot be loaded as '{typeof(TAsset).FullName}'.");
    }

    /// <summary>
    /// Tries to get candidate catalog information by isolated source path.
    /// </summary>
    /// <param name="path">
    /// Candidate source path.
    /// </param>
    /// <param name="info">
    /// Candidate catalog information when available.
    /// </param>
    /// <returns>
    /// True when the candidate path is cataloged.
    /// </returns>
    [ScriptingApiIgnore]
    public bool TryGetInfo(AssetPath path, out AssetInfo? info)
    {
        EnsureOpen();
        return candidateLoader.TryGetInfo(path, out info);
    }

    /// <summary>
    /// Tries to resolve one named candidate artifact.
    /// </summary>
    /// <param name="persistentId">
    /// Candidate asset identity.
    /// </param>
    /// <param name="outputName">
    /// Named artifact output.
    /// </param>
    /// <param name="artifact">
    /// Artifact information when available.
    /// </param>
    /// <returns>
    /// True when the candidate output exists.
    /// </returns>
    [ScriptingApiIgnore]
    public bool TryGetArtifact(
        Guid persistentId,
        string outputName,
        out AssetArtifactInfo? artifact)
    {
        EnsureOpen();
        return candidateLoader.TryGetArtifact(persistentId, outputName, out artifact);
    }

    /// <summary>
    /// Activates this candidate without releasing the previous generation or notifying observers.
    /// </summary>
    [ScriptingApiIgnore]
    public void Activate()
    {
        EnsureOpen();
        if (m_activationComplete)
            return;
        if (!m_prepared)
        {
            m_prepared = true;
            m_recovery.PrepareForActivation();
        }
        m_recovery.Apply();
        m_activationComplete = true;
    }

    /// <summary>
    /// Commits an activated candidate, notifies observers, and retires the previous generation.
    /// </summary>
    /// <exception cref="RetirementPendingException">
    /// Retirement exceeded its deadline. Both generations remain owned and the host must restart.
    /// </exception>
    /// <exception cref="AggregateException">
    /// Publication or completed retirement failed; the host generation gate is faulted.
    /// </exception>
    [ScriptingApiIgnore]
    public void Complete()
    {
        EnsureOpen();
        if (!m_activationComplete)
            throw new InvalidOperationException("A source-mount candidate must finish activation before completion.");
        try { m_recovery.Complete(); }
        catch (Exception failure)
        {
            m_terminalFailure = failure;
            throw;
        }
    }

    /// <summary>
    /// Discards the candidate or restores the previous generation after provisional activation.
    /// </summary>
    /// <exception cref="RetirementPendingException">
    /// Candidate retirement exceeded its deadline; its owners remain retained until host restart.
    /// </exception>
    /// <exception cref="AggregateException">
    /// Compensation or completed retirement failed.
    /// </exception>
    [ScriptingApiIgnore]
    public void Rollback()
    {
        if (isFinished)
            return;
        if (m_terminalFailure is not null)
            throw m_terminalFailure;
        if (!m_prepared)
        {
            m_prepared = true;
            m_recovery.PrepareForActivation();
        }
        try
        {
            m_recovery.RollbackStructure();
            m_recovery.RestorePreviousState();
        }
        catch (Exception failure)
        {
            m_terminalFailure = failure;
            throw;
        }
    }

    /// <summary>
    /// Discards an unfinished candidate and releases its rebuildable staging storage.
    /// </summary>
    public void Dispose()
    {
        Rollback();
        GC.SuppressFinalize(this);
    }

    private void EnsureOpen()
    {
        if (m_terminalFailure is not null)
            throw m_terminalFailure;
        if (isFinished)
            throw new ObjectDisposedException(nameof(AssetSourceMountTransaction));
    }
}
