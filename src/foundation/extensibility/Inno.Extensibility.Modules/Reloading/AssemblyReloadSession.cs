using System;
using System.Linq;

using Inno.Core.Execution;
using Inno.Extensibility.Modules.Internal;
using Inno.Extensibility.Reload;

namespace Inno.Extensibility.Modules;

/// <summary>
/// Controls activation, completion, and rollback of one prepared module generation.
/// </summary>
public sealed class AssemblyReloadSession : IDisposable, IGenerationPublication<AssemblyUnloadMonitor>
{
    private readonly ModuleHost m_owner;
    private ReloadState? m_state;
    private bool m_disposed;
    private Exception? m_retirementFailure;

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
    /// Gets the owning host's shared generation gate, including discarded-candidate retirements.
    /// </summary>
    public GenerationCoordinator generations => m_owner.generations;

    /// <summary>
    /// Atomically publishes the candidate module and all participant snapshots.
    /// </summary>
    /// <exception cref="RetirementPendingException">
    /// A participant could not quiesce. Both generations remain owned and the host must restart.
    /// </exception>
    public void Activate()
    {
        EnsureRetirementSafe();
        ObjectDisposedException.ThrowIf(m_disposed, this);
        m_owner.Activate(m_state!);
    }

    /// <summary>
    /// Commits an activated reload and begins cooperative unload of the old generation.
    /// </summary>
    /// <returns>
    /// The validated assembly unload monitor that represents the completed operation.
    /// </returns>
    /// <exception cref="RetirementPendingException">
    /// A participant still uses its dependencies. Contexts remain owned and no unload monitor is issued.
    /// </exception>
    public AssemblyUnloadMonitor Complete()
    {
        EnsureRetirementSafe();
        ObjectDisposedException.ThrowIf(m_disposed, this);
        try { return m_owner.Complete(m_state!); }
        catch (Exception failure) when (RetirementPendingException.Find(failure) is not null)
        {
            m_retirementFailure = failure;
            throw;
        }
        finally
        {
            if (m_retirementFailure is null && m_state!.finished)
            {
                context.Release();
                m_state = null;
                m_disposed = true;
            }
        }
    }

    /// <summary>
    /// Restores the previous generation and unloads the candidate generation.
    /// </summary>
    /// <exception cref="RetirementPendingException">
    /// Rollback cannot safely release the candidate. Contexts remain owned and the shared gate is faulted.
    /// </exception>
    public void Rollback()
    {
        EnsureRetirementSafe();
        if (m_disposed)
            return;
        try { m_owner.Rollback(m_state!); }
        catch (Exception failure) when (RetirementPendingException.Find(failure) is not null)
        {
            m_retirementFailure = failure;
            throw;
        }
        finally
        {
            if (m_retirementFailure is null)
            {
                context.Release();
                m_state = null;
                m_disposed = true;
            }
        }
    }

    /// <summary>
    /// Rolls back an incomplete session.
    /// </summary>
    /// <exception cref="RetirementPendingException">
    /// The session still owns unfinished retirement and cannot be marked disposed.
    /// </exception>
    public void Dispose()
    {
        Rollback();
        GC.SuppressFinalize(this);
    }

    private void EnsureRetirementSafe()
    {
        if (m_retirementFailure is not null)
            throw m_retirementFailure;
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
