using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Inno.Storage;

/// <summary>
/// Provides asynchronous atomic access to one application-specific persistent sandbox.
/// </summary>
public interface IApplicationStorage
{
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
    ValueTask<bool> ExistsAsync(StorageKey key, CancellationToken cancellationToken = default);

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
    ValueTask<byte[]?> ReadAsync(StorageKey key, CancellationToken cancellationToken = default);

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
    ValueTask WriteAsync(
        StorageKey key,
        ReadOnlyMemory<byte> value,
        CancellationToken cancellationToken = default);

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
    ValueTask<bool> DeleteAsync(StorageKey key, CancellationToken cancellationToken = default);

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
    ValueTask<IReadOnlyList<StorageKey>> ListAsync(
        StorageKey? prefix = null,
        CancellationToken cancellationToken = default);
}
