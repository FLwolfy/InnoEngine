using System;
using System.Threading;
using System.Runtime.CompilerServices;
using Inno.Core.Reflection;

namespace Inno.Core.ECS;

/// <summary>
/// Opaque handle used to query entity views without repeatedly passing component type arrays.
/// </summary>
public struct EntityViewHandle
{
    private readonly WeakReference<World>? m_worldRef;
    private readonly int[]? m_componentTypeIds;
    private int m_validatedVersion;
    private bool m_cachedIsValid;

    private static int s_typeCacheVersion;
    
    [TypeCacheRebuild("Inno.Core.ECS")]
    private static void OnTypeCacheRefresh()
    {
        Interlocked.Increment(ref s_typeCacheVersion);
    }

    /// <summary>
    /// Returns <see langword="true"/> when the handle still belongs to a live world and all stored component runtime ids are currently resolvable.
    /// </summary>
    public bool isValid
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            if (m_worldRef == null || !m_worldRef.TryGetTarget(out _))
            {
                return false;
            }

            if (m_componentTypeIds == null || m_componentTypeIds.Length == 0)
            {
                return false;
            }

            int version = Volatile.Read(ref s_typeCacheVersion);
            if (m_validatedVersion == version)
            {
                return m_cachedIsValid;
            }

            bool valid = true;
            for (int i = 0; i < m_componentTypeIds.Length; i++)
            {
                if (!TypeCache.TryResolveType(m_componentTypeIds[i], out Type? type) || type == null)
                {
                    valid = false;
                    break;
                }
            }

            m_cachedIsValid = valid;
            m_validatedVersion = version;
            return valid;
        }
    }

    internal int[] GetComponentTypeIdsOrThrow(World world)
    {
        if (m_worldRef == null || !m_worldRef.TryGetTarget(out World? owner) || !ReferenceEquals(owner, world))
        {
            throw new InvalidOperationException("EntityViewHandle does not belong to this world, or its world is no longer alive.");
        }

        if (!isValid || m_componentTypeIds == null)
        {
            throw new InvalidOperationException("EntityViewHandle is no longer valid. Recreate it from component types.");
        }

        return m_componentTypeIds;
    }

    internal EntityViewHandle(WeakReference<World> worldRef, int[] componentTypeIds)
    {
        m_worldRef = worldRef;
        m_componentTypeIds = componentTypeIds;
        m_validatedVersion = -1;
        m_cachedIsValid = false;
    }
}
