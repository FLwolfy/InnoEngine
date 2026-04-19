using System;
using System.Threading;

namespace Inno.Core.Job;

/// <summary>
/// Lifecycle manager for the process-wide active <see cref="IJobSystem"/> instance.
/// </summary>
public static class JobSystemManager
{
    private static readonly Lock S_LIFECYCLE_LOCK = new();
    private static IJobSystem? s_jobSystem;
    private static bool s_initialized;

    /// <summary>
    /// Initializes manager lifecycle state.
    /// </summary>
    public static void Initialize()
    {
        lock (S_LIFECYCLE_LOCK)
        {
            s_initialized = true;
        }
    }

    /// <summary>
    /// Sets the active job system instance.
    /// </summary>
    /// <param name="jobSystem">Job system instance.</param>
    public static void SetJobSystem(IJobSystem jobSystem)
    {
        ArgumentNullException.ThrowIfNull(jobSystem);

        lock (S_LIFECYCLE_LOCK)
        {
            if (!s_initialized)
            {
                throw new InvalidOperationException("JobSystemManager is not initialized.");
            }

            if (ReferenceEquals(s_jobSystem, jobSystem))
            {
                return;
            }

            s_jobSystem?.Dispose();
            s_jobSystem = jobSystem;
        }
    }

    /// <summary>
    /// Gets the active job system instance.
    /// </summary>
    public static IJobSystem current
    {
        get
        {
            lock (S_LIFECYCLE_LOCK)
            {
                if (!s_initialized || s_jobSystem is null)
                {
                    throw new InvalidOperationException("JobSystem is not initialized.");
                }

                return s_jobSystem;
            }
        }
    }

    /// <summary>
    /// Shuts down manager lifecycle and disposes the active job system.
    /// </summary>
    public static void Shutdown()
    {
        lock (S_LIFECYCLE_LOCK)
        {
            s_jobSystem?.Dispose();
            s_jobSystem = null;
            s_initialized = false;
        }
    }
}
