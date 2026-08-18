using System;

namespace Inno.Core.Reflection;

/// <summary>
/// Provides the previous and candidate type catalogs during an assembly reload transaction.
/// </summary>
public sealed class TypeCacheReloadContext
{
    private TypeCacheSnapshot? m_previous;
    private TypeCacheSnapshot? m_candidate;

    internal TypeCacheReloadContext(TypeCacheSnapshot previous, TypeCacheSnapshot candidate)
    {
        m_previous = previous;
        m_candidate = candidate;
    }

    /// <summary>
    /// Gets the active type snapshot from before activation.
    /// </summary>
    public TypeCacheSnapshot previous => m_previous ?? throw CreateCompletedException();

    /// <summary>
    /// Gets the validated candidate type snapshot.
    /// </summary>
    public TypeCacheSnapshot candidate => m_candidate ?? throw CreateCompletedException();

    /// <summary>
    /// Determines whether a runtime type belongs to the retiring generation.
    /// </summary>
    /// <param name="type">The runtime type to inspect.</param>
    /// <returns><see langword="true"/> when the candidate replaces or removes the type.</returns>
    public bool IsRetiredType(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return previous.TryGetStableTypeId(type, out Guid stableTypeId) &&
               (!candidate.TryGetStableTypeId(type, out _) ||
                candidate.TryResolveType(stableTypeId, out Type? replacement) && replacement != type);
    }

    /// <summary>
    /// Tries to find the candidate type that preserves a retiring type's stable identity.
    /// </summary>
    /// <param name="previousType">A type from the previous generation.</param>
    /// <param name="replacement">Receives its candidate replacement when found.</param>
    /// <returns><see langword="true"/> when a distinct replacement exists.</returns>
    public bool TryResolveReplacement(Type previousType, out Type? replacement)
    {
        ArgumentNullException.ThrowIfNull(previousType);
        if (!previous.TryGetStableTypeId(previousType, out Guid stableTypeId))
        {
            replacement = null;
            return false;
        }

        return candidate.TryResolveType(stableTypeId, out replacement) && replacement != previousType;
    }

    internal void Release()
    {
        m_previous = null;
        m_candidate = null;
    }

    private static InvalidOperationException CreateCompletedException()
        => new("The type-cache reload context is unavailable after the transaction has completed.");
}
