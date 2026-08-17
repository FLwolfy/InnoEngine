using System;

namespace Inno.Core.Reflection;

/// <summary>
/// Stores the active immutable type snapshot without owning assembly lifecycle policy.
/// </summary>
internal static class TypeCacheManager
{
    private static readonly object S_SYNC = new();

    private static TypeCacheSnapshot s_current = TypeCacheSnapshot.empty;
    private static Action? s_refreshProvider;

    internal static bool isInitialized { get; private set; }

    internal static TypeCacheSnapshot current
    {
        get
        {
            EnsureFresh();
            lock (S_SYNC)
                return s_current;
        }
    }

    internal static TypeCacheSnapshot PeekCurrent()
    {
        lock (S_SYNC)
            return s_current;
    }

    internal static void ConfigureRefreshProvider(Action? refreshProvider)
    {
        lock (S_SYNC)
            s_refreshProvider = refreshProvider;
    }

    internal static void Commit(TypeCacheSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        lock (S_SYNC)
        {
            s_current = snapshot;
            isInitialized = true;
        }
    }

    internal static void Shutdown()
    {
        lock (S_SYNC)
        {
            s_refreshProvider = null;
            s_current = TypeCacheSnapshot.empty;
            isInitialized = false;
        }
    }

    private static void EnsureFresh()
    {
        Action? refreshProvider;
        lock (S_SYNC)
            refreshProvider = s_refreshProvider;
        refreshProvider?.Invoke();
    }
}
