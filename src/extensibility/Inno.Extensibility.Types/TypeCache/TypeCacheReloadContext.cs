using System;

namespace Inno.Extensibility.Types;

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
    /// Determines whether a logical type belongs to the retiring generation.
    /// </summary>
    /// <param name="typeRef">
    /// The logical type to inspect.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the candidate replaces or removes the type.
    /// </returns>
    public bool IsRetired(TypeRef typeRef)
    {
        if (!previous.TryResolve(typeRef, out Type? previousType))
            return false;
        return !candidate.TryResolve(typeRef, out Type? replacement) || replacement != previousType;
    }

    /// <summary>
    /// Tries to find the candidate type that preserves a retiring type's stable identity.
    /// </summary>
    /// <param name="previousType">
    /// A type reference from the previous generation.
    /// </param>
    /// <param name="replacement">
    /// Receives its candidate replacement when found.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when a distinct replacement exists.
    /// </returns>
    public bool TryResolveReplacement(TypeRef previousType, out TypeRef replacement)
    {
        if (!previous.TryResolve(previousType, out Type? previousRuntimeType) ||
            !candidate.TryResolve(previousType, out Type? replacementType) ||
            replacementType == previousRuntimeType)
        {
            replacement = default;
            return false;
        }
        replacement = candidate.GetTypeRef(replacementType!);
        return true;
    }

    internal void Release()
    {
        m_previous = null;
        m_candidate = null;
    }

    private static InvalidOperationException CreateCompletedException()
        => new("The type-cache reload context is unavailable after the transaction has completed.");
}
