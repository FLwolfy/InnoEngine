using System;

namespace Inno.Core.Assemblies;

/// <summary>
/// Describes whether a collectible load context is still reachable.
/// </summary>
public enum AssemblyUnloadStatus
{
    /// <summary>
    /// At least one runtime reference still keeps the load context alive.
    /// </summary>
    Pending,

    /// <summary>
    /// The load context is no longer reachable, or the module was not collectible.
    /// </summary>
    Completed
}

/// <summary>
/// Observes cooperative unload completion without retaining the old load context.
/// </summary>
public sealed class AssemblyUnloadMonitor
{
    private readonly WeakReference? m_loadContext;

    internal AssemblyUnloadMonitor(WeakReference? loadContext)
    {
        m_loadContext = loadContext;
    }

    /// <summary>
    /// Gets whether the old load context is no longer reachable.
    /// </summary>
    public bool isCompleted => status == AssemblyUnloadStatus.Completed;

    /// <summary>
    /// Gets the current cooperative unload state.
    /// </summary>
    public AssemblyUnloadStatus status
        => m_loadContext is null || !m_loadContext.IsAlive
            ? AssemblyUnloadStatus.Completed
            : AssemblyUnloadStatus.Pending;
}
