using Inno.Runtime.Contracts;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Inno.Assets;
using Inno.Core.Coroutines;
using Inno.Core.Diagnostics;
using Inno.Core.Events;
using Inno.Core.Execution;
using Inno.Core.Identity;
using Inno.Core.Jobs;
using Inno.Core.Logging;
using Inno.Core.Serialization;
using Inno.References;
using Inno.Scene;

namespace Inno.Runtime;

/// <summary>
/// Owns all mutable simulation, identity, asset, scheduling, and logging state for one isolated execution session.
/// </summary>
public sealed class RuntimeSession : IDisposable
{
    private readonly EngineHost m_host;
    private readonly IdentityAllocator m_identities;
    private JobScheduler m_jobs = null!;
    private readonly CoroutineScheduler m_coroutines;
    private readonly RuntimeClock m_clock = new();
    private RuntimeSubsystemPipeline? m_subsystems;
    private SerializationGeneration? m_serialization;
    private SessionFileLogSink m_fileLog = null!;
    private AssetDatabase? m_assets;
    private ReferenceCatalog m_references = ReferenceCatalog.empty;
    private readonly RetirementBarrier m_retirement;
    private List<Exception>? m_retirementFailures;
    private float m_fixedAccumulator;
    private bool m_disposed;

    internal RuntimeSession(EngineHost host, RuntimeSessionOptions options)
    {
        m_host = host;
        this.options = Validate(options);
        sessionId = LogSessionId.Create();
        m_identities = new IdentityAllocator();
        m_coroutines = new CoroutineScheduler();
        events = new EventDispatcher();
        m_retirement = new RetirementBarrier($"Runtime session {sessionId}", host.retirementTimeout);
    }

    internal void Start()
    {
        m_jobs = new JobScheduler(
            options.jobExecutionMode switch
            {
                RuntimeJobExecutionMode.SingleThread => JobExecutionMode.SingleThread,
                RuntimeJobExecutionMode.WorkerPool => JobExecutionMode.WorkerPool,
                _ => throw new ArgumentOutOfRangeException(
                    nameof(options),
                    options.jobExecutionMode,
                    "Unknown runtime job execution mode.")
            },
            new JobSchedulerOptions
            {
                workerCount = options.jobWorkerCount
            });
        scenes = new SceneWorld(m_identities, m_host.types);
        Directory.CreateDirectory(this.options.persistentDataDirectory);
        m_fileLog = new SessionFileLogSink(
            sessionId,
            new FileLogSink(Path.Combine(this.options.persistentDataDirectory, "Logs")));
        m_host.logs.RegisterSink(m_fileLog);
        using IDisposable scope = EnterExecutionScope();
        if (!string.IsNullOrWhiteSpace(options.runtimeContentDirectory))
        {
            m_serialization = m_host.serialization.CaptureGeneration();
            m_assets = new AssetDatabase(
                options.runtimeContentDirectory,
                m_serialization,
                m_host.types.current,
                m_identities,
                options.assetResidencyBudgetBytes,
                options.assetPreparationBudgetBytes);
        }
        IReferenceResolver[] referenceResolvers = m_assets is IReferenceResolver assetResolver
            ? [.. options.referenceResolvers, assetResolver]
            : [.. options.referenceResolvers];
        m_references = referenceResolvers.Length == 0
            ? ReferenceCatalog.empty
            : ReferenceCatalog.Create(1, referenceResolvers);
        var context = new RuntimeSubsystemContext(events, m_host.diagnostics, m_identities, m_host.types,
            new LifetimeScope(), RuntimeSubsystemLifetime.Session, options.persistentDataDirectory,
            options.kind == RuntimeSessionKind.Edit, options.capabilities);
        m_subsystems = new RuntimeSubsystemPipeline(context, m_host.retirementTimeout, m_host.generations);
        m_subsystems.Start([new SceneRuntimeSubsystemFactory(scenes, options.kind), .. options.createSubsystems(this)]);
    }

    /// <summary>
    /// Gets the validated immutable options used to create this session.
    /// </summary>
    public RuntimeSessionOptions options { get; }

