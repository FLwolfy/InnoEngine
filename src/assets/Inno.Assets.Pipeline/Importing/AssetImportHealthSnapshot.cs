using System;
using System.Collections.Generic;

namespace Inno.Assets.Pipeline;

/// <summary>
/// Represents an immutable observation of writable-source import failures at one point in time.
/// </summary>
/// <remarks>
/// The snapshot deliberately keeps importer fingerprints private to the Asset Pipeline. Callers can
/// compare snapshots without depending on catalog records, importer implementation names, or storage
/// details.
/// </remarks>
public sealed class AssetImportHealthSnapshot
{
    private static readonly AssetImportHealthSnapshot S_EMPTY = new(
        new HashSet<AssetImportFailureFingerprint>());

    private readonly IReadOnlySet<AssetImportFailureFingerprint> m_failures;

    internal AssetImportHealthSnapshot(IReadOnlySet<AssetImportFailureFingerprint> failures)
    {
        ArgumentNullException.ThrowIfNull(failures);
        m_failures = failures;
    }

    internal IReadOnlySet<AssetImportFailureFingerprint> failures => m_failures;

    /// <summary>
    /// Gets the immutable snapshot used when no Asset Pipeline generation is active.
    /// </summary>
    public static AssetImportHealthSnapshot empty => S_EMPTY;
}

/// <summary>
/// Describes a writable-source import failure introduced after an earlier health snapshot.
/// </summary>
/// <param name="assetPath">
/// The isolated path of the source that failed to import.
/// </param>
/// <param name="diagnostics">
/// The deterministic importer diagnostics associated with the failure.
/// </param>
public readonly record struct AssetImportFailure(
    string assetPath,
    string diagnostics);
