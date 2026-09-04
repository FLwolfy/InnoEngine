using System;
using System.Linq;

using Inno.Extensibility.Modules.Internal;

namespace Inno.Extensibility.Modules;

/// <summary>
/// Controls activation, completion, and rollback of one prepared module generation.
/// </summary>
public sealed class AssemblyReloadSession : IDisposable
{
    private readonly ModuleHost m_owner;
    private ReloadState? m_state;
    private bool m_disposed;

    internal AssemblyReloadSession(ModuleHost owner, ReloadState state)
    {
        m_owner = owner;
        m_state = state;
        context = new AssemblyReloadContext(
            state.previousCatalog,
            state.candidateCatalog,
            state.candidateModules.Select(static module => module.handle).ToArray(),
            state.refresh.contexts);
    }

    /// <summary>
    /// Gets the old and candidate assembly catalogs and participant migration contexts.
    /// </summary>
    public AssemblyReloadContext context { get; }

    /// <summary>
    /// Atomically publishes the candidate module and all participant snapshots.
    /// </summary>
    public void Activate()
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
        m_owner.Activate(m_state!);
    }

    /// <summary>
    /// Commits an activated reload and begins cooperative unload of the old generation.
    /// </summary>
    /// <returns>
    /// The validated assembly unload monitor that represents the completed operation.
    /// </returns>
    public AssemblyUnloadMonitor Complete()
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
        AssemblyUnloadMonitor monitor = m_owner.Complete(m_state!);
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
        m_owner.Rollback(m_state!);
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
    internal required AssemblyModuleEntry?[] previousModules { get; init; }
    internal required AssemblyModuleEntry[] removedModules { get; init; }
    internal required AssemblyModuleEntry[] candidateModules { get; init; }
    internal required AssemblyCatalogSnapshot previousCatalog { get; init; }
    internal required AssemblyCatalogSnapshot candidateCatalog { get; init; }
    internal required AssemblyCatalogRefreshSet refresh { get; init; }
    internal bool activated { get; set; }
    internal bool finished { get; set; }
}