    /// <summary>
    /// Gets the unique logging identity assigned to this session.
    /// </summary>
    public LogSessionId sessionId { get; }

    /// <summary>
    /// Gets the event dispatcher owned by this session.
    /// </summary>
    public EventDispatcher events { get; }

    /// <summary>
    /// Gets the identity domain that owns every live object in this isolated session.
    /// </summary>
    public IdentityAllocator identities => m_identities;

    /// <summary>
    /// Gets the immutable cross-domain reference resolver generation owned by this session.
    /// </summary>
    public ReferenceCatalog references => m_references;

    /// <summary>
    /// Gets the dependency-ordered subsystem generation owned by this session.
    /// </summary>
    public RuntimeSubsystemPipeline subsystems
        => m_subsystems ?? throw new InvalidOperationException("Runtime subsystems are not initialized.");

    /// <summary>
    /// Gets or sets the finite non-negative simulation time multiplier.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when the assigned value is negative, NaN, or infinite.
    /// </exception>
    public float timeScale
    {
        get => m_clock.timeScale;
        set => m_clock.SetTimeScale(value);
    }

    /// <summary>
    /// Gets or sets whether scaled simulation is paused while unscaled subsystems continue to receive frames.
    /// </summary>
    public bool isPaused
    {
        get => m_clock.isPaused;
        set => m_clock.SetPaused(value);
    }

    /// <summary>
    /// Gets the isolated scene world owned by this session.
    /// </summary>
    public SceneWorld scenes { get; private set; } = null!;

    /// <summary>
    /// Gets the source-free runtime asset database configured for this session.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when this Edit or Play session was composed without a runtime content deployment.
    /// </exception>
    public AssetDatabase assets
        => m_assets ?? throw new InvalidOperationException(
            "This runtime session was created without a deployed asset database.");

