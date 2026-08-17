using System;

using Inno.Core.Assemblies.Internal;

namespace Inno.Core.Assemblies;

/// <summary>
/// Controls activation, completion, and rollback of one prepared module generation.
/// </summary>
public sealed class AssemblyReloadSession : IDisposable
{
    private ReloadState? m_state;
    private bool m_disposed;

    internal AssemblyReloadSession(ReloadState state)
    {
        m_state = state;
        context = new AssemblyReloadContext(
            state.previousTypes,
            state.candidateTypes,
            state.previousModule.handle);
    }

    /// <summary>
    /// Gets the old and candidate type snapshots used for state migration.
    /// </summary>
    public AssemblyReloadContext context { get; }

    /// <summary>
    /// Atomically publishes the candidate module, TypeCache, and registry snapshots.
    /// </summary>
    public void Activate()
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
        AssemblyManager.Activate(m_state!);
    }

    /// <summary>
    /// Commits an activated reload and begins cooperative unload of the old generation.
    /// </summary>
    public AssemblyUnloadMonitor Complete()
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
        AssemblyUnloadMonitor monitor = AssemblyManager.Complete(m_state!);
        context.Release();
        m_state = null;
        m_disposed = true;
        return monitor;
    }

    /// <summary>
    /// Restores the previous generation and unloads the candidate generation.
    /// </summary>
    public void Rollback()
    {
        if (m_disposed)
            return;
        AssemblyManager.Rollback(m_state!);
        context.Release();
        m_state = null;
        m_disposed = true;
    }

    /// <summary>
    /// Rolls back an incomplete session.
    /// </summary>
    public void Dispose()
    {
        Rollback();
        GC.SuppressFinalize(this);
    }
}

internal sealed class ReloadState
{
    internal required AssemblyModuleEntry previousModule { get; init; }
    internal required AssemblyModuleEntry candidateModule { get; init; }
    internal required Inno.Core.Reflection.TypeCacheSnapshot previousTypes { get; init; }
    internal required Inno.Core.Reflection.TypeCacheSnapshot candidateTypes { get; init; }
    internal required RegistryRefreshSet registryRefresh { get; init; }
    internal bool activated { get; set; }
    internal bool finished { get; set; }
}
