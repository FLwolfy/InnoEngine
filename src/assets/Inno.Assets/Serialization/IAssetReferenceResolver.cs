using System;

using Inno.Assets;

namespace Inno.Assets;

/// <summary>
/// Resolves persistent asset references against one isolated asset database generation.
/// </summary>
public interface IAssetReferenceResolver
{
    /// <summary>
    /// Resolves one serialized asset reference to the canonical object owned by this resolver.
    /// </summary>
    /// <param name="persistentId">
    /// The persistent asset identity encoded in the reference.
    /// </param>
    /// <param name="stableTypeId">
    /// The stable concrete asset type identity encoded in the reference.
    /// </param>
    /// <param name="lastKnownPath">
    /// The optional logical path retained for diagnostics and authoring recovery.
    /// </param>
    /// <param name="expectedType">
    /// The declared property type that the resolved object must satisfy.
    /// </param>
    /// <param name="propertyPath">
    /// The serialization property path used when reporting resolution failures.
    /// </param>
    /// <returns>
    /// The canonical compatible asset owned by this resolver's generation.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the reference cannot be resolved to a compatible asset.
    /// </exception>
    AssetObject Resolve(
        Guid persistentId,
        Guid stableTypeId,
        string lastKnownPath,
        Type expectedType,
        string propertyPath);
}
