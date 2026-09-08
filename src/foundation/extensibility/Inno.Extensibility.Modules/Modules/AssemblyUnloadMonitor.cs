using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Inno.Extensibility.Reload;

namespace Inno.Extensibility.Modules;

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
public sealed class AssemblyUnloadMonitor : IAssemblyUnloadProbe
{
    private readonly object m_sync = new();
    private readonly WeakReference? m_loadContext;
    private readonly string? m_shadowDirectory;
    private readonly AssemblyUnloadMonitor[]? m_children;
    private bool m_isShadowDirectoryCleaned;
    private readonly string m_description;

    internal AssemblyUnloadMonitor(
        WeakReference? loadContext,
        string? shadowDirectory = null,
        string? description = null)
    {
        m_loadContext = loadContext;
        m_shadowDirectory = shadowDirectory;
        m_description = description ?? "non-collectible assembly generation";
    }

    internal AssemblyUnloadMonitor(IReadOnlyList<AssemblyUnloadMonitor> children)
    {
        ArgumentNullException.ThrowIfNull(children);
        m_children = children.ToArray();
        m_description = string.Join(", ", m_children.Select(static child => child.description));
    }

    /// <summary>
    /// Gets a stable diagnostic description of the retired assembly generation.
    /// </summary>
    public string description => m_description;

    /// <summary>
    /// Gets whether the old load context is no longer reachable.
    /// </summary>
    public bool isCompleted => status == AssemblyUnloadStatus.Completed;

    /// <summary>
    /// Gets the current cooperative unload state.
    /// </summary>
    public AssemblyUnloadStatus status
    {
        get
        {
            if (m_children is not null)
                return m_children.All(static child => child.isCompleted)
                    ? AssemblyUnloadStatus.Completed
                    : AssemblyUnloadStatus.Pending;
            if (m_loadContext is not null && m_loadContext.IsAlive)
                return AssemblyUnloadStatus.Pending;
            return TryCleanupShadowDirectory()
                ? AssemblyUnloadStatus.Completed
                : AssemblyUnloadStatus.Pending;
        }
    }

    internal bool TryCleanupShadowDirectory()
    {
        if (m_children is not null)
            return m_children.All(static child => child.TryCleanupShadowDirectory());
        if (m_loadContext is not null && m_loadContext.IsAlive)
            return false;
        lock (m_sync)
        {
            if (m_isShadowDirectoryCleaned)
                return true;
            try
            {
                if (!string.IsNullOrWhiteSpace(m_shadowDirectory) && Directory.Exists(m_shadowDirectory))
                {
                    Directory.Delete(m_shadowDirectory, recursive: true);
                    string? moduleDirectory = Path.GetDirectoryName(m_shadowDirectory);
                    if (!string.IsNullOrWhiteSpace(moduleDirectory) &&
                        Directory.Exists(moduleDirectory) &&
                        !Directory.EnumerateFileSystemEntries(moduleDirectory).Any())
                    {
                        Directory.Delete(moduleDirectory);
                    }
                }
                m_isShadowDirectoryCleaned = true;
                return true;
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }
    }
}