    /// <summary>
    /// Binds this session's script façades to the current asynchronous execution context.
    /// </summary>
    /// <returns>
    /// A strict last-in-first-out scope owned by the caller.
    /// </returns>
    /// <exception cref="ObjectDisposedException">
    /// Thrown when this session has been disposed.
    /// </exception>
    public IDisposable EnterExecutionScope()
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
        var scopes = new List<IDisposable>(6);
        try
        {
            scopes.Add(m_host.logs.EnterScope());
            scopes.Add(m_host.diagnostics.EnterScope());
            scopes.Add(LogSessionContext.Enter(sessionId));
            scopes.Add(scenes.EnterScope());
            scopes.Add(m_clock.EnterScope());
            if (m_assets is not null)
                scopes.Add(AssetExecutionContext.EnterScope(m_assets));
            return new ExecutionScope(scopes);
        }
        catch
        {
            for (int index = scopes.Count - 1; index >= 0; index--)
                scopes[index].Dispose();
            throw;
        }
    }

    /// <summary>
    /// Advances session events, jobs, coroutines, and scene lifecycle by one frame.
    /// </summary>
    /// <param name="deltaTime">
    /// The elapsed frame time in seconds; negative values are treated as zero.
    /// </param>
    /// <exception cref="ObjectDisposedException">
    /// Thrown when this session has been disposed.
    /// </exception>
    /// <exception cref="RetirementPendingException">
    /// A faulted generation still owns live dependencies and cannot execute another frame.
    /// </exception>
    public void Tick(float deltaTime)
    {
        m_host.generations.EnsureRetirementSafe();
        using IDisposable scope = EnterExecutionScope();
        float delta = Math.Clamp(deltaTime, 0f, options.maxFrameDeltaTime);
        RuntimeFrame frame = m_clock.Update(delta);
        m_jobs.BeginFrame();
        bool subsystemFrameActive = false;
        try
        {
            _ = m_assets?.CompletePendingLoads();
            subsystemFrameActive = true;
            subsystems.BeginFrame(frame);
            events.Flush();
            m_coroutines.Tick(frame.deltaTime);
            if (options.kind != RuntimeSessionKind.Edit && !frame.isPaused)
            {
                m_fixedAccumulator += frame.deltaTime;
                int steps = 0;
                while (m_fixedAccumulator >= options.fixedDeltaTime
                       && steps < options.maxFixedStepsPerFrame)
                {
                    RuntimeFixedFrame fixedFrame = m_clock.BeginFixedStep(options.fixedDeltaTime);
                    subsystems.FixedUpdate(fixedFrame);
                    m_fixedAccumulator -= options.fixedDeltaTime;
                    steps++;
                }
                if (steps == options.maxFixedStepsPerFrame
                    && m_fixedAccumulator >= options.fixedDeltaTime)
                {
                    m_fixedAccumulator = 0f;
                }
            }
            subsystems.Update(frame);
            subsystems.LateUpdate(frame);
            subsystems.RenderFrame(frame);
        }
        finally
        {
            try
            {
                if (subsystemFrameActive)
                    subsystems.EndFrame(frame);
            }
            finally
            {
                try
                {
                    m_jobs.EndFrame();
                }
                finally
                {
                    m_jobs.DrainMainThreadQueue();
                }
            }
        }
    }

    /// <summary>
    /// Stops every coroutine owned by a runtime object before that object is retired by an atomic reload.
    /// </summary>
    /// <param name="owner">
    /// The exact owner identity supplied when its coroutines were started.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="owner"/> is null.
    /// </exception>
    /// <exception cref="ObjectDisposedException">
    /// Thrown when this session has been disposed.
    /// </exception>
    public void StopCoroutines(object owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ObjectDisposedException.ThrowIf(m_disposed, this);
        m_coroutines.StopAllCoroutines(owner);
    }

    /// <summary>
    /// Releases scene, asset, scheduling, serialization, and logging ownership for this session.
    /// </summary>
    /// <exception cref="RetirementPendingException">
    /// Session work has not drained or a shared generation still owns live dependencies.
    /// </exception>
    public void Dispose()
    {
        try
        {
            Retire();
        }
        finally
        {
            if (m_disposed)
                m_host.Release(this);
        }
    }

    internal void DisposeFromHost() => Retire();

    private void Retire()
    {
        if (m_disposed)
            return;
        m_host.generations.EnsureRetirementSafe();
        try
        {
            if (!m_retirement.TryComplete(DisposeCore))
                throw new RetirementPendingException($"Runtime session {sessionId} is still draining owned work.");
        }
        catch (Exception failure) when (RetirementPendingException.Find(failure) is RetirementTimeoutException)
        {
            m_host.generations.Fault(failure);
            throw;
        }
        catch (Exception pendingRetirement) when (RetirementPendingException.Find(pendingRetirement) is not null) { throw; }
        catch (Exception failure)
        {
            m_host.generations.Fault(failure);
            throw;
        }
    }

    private void DisposeCore()
    {
        if (m_disposed)
            return;
        try
        {
            using IDisposable? scope = scenes is null ? null : EnterExecutionScope();
            if (m_subsystems is not null)
                DisposeStage(m_subsystems, ref m_retirementFailures);
            if (m_assets is not null)
                DisposeStage(m_assets, ref m_retirementFailures);
            if (scenes is not null)
                DisposeStage(scenes, ref m_retirementFailures);
        }
        catch (Exception pendingRetirement) when (RetirementPendingException.Find(pendingRetirement) is not null)
        {
            m_jobs?.DrainMainThreadQueue();
            throw;
        }
        catch (Exception exception)
        {
            (m_retirementFailures ??= []).Add(exception);
        }
        m_disposed = true;
        DisposeStage(m_coroutines, ref m_retirementFailures);
        if (m_jobs is not null)
            DisposeStage(m_jobs, ref m_retirementFailures);
        if (m_serialization is not null)
            DisposeStage(m_serialization, ref m_retirementFailures);
        if (m_fileLog is not null)
        {
            m_host.logs.UnregisterSink(m_fileLog);
            DisposeStage(m_fileLog, ref m_retirementFailures);
        }
        if (m_retirementFailures is not null)
        {
            var failure = new AggregateException("Runtime session disposal encountered one or more failures.", m_retirementFailures);
            m_retirementFailures = null;
            throw failure;
        }
    }

    private static RuntimeSessionOptions Validate(RuntimeSessionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.applicationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.persistentDataDirectory);
        if (options.fixedDeltaTime <= 0f)
            throw new ArgumentOutOfRangeException(nameof(options), "Fixed delta time must be positive.");
        if (options.assetResidencyBudgetBytes < 0)
            throw new ArgumentOutOfRangeException(nameof(options), "Asset residency budget cannot be negative.");
        if (options.assetPreparationBudgetBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), "Asset preparation budget must be positive.");
        if (options.maxFrameDeltaTime <= 0f)
            throw new ArgumentOutOfRangeException(nameof(options), "Maximum frame delta time must be positive.");
        if (options.maxFixedStepsPerFrame <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), "Maximum fixed steps must be positive.");
        string persistentRoot = Path.GetFullPath(options.persistentDataDirectory);
        if (!string.Equals(
                Path.GetFileName(Path.TrimEndingDirectorySeparator(persistentRoot)),
                options.applicationId,
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The persistent data directory must be rooted in the exact application identifier.",
                nameof(options));
        }
        string? contentRoot = string.IsNullOrWhiteSpace(options.runtimeContentDirectory)
            ? null
            : Path.GetFullPath(options.runtimeContentDirectory);
        if (options.kind == RuntimeSessionKind.Player
            && (contentRoot is null || !Directory.Exists(contentRoot)))
        {
            throw new DirectoryNotFoundException(
                "A Player session requires an existing materialized runtime content directory.");
        }
        return new RuntimeSessionOptions
        {
            kind = options.kind,
            applicationId = options.applicationId,
            runtimeContentDirectory = contentRoot,
            persistentDataDirectory = persistentRoot,
            assetResidencyBudgetBytes = options.assetResidencyBudgetBytes,
            assetPreparationBudgetBytes = options.assetPreparationBudgetBytes,
            fixedDeltaTime = options.fixedDeltaTime,
            maxFrameDeltaTime = options.maxFrameDeltaTime,
            maxFixedStepsPerFrame = options.maxFixedStepsPerFrame,
            jobExecutionMode = options.jobExecutionMode,
            jobWorkerCount = options.jobWorkerCount,
            createSubsystems = options.createSubsystems
                ?? throw new ArgumentException("Runtime subsystem factories cannot be null.", nameof(options)),
            capabilities = Array.AsReadOnly(options.capabilities?.ToArray()
                ?? throw new ArgumentException("Runtime capabilities cannot be null.", nameof(options))),
            referenceResolvers = options.referenceResolvers?.ToArray()
                ?? throw new ArgumentException("Runtime reference resolvers cannot be null.", nameof(options))
        };
    }

    private static void DisposeStage(IDisposable stage, ref List<Exception>? failures)
    {
        try
        {
            stage.Dispose();
        }
        catch (Exception pendingRetirement) when (RetirementPendingException.Find(pendingRetirement) is not null) { throw; }
        catch (Exception exception)
        {
            failures ??= [];
            failures.Add(exception);
        }
    }

    private sealed class ExecutionScope(IReadOnlyList<IDisposable> scopes) : IDisposable
    {
        private bool m_disposed;

        /// <summary>
        /// Releases the resources owned by this instance.
        /// </summary>
        public void Dispose()
        {
            if (m_disposed)
                return;
            m_disposed = true;
            for (int index = scopes.Count - 1; index >= 0; index--)
                scopes[index].Dispose();
        }
    }

    private sealed class SessionFileLogSink(LogSessionId sessionId, FileLogSink sink)
        : ILogSink, IDisposable
    {
        /// <summary>
        /// Receives one immutable entry and routes it through the active sink policy.
        /// </summary>
        /// <param name="entry">
        /// The entry consumed by receive; ownership remains with the caller unless explicitly stated otherwise.
        /// </param>
        public void Receive(LogEntry entry)
        {
            if (entry.sessionId == sessionId)
                sink.Receive(entry);
        }

        /// <summary>
        /// Releases the resources owned by this instance.
        /// </summary>
        public void Dispose() => sink.Dispose();
    }
}
