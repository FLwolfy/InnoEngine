using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Inno.Core.Execution;

namespace Inno.Assets;

/// <summary>
/// Exposes explicit asynchronous asset and artifact residency ownership.
/// </summary>
public interface IAssetResidency
{
    /// <summary>
    /// Acquires a canonical asset by logical path and keeps it resident until the lease is released.
    /// </summary>
    /// <typeparam name="TAsset">
    /// Required asset contract.
    /// </typeparam>
    /// <param name="path">
    /// Mount-qualified logical asset path.
    /// </param>
    /// <param name="cancellationToken">
    /// Cancellation for this caller's asynchronous wait.
    /// </param>
    /// <returns>
    /// A lease that owns residency for the canonical asset.
    /// </returns>
    ValueTask<AssetLease<TAsset>> AcquireAsync<TAsset>(
        AssetPath path,
        CancellationToken cancellationToken = default)
        where TAsset : AssetObject;

    /// <summary>
    /// Acquires a canonical asset by persistent identity and keeps it resident until release.
    /// </summary>
    /// <typeparam name="TAsset">
    /// Required asset contract.
    /// </typeparam>
    /// <param name="persistentId">
    /// Non-empty persistent asset identity.
    /// </param>
    /// <param name="cancellationToken">
    /// Cancellation for this caller's asynchronous wait.
    /// </param>
    /// <returns>
    /// A lease that owns residency for the canonical asset.
    /// </returns>
    ValueTask<AssetLease<TAsset>> AcquireAsync<TAsset>(
        Guid persistentId,
        CancellationToken cancellationToken = default)
        where TAsset : AssetObject;

    /// <summary>
    /// Acquires one verified immutable artifact output.
    /// </summary>
    /// <param name="persistentId">
    /// Persistent identity of the artifact owner.
    /// </param>
    /// <param name="outputName">
    /// Stable artifact output name.
    /// </param>
    /// <returns>
    /// A lease that keeps the artifact generation available to the caller.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the requested artifact does not exist or fails verification.
    /// </exception>
    ArtifactLease AcquireArtifact(Guid persistentId, string outputName);
}

/// <summary>
/// Provides controlled lease construction to concrete asset residency implementations.
/// </summary>
public abstract class AssetResidencyProvider
{
    /// <summary>
    /// Creates one strongly typed asset residency lease.
    /// </summary>
    /// <typeparam name="TAsset">
    /// Concrete canonical asset type.
    /// </typeparam>
    /// <param name="asset">
    /// Canonical asset retained by the lease.
    /// </param>
    /// <param name="release">
    /// Provider-owned release callback. Pending retirement must be retryable without repeating completed effects.
    /// </param>
    /// <returns>
    /// A lease that retains its value until release completes; a Pending callback is retried by the owning lifecycle.
    /// </returns>
    protected static AssetLease<TAsset> CreateAssetLease<TAsset>(TAsset asset, Action release)
        where TAsset : AssetObject
        => new(asset, release);

    /// <summary>
    /// Creates one immutable artifact residency lease.
    /// </summary>
    /// <param name="artifact">
    /// Verified artifact metadata retained by the lease.
    /// </param>
    /// <param name="release">
    /// Provider-owned release callback. Pending retirement must be retryable without repeating completed effects.
    /// </param>
    /// <returns>
    /// A lease that retains its value until release completes; a Pending callback is retried by the owning lifecycle.
    /// </returns>
    protected static ArtifactLease CreateArtifactLease(AssetArtifactInfo artifact, Action release)
        => new(artifact, release);
}

/// <summary>
/// Keeps one canonical asset generation resident for an explicit lifetime.
/// </summary>
/// <typeparam name="TAsset">
/// Concrete retained asset type.
/// </typeparam>
public sealed class AssetLease<TAsset> : IDisposable
    where TAsset : AssetObject
{
    private readonly ResidencyLease<TAsset> m_residency;

    internal AssetLease(TAsset asset, Action release)
    {
        m_residency = new ResidencyLease<TAsset>(asset, release);
    }

    /// <summary>
    /// Gets the retained canonical asset while this lease is active.
    /// </summary>
    /// <exception cref="ObjectDisposedException">
    /// Thrown after the lease has been released.
    /// </exception>
    public TAsset asset
        => m_residency.value;

    /// <summary>
    /// Releases this caller's residency ownership, retaining its value and callback while the provider reports Pending.
    /// </summary>
    /// <remarks>
    /// Pending may be wrapped in another exception; classify the complete failure with RetirementPendingException.Find.
    /// Ordinary sibling failures remain observable after a later release attempt succeeds.
    /// </remarks>
    /// <exception cref="RetirementPendingException">
    /// Provider retirement is unfinished or a concurrent release is in progress; retain this lease and retry at a safe point.
    /// </exception>
    /// <exception cref="AggregateException">
    /// The provider reports a wrapped failure, or release completes with ordinary errors retained from earlier pending attempts.
    /// </exception>
    public void Dispose() => m_residency.Dispose();
}

