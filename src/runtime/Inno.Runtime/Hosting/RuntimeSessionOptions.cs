using System;

namespace Inno.Runtime;

/// <summary>
/// Defines storage, scheduling, and timing policy for one runtime session.
/// </summary>
public sealed class RuntimeSessionOptions
{
    /// <summary>
    /// Gets or initializes the session role.
    /// </summary>
    public RuntimeSessionKind kind { get; init; } = RuntimeSessionKind.Edit;

    /// <summary>
    /// Gets or initializes the stable application identifier used to isolate persistent data.
    /// </summary>
    public string applicationId { get; init; } = "inno.application";

    /// <summary>
    /// Gets or initializes the optional materialized runtime content root.
    /// </summary>
    /// <remarks>
    /// Player sessions require this directory. Edit and Play sessions may omit it when an authoring asset
    /// service is composed by the Editor.
    /// </remarks>
    public string? runtimeContentDirectory { get; init; }

    /// <summary>
    /// Gets or initializes the writable application-specific persistent data root.
    /// </summary>
    public string persistentDataDirectory { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the fixed simulation interval in seconds.
    /// </summary>
    public float fixedDeltaTime { get; init; } = 1f / 60f;

    /// <summary>
    /// Gets or initializes the maximum accepted variable frame interval in seconds.
    /// </summary>
    public float maxFrameDeltaTime { get; init; } = 0.25f;

    /// <summary>
    /// Gets or initializes the maximum number of fixed updates performed by one frame tick.
    /// </summary>
    public int maxFixedStepsPerFrame { get; init; } = 8;

    /// <summary>
    /// Gets or initializes the job execution strategy owned by this session.
    /// </summary>
    public RuntimeJobExecutionMode jobExecutionMode { get; init; } = RuntimeJobExecutionMode.WorkerPool;

    /// <summary>
    /// Gets or initializes the worker count used by the work-stealing scheduler; zero selects the default.
    /// </summary>
    public int jobWorkerCount { get; init; }
}
