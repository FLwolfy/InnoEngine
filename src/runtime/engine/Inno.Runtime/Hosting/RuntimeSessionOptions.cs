using Inno.Runtime.Contracts;
using System;
using System.Collections.Generic;

using Inno.References;

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
    /// Gets or initializes the runtime asset payload residency budget in bytes.
    /// </summary>
    public long assetResidencyBudgetBytes { get; init; } = 256L * 1024L * 1024L;

    /// <summary>
    /// Gets or initializes the maximum simultaneous cold asset payload preparation bytes before admission is rejected.
    /// </summary>
    public long assetPreparationBudgetBytes { get; init; } = 64L * 1024L * 1024L;

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

    /// <summary>
    /// Gets or initializes the backend-neutral subsystem factories composed into each session.
    /// </summary>
    public Func<RuntimeSession, IReadOnlyList<IRuntimeSubsystemFactory>> createSubsystems { get; init; }
        = static _ => Array.Empty<IRuntimeSubsystemFactory>();

    /// <summary>
    /// Gets or initializes capabilities verified by composition for this session's selected services.
    /// </summary>
    public IReadOnlyList<RuntimeCapabilityId> capabilities { get; init; } = Array.Empty<RuntimeCapabilityId>();

    /// <summary>
    /// Gets or initializes owner-provided resolvers included in this session's immutable reference catalog.
    /// </summary>
    /// <remarks>
    /// A deployed <c>AssetDatabase</c> automatically contributes its asset resolver and must not be repeated here.
    /// </remarks>
    public IReadOnlyList<IReferenceResolver> referenceResolvers { get; init; }
        = Array.Empty<IReferenceResolver>();
}