/// <summary>
/// Keeps one verified immutable artifact generation available for an explicit lifetime.
/// </summary>
public sealed class ArtifactLease : IDisposable
{
    private readonly ResidencyLease<AssetArtifactInfo> m_residency;

    internal ArtifactLease(AssetArtifactInfo artifact, Action release)
    {
        m_residency = new ResidencyLease<AssetArtifactInfo>(artifact, release);
    }

    /// <summary>
    /// Gets verified metadata for the retained artifact.
    /// </summary>
    public AssetArtifactInfo info
        => m_residency.value;

    /// <summary>
    /// Opens a read-only stream over the retained immutable artifact.
    /// </summary>
    /// <returns>
    /// A new independently owned read stream.
    /// </returns>
    public Stream OpenRead()
        => File.Open(info.absolutePath, FileMode.Open, FileAccess.Read, FileShare.Read);

    /// <summary>
    /// Releases artifact ownership, retaining metadata and callback while the provider reports Pending.
    /// </summary>
    /// <remarks>
    /// Pending may be wrapped in another exception; classify the complete failure with RetirementPendingException.Find.
    /// Ordinary sibling failures remain observable after a later release attempt succeeds.
    /// </remarks>
    /// <exception cref="RetirementPendingException">
    /// Provider retirement is unfinished or a concurrent release is in progress; retain this lease and retry at a safe point.
    /// </exception>
    /// <exception cref="AggregateException">
    /// The provider reports a wrapped failure, or release completes with ordinary errors retained from earlier pending attempts.
    /// </exception>
    public void Dispose() => m_residency.Dispose();
}

/// <summary>
/// Owns multiple asset, artifact, or subsystem leases as one reverse-order lifetime.
/// </summary>
public sealed class RetentionScope : IDisposable
{
    private readonly LifetimeScope m_lifetime = new();

    /// <summary>
    /// Transfers one lease into this scope.
    /// </summary>
    /// <typeparam name="TLease">
    /// Disposable lease type.
    /// </typeparam>
    /// <param name="lease">
    /// Lease whose disposal ownership transfers to this scope.
    /// </param>
    /// <returns>
    /// The same lease for convenient initialization.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Retirement has begun and the scope cannot accept another lease.
    /// </exception>
    public TLease Retain<TLease>(TLease lease)
        where TLease : IDisposable
    {
        return m_lifetime.Own(lease);
    }

    /// <summary>
    /// Releases all retained leases in reverse acquisition order.
    /// </summary>
    /// <exception cref="AggregateException">
    /// Thrown after every lease is attempted when one or more releases fail.
    /// </exception>
    /// <exception cref="RetirementPendingException">
    /// A lease is not retired. The current and remaining dependencies are retained for retry.
    /// </exception>
    public void Dispose() => m_lifetime.Dispose();
}

/// <summary>
/// Reports current explicit runtime asset residency accounting.
/// </summary>
public readonly record struct AssetResidencyStatistics
{
    /// <summary>
    /// Creates one immutable residency snapshot.
    /// </summary>
    /// <param name="residentAssetCount">
    /// Number of currently materialized canonical assets.
    /// </param>
    /// <param name="residentBytes">
    /// Runtime payload bytes retained by materialized assets.
    /// </param>
    /// <param name="budgetBytes">
    /// Configured runtime payload budget.
    /// </param>
    public AssetResidencyStatistics(int residentAssetCount, long residentBytes, long budgetBytes)
    {
        this.residentAssetCount = residentAssetCount;
        this.residentBytes = residentBytes;
        this.budgetBytes = budgetBytes;
    }

    /// <summary>
    /// Gets the number of materialized canonical assets.
    /// </summary>
    public int residentAssetCount { get; }

    /// <summary>
    /// Gets retained runtime payload bytes.
    /// </summary>
    public long residentBytes { get; }

    /// <summary>
    /// Gets the configured runtime payload budget.
    /// </summary>
    public long budgetBytes { get; }
}
