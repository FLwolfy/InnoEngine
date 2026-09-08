using Inno.Runtime.Contracts;
using System;
using Inno.Core.Execution;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Inno.Runtime;

namespace Inno.Storage.Runtime;

/// <summary>
/// Binds one application storage sandbox throughout every runtime frame.
/// </summary>
public sealed class StorageRuntime : RuntimeSubsystem, IApplicationStorage
{
    private readonly IApplicationStorage m_storage;
    private readonly LifetimeScope m_operations;
    private readonly int m_maxWriteBytes;

    /// <summary>
    /// Creates a feature whose optional disposable storage ownership transfers to this instance.
    /// </summary>
    /// <param name="storage">
    /// The application-specific persistent storage sandbox.
    /// </param>
    /// <param name="maxPendingOperations">
    /// Positive simultaneous asynchronous operation capacity.
    /// </param>
    /// <param name="maxWriteBytes">
    /// Positive maximum value size accepted by one write.
    /// </param>
    public StorageRuntime(IApplicationStorage storage, int maxPendingOperations = 128, int maxWriteBytes = 64 * 1024 * 1024)
    {
        m_storage = storage ?? throw new ArgumentNullException(nameof(storage));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxWriteBytes);
        m_maxWriteBytes = maxWriteBytes;
        m_operations = lifetime.Own(new LifetimeScope(maxPendingOperations));
        if (storage is IDisposable disposable) m_operations.Own(disposable);
    }

    /// <summary>
    /// Gets the storage service owned by this runtime feature.
    /// </summary>
    public IApplicationStorage storage => this;
    /// <summary>
    /// Gets asynchronous operations retained until completion or retirement reporting.
    /// </summary>
    public int pendingOperations => m_operations.trackedWorkCount;
    /// <summary>
    /// Gets operations rejected by finite asynchronous admission capacity.
    /// </summary>
    public long rejectedOperations => m_operations.rejectedWorkCount;
    /// <summary>
    /// Determines whether a value exists.
    /// </summary>
    /// <param name="key">
    /// The sandbox-relative storage key.
    /// </param>
    /// <param name="cancellationToken">
    /// Cancellation requested by the caller.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the value exists.
    /// </returns>
    public ValueTask<bool> ExistsAsync(StorageKey key, CancellationToken cancellationToken = default)
        => new(m_operations.RunAsync(token => m_storage.ExistsAsync(key, token), cancellationToken));
    /// <summary>
    /// Reads a complete immutable value.
    /// </summary>
    /// <param name="key">
    /// The sandbox-relative storage key.
    /// </param>
    /// <param name="cancellationToken">
    /// Cancellation requested by the caller.
    /// </param>
    /// <returns>
    /// The stored bytes, or <see langword="null"/> when the key does not exist.
    /// </returns>
    public ValueTask<byte[]?> ReadAsync(StorageKey key, CancellationToken cancellationToken = default)
        => new(m_operations.RunAsync(token => m_storage.ReadAsync(key, token), cancellationToken));
    /// <summary>
    /// Atomically replaces a complete value.
    /// </summary>
    /// <param name="key">
    /// The sandbox-relative storage key.
    /// </param>
    /// <param name="value">
    /// The complete value to commit.
    /// </param>
    /// <param name="cancellationToken">
    /// Cancellation requested by the caller.
    /// </param>
    /// <returns>
    /// A task that completes after the value is durably replaced.
    /// </returns>
    public ValueTask WriteAsync(StorageKey key, ReadOnlyMemory<byte> value, CancellationToken cancellationToken = default)
        => new(m_operations.RunAsync(async token =>
        {
            if (value.Length > m_maxWriteBytes) throw new ArgumentException("The storage value exceeds its write byte budget.", nameof(value));
            byte[] snapshot = value.ToArray();
            await m_storage.WriteAsync(key, snapshot, token).ConfigureAwait(false);
            return true;
        }, cancellationToken));
    /// <summary>
    /// Deletes one value without failing when it is already absent.
    /// </summary>
    /// <param name="key">
    /// The sandbox-relative storage key.
    /// </param>
    /// <param name="cancellationToken">
    /// Cancellation requested by the caller.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when an existing value was deleted.
    /// </returns>
    public ValueTask<bool> DeleteAsync(StorageKey key, CancellationToken cancellationToken = default)
        => new(m_operations.RunAsync(token => m_storage.DeleteAsync(key, token), cancellationToken));
    /// <summary>
    /// Lists immutable keys below an optional logical prefix.
    /// </summary>
    /// <param name="prefix">
    /// An optional normalized directory prefix.
    /// </param>
    /// <param name="cancellationToken">
    /// Cancellation requested by the caller.
    /// </param>
    /// <returns>
    /// Keys in deterministic ordinal order.
    /// </returns>
    public ValueTask<IReadOnlyList<StorageKey>> ListAsync(StorageKey? prefix = null, CancellationToken cancellationToken = default)
        => new(m_operations.RunAsync(token => m_storage.ListAsync(prefix, token), cancellationToken));

    /// <summary>
    /// Begins a frame-scoped operation and makes queued work visible.
    /// </summary>
    /// <param name="frame">
    /// The frame consumed by begin frame; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    protected override void OnBeginFrame(RuntimeFrame frame)
    {
        OwnFrameScope(StorageExecutionContext.EnterScope(this));
    }
    /// <summary>
    /// Releases the storage backend after owned work has quiesced.
    /// </summary>
    protected override void OnStop()
    {
        m_operations.Dispose();
    }
}
