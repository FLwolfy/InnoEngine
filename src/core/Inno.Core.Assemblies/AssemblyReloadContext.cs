using System;

using Inno.Core.Reflection;

namespace Inno.Core.Assemblies;

/// <summary>
/// Provides old and candidate type snapshots to a state-migration coordinator.
/// </summary>
public sealed class AssemblyReloadContext
{
    private TypeCacheSnapshot? m_previousTypes;
    private TypeCacheSnapshot? m_candidateTypes;

    internal AssemblyReloadContext(
        TypeCacheSnapshot previousTypes,
        TypeCacheSnapshot candidateTypes,
        AssemblyModuleHandle module)
    {
        m_previousTypes = previousTypes;
        m_candidateTypes = candidateTypes;
        this.module = module;
    }

    /// <summary>
    /// Gets the active snapshot from before activation.
    /// </summary>
    public TypeCacheSnapshot previousTypes => m_previousTypes ?? throw CreateCompletedException();

    /// <summary>
    /// Gets the validated candidate snapshot.
    /// </summary>
    public TypeCacheSnapshot candidateTypes => m_candidateTypes ?? throw CreateCompletedException();

    /// <summary>
    /// Gets the logical module being reloaded.
    /// </summary>
    public AssemblyModuleHandle module { get; }

    /// <summary>
    /// Determines whether a runtime type belongs to the retiring generation.
    /// </summary>
    public bool IsRetiredType(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        TypeCacheSnapshot previous = previousTypes;
        TypeCacheSnapshot candidate = candidateTypes;
        return previous.TryGetStableTypeId(type, out Guid stableTypeId) &&
               (!candidate.TryGetStableTypeId(type, out _) ||
                candidate.TryResolveType(stableTypeId, out Type? replacement) && replacement != type);
    }

    /// <summary>
    /// Tries to find the candidate type that preserves a retiring type's stable identity.
    /// </summary>
    public bool TryResolveReplacement(Type previousType, out Type? replacement)
    {
        ArgumentNullException.ThrowIfNull(previousType);
        TypeCacheSnapshot previous = previousTypes;
        TypeCacheSnapshot candidate = candidateTypes;
        if (!previous.TryGetStableTypeId(previousType, out Guid stableTypeId))
        {
            replacement = null;
            return false;
        }

        return candidate.TryResolveType(stableTypeId, out replacement) && replacement != previousType;
    }

    internal void Release()
    {
        m_previousTypes = null;
        m_candidateTypes = null;
    }

    private static InvalidOperationException CreateCompletedException()
        => new("The assembly reload context is no longer available after the transaction has completed.");
}
