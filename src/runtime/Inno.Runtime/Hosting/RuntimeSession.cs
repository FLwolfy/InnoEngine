using System;
using System.Collections.Generic;
using System.IO;
using Inno.Assets;
using Inno.Core.Coroutines;
using Inno.Core.Diagnostics;
using Inno.Core.Events;
using Inno.Core.Identity;
using Inno.Core.Jobs;
using Inno.Core.Logging;
using Inno.Core.Serialization;
using Inno.Scene;

namespace Inno.Runtime;

/// <summary>
/// Owns all mutable simulation, identity, asset, scheduling, and logging state for one isolated execution session.
/// </summary>
public sealed class RuntimeSession : IDisposable
{
    private readonly EngineHost m_host;
    private readonly IdentityAllocator m_identities;
    private readonly JobScheduler m_jobs;
    private readonly CoroutineScheduler m_coroutines;
    private readonly RuntimeClock m_clock = new();
    private readonly SerializationGeneration m_serialization;
    private readonly SessionFileLogSink m_fileLog;
    private readonly AssetDatabase? m_assets;
    private float m_fixedAccumulator;
    private bool m_disposed;

    internal RuntimeSession(EngineHost host, RuntimeSessionOptions options)
    {
        m_host = host;
        this.options = Validate(options);
        sessionId = LogSessionId.Create();
        m_identities = new IdentityAllocator();
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
        m_coroutines = new CoroutineScheduler();
        m_serialization = host.serialization.CaptureGeneration();
        events = new EventDispatcher();
        scenes = new SceneWorld(m_identities, host.types);
        Directory.CreateDirectory(this.options.persistentDataDirectory);
        m_fileLog = new SessionFileLogSink(
            sessionId,
            new FileLogSink(Path.Combine(this.options.persistentDataDirectory, "Logs")));
        m_host.logs.RegisterSink(m_fileLog);
        try
        {
            using IDisposable scope = EnterExecutionScope();
            if (!string.IsNullOrWhiteSpace(this.options.runtimeContentDirectory))
            {
                m_assets = new AssetDatabase(
                    this.options.runtimeContentDirectory,
                    m_serialization,
                    host.types,
                    m_identities);
            }
        }
        catch
        {
            m_host.logs.UnregisterSink(m_fileLog);
            m_fileLog.Dispose();
            scenes.Dispose();
            m_serialization.Dispose();
            m_coroutines.Dispose();
            m_jobs.Dispose();
            throw;
        }
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
    /// Gets the isolated scene world owned by this session.
    /// </summary>
    public SceneWorld scenes { get; }

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
    /// <param name="totalTime">
    /// The absolute session time in seconds.
    /// </param>
    /// <param name="deltaTime">
    /// The elapsed frame time in seconds; negative values are treated as zero.
    /// </param>
    /// <exception cref="ObjectDisposedException">
    /// Thrown when this session has been disposed.
    /// </exception>
    public void Tick(float totalTime, float deltaTime)
    {
        _ = totalTime;
        using IDisposable scope = EnterExecutionScope();
        float delta = Math.Clamp(deltaTime, 0f, options.maxFrameDeltaTime);
        m_clock.Update(totalTime, delta);
        m_jobs.BeginFrame();
        try
        {
            events.Flush();
            m_coroutines.Tick(delta);
            if (options.kind != RuntimeSessionKind.Edit)
            {
                m_fixedAccumulator += delta;
                int steps = 0;
                while (m_fixedAccumulator >= options.fixedDeltaTime
                       && steps < options.maxFixedStepsPerFrame)
                {
                    m_clock.BeginFixedStep(options.fixedDeltaTime);
                    scenes.FixedUpdate(options.fixedDeltaTime);
                    m_fixedAccumulator -= options.fixedDeltaTime;
                    steps++;
                }
                if (steps == options.maxFixedStepsPerFrame
                    && m_fixedAccumulator >= options.fixedDeltaTime)
                {
                    m_fixedAccumulator = 0f;
                }
                scenes.Update(delta);
                scenes.LateUpdate(delta);
            }
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
    public void Dispose()
    {
        try
        {
            DisposeCore();
        }
        finally
        {
            m_host.Release(this);
        }
    }

    internal void DisposeFromHost() => DisposeCore();

    private void DisposeCore()
    {
        if (m_disposed)
            return;
        List<Exception>? failures = null;
        try
        {
            using IDisposable scope = EnterExecutionScope();
            m_assets?.Dispose();
            scenes.Dispose();
        }
        catch (Exception exception)
        {
            failures = [exception];
        }
        m_disposed = true;
        DisposeStage(m_coroutines, ref failures);
        DisposeStage(m_jobs, ref failures);
        DisposeStage(m_serialization, ref failures);
        m_host.logs.UnregisterSink(m_fileLog);
        DisposeStage(m_fileLog, ref failures);
        if (failures is not null)
            throw new AggregateException("Runtime session disposal encountered one or more failures.", failures);
    }

    private static RuntimeSessionOptions Validate(RuntimeSessionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.applicationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.persistentDataDirectory);
        if (options.fixedDeltaTime <= 0f)
            throw new ArgumentOutOfRangeException(nameof(options), "Fixed delta time must be positive.");
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
            fixedDeltaTime = options.fixedDeltaTime,
            maxFrameDeltaTime = options.maxFrameDeltaTime,
            maxFixedStepsPerFrame = options.maxFixedStepsPerFrame,
            jobExecutionMode = options.jobExecutionMode,
            jobWorkerCount = options.jobWorkerCount
        };
    }

    private static void DisposeStage(IDisposable stage, ref List<Exception>? failures)
    {
        try
        {
            stage.Dispose();
        }
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
