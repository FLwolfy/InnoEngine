using System;
using System.Collections.Generic;

namespace Inno.References;

/// <summary>
/// Owns one complete immutable generation of reference resolvers.
/// </summary>
public sealed class ReferenceCatalog
{
    private readonly IReadOnlyDictionary<ReferenceKindId, IReferenceResolver> m_resolvers;

    private ReferenceCatalog(long generation, IReadOnlyDictionary<ReferenceKindId, IReferenceResolver> resolvers)
    {
        this.generation = generation;
        m_resolvers = resolvers;
    }

    /// <summary>
    /// Gets an empty initial reference catalog.
    /// </summary>
    public static ReferenceCatalog empty { get; } = new(0, new Dictionary<ReferenceKindId, IReferenceResolver>());

    /// <summary>
    /// Gets the owner-assigned generation represented by this snapshot.
    /// </summary>
    public long generation { get; }

    /// <summary>
    /// Builds a complete resolver generation and rejects duplicate protocol identifiers.
    /// </summary>
    /// <param name="generation">
    /// The positive generation assigned by the catalog owner.
    /// </param>
    /// <param name="resolvers">
    /// Every resolver that belongs to the candidate generation.
    /// </param>
    /// <returns>
    /// An immutable reference catalog ready for publication.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="generation"/> is not positive.
    /// </exception>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="resolvers"/> or one of its elements is null.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when a resolver kind is invalid or duplicated.
    /// </exception>
    public static ReferenceCatalog Create(long generation, IEnumerable<IReferenceResolver> resolvers)
    {
        if (generation <= 0)
            throw new ArgumentOutOfRangeException(nameof(generation), "A published reference catalog requires a positive generation.");
        ArgumentNullException.ThrowIfNull(resolvers);
        var byKind = new Dictionary<ReferenceKindId, IReferenceResolver>();
        foreach (IReferenceResolver? resolver in resolvers)
        {
            ArgumentNullException.ThrowIfNull(resolver);
            if (!resolver.kindId.isValid)
                throw new InvalidOperationException("A reference resolver declared an invalid kind identifier.");
            if (!byKind.TryAdd(resolver.kindId, resolver))
                throw new InvalidOperationException($"Reference kind '{resolver.kindId}' has more than one resolver.");
        }
        return new ReferenceCatalog(generation, byKind);
    }

    /// <summary>
    /// Resolves a persistent descriptor using the resolver selected by its kind.
    /// </summary>
    /// <param name="descriptor">
    /// The descriptor to resolve.
    /// </param>
    /// <returns>
    /// The resolver result, an unassigned result for an empty target, or a missing result when no resolver is installed.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="descriptor"/> is null.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when a resolver returns a result for a different descriptor.
    /// </exception>
    public ReferenceResolution Resolve(ReferenceDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        if (descriptor.isUnassigned)
            return new ReferenceResolution(descriptor, ReferenceResolutionState.Unassigned);
        if (!m_resolvers.TryGetValue(descriptor.kindId, out IReferenceResolver? resolver))
        {
            return new ReferenceResolution(
                descriptor,
                ReferenceResolutionState.Missing,
                diagnostic: $"No resolver for reference kind '{descriptor.kindId}' is active in generation {generation}.");
        }
        ReferenceResolution result = resolver.Resolve(descriptor)
            ?? throw new InvalidOperationException($"Reference resolver '{descriptor.kindId}' returned null.");
        if (!ReferenceEquals(result.descriptor, descriptor))
            throw new InvalidOperationException($"Reference resolver '{descriptor.kindId}' replaced the supplied descriptor.");
        return result;
    }
}
