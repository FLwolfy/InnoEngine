using System;

using Inno.Core.Serialization;

namespace Inno.Assets;

/// <summary>
/// Creates complete asset-aware converter contexts from an owner-provided reference generation.
/// </summary>
public static class AssetSerializationContext
{
    /// <summary>
    /// Creates a context containing the required asset resolver and an optional dependency collector.
    /// </summary>
    /// <param name="references">
    /// The authoritative asset reference resolver for the operation's complete generation.
    /// </param>
    /// <param name="dependencies">
    /// Optional collector that records direct asset dependencies during serialization.
    /// </param>
    /// <returns>
    /// A complete immutable asset-aware serialization context.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="references"/> is null.
    /// </exception>
    public static SerializationContext Create(
        IAssetReferenceResolver references,
        AssetDependencyCollection? dependencies = null)
    {
        ArgumentNullException.ThrowIfNull(references);
        SerializationContext context = SerializationContext.empty
            .With<IAssetReferenceResolver>(references);
        return dependencies is null ? context : context.With(dependencies);
    }
}
