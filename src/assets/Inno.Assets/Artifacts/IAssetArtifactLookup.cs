using System;

namespace Inno.Assets;

/// <summary>
/// Resolves verified named outputs from immutable asset artifact bundles.
/// </summary>
public interface IAssetArtifactLookup
{
    /// <summary>
    /// Tries to resolve one named immutable artifact output by persistent asset identity.
    /// </summary>
    /// <param name="persistentId">
    /// Persistent identity of the artifact owner.
    /// </param>
    /// <param name="outputName">
    /// Exact stable artifact output name.
    /// </param>
    /// <param name="artifact">
    /// Receives verified output metadata and its absolute immutable path when available.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the artifact output exists and passes integrity validation.
    /// </returns>
    bool TryGetArtifact(
        Guid persistentId,
        string outputName,
        out AssetArtifactInfo? artifact);
}
