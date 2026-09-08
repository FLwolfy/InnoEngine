using System;
using System.Collections.Generic;

using Inno.Extensibility.Modules;
using Inno.Core.Diagnostics;
using Inno.Core.Events;
using Inno.Core.Execution;
using Inno.Core.Identity;
using Inno.Runtime.Contracts;
using Inno.Core.Logging;
using Inno.Extensibility.Types;
using Inno.Core.Serialization;
using Inno.Extensibility.Reload;

namespace Inno.Runtime;

/// <summary>
/// Owns application-level engine services and creates isolated runtime sessions.
/// </summary>
public sealed class EngineHost : IDisposable
{
    private readonly object m_sync = new();
    private readonly HashSet<RuntimeSession> m_sessions = [];
    private readonly List<RuntimeSubsystemPipeline> m_hostPipelines = [];
    private RetirementBarrier? m_retirement;
    private List<Exception>? m_retirementFailures;
    private bool m_disposed;
    private bool m_stopping;

    internal EngineHost(string metadataCacheDirectory, TimeSpan retirementTimeout)
    {
        this.retirementTimeout = retirementTimeout;
        logs = new LogRouter();
        diagnostics = new DiagnosticHub();
        try
        {
            modules = new ModuleHost(new ModuleHostOptions
            {
                cacheDirectory = metadataCacheDirectory
            });
            types = new TypeCatalog(modules);
            serialization = new SerializationRegistry(types);
        }
        catch
        {
            serialization?.Dispose();
            types?.Dispose();
            modules?.Dispose();
            logs.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Gets the isolated asynchronous logging router owned by this host.
    /// </summary>
    public LogRouter logs { get; }

    /// <summary>
    /// Gets the isolated diagnostic state hub owned by this host.
    /// </summary>
    public DiagnosticHub diagnostics { get; }

    /// <summary>
    /// Gets the shared admission gate for reload, recovery, Play, Build and Export.
    /// </summary>
    public GenerationCoordinator generations => modules.generations;

    /// <summary>
    /// Gets the isolated managed module host that owns this engine host's reload generations.
    /// </summary>
    public ModuleHost modules { get; private set; } = null!;

    /// <summary>
    /// Gets the isolated immutable type catalog derived from this host's active modules.
    /// </summary>
    public TypeCatalog types { get; private set; } = null!;

    /// <summary>
    /// Gets the isolated serialization registry derived from this host's active type generation.
    /// </summary>
    public SerializationRegistry serialization { get; private set; } = null!;

    /// <summary>
    /// Creates an isolated Edit, Play, or Player runtime session.
    /// </summary>
    /// <param name="options">
    /// The validated ownership, storage, and timing options for the session.
    /// </param>
    /// <returns>
    /// A started session owned by this host and the caller.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="options"/> is null.
    /// </exception>
    /// <exception cref="ObjectDisposedException">
    /// Thrown when this host has been disposed.
    /// </exception>
    public RuntimeSession CreateSession(RuntimeSessionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        lock (m_sync)
        {
            ObjectDisposedException.ThrowIf(m_disposed, this);
            if (m_stopping)
                throw new InvalidOperationException("The engine host is retiring and cannot acquire new owners.");
            generations.EnsureReady("create a runtime session");
            var session = new RuntimeSession(this, options);
            m_sessions.Add(session);
            try { session.Start(); }
            catch (Exception failure)
            {
                RetireFailedStartup(session, failure);
                throw;
            }
            return session;
        }
    }

    /// <summary>
    /// Creates host-owned subsystems that outlive individual Edit, Play or Player sessions.
    /// </summary>
    /// <param name="factories">
    /// The complete host factory set; session-lifetime factories are rejected.
    /// </param>
    /// <param name="capabilities">
    /// Backend-neutral capabilities verified by the application's selected services.
    /// </param>
    /// <returns>
    /// A pipeline owned by this EngineHost and borrowed by the application's Shell.
    /// </returns>
    /// <exception cref="ObjectDisposedException">
    /// The engine host is disposed.
    /// </exception>
    public RuntimeSubsystemPipeline CreateHostPipeline(IReadOnlyList<IRuntimeSubsystemFactory> factories,
        IReadOnlyList<RuntimeCapabilityId>? capabilities = null)
    {
        ArgumentNullException.ThrowIfNull(factories);
        lock (m_sync)
        {
            ObjectDisposedException.ThrowIf(m_disposed, this);
            if (m_stopping)
                throw new InvalidOperationException("The engine host is retiring and cannot acquire new owners.");
            generations.EnsureReady("create a host subsystem pipeline");
            var context = new RuntimeSubsystemContext(new EventDispatcher(), diagnostics,
                new IdentityAllocator(), types, new LifetimeScope(), RuntimeSubsystemLifetime.Host, capabilities: capabilities);
            var pipeline = new RuntimeSubsystemPipeline(context, retirementTimeout, generations);
            m_hostPipelines.Add(pipeline);
            try { pipeline.Start(factories); }
            catch (Exception failure)
            {
                RetireFailedStartup(pipeline, failure);
                m_hostPipelines.Remove(pipeline);
                throw;
            }
            return pipeline;
        }
    }

    /// <summary>
    /// Disposes every owned session before releasing application metadata services.
    /// </summary>
    /// <exception cref="RetirementPendingException">
    /// Owned work is still draining, or a generation deadline failed. Dependencies remain owned.
    /// </exception>
    public void Dispose()
    {
        if (m_disposed)
            return;
        generations.EnsureRetirementSafe();
        try
        {
            // Metadata-only hosts are thread-neutral; attached subsystems enforce their own control thread.
            m_retirement ??= new RetirementBarrier("EngineHost", retirementTimeout);
            if (!m_retirement.TryComplete(DisposeCore))
                throw new RetirementPendingException("EngineHost is still draining owned sessions or pipelines.");
        }
        catch (Exception failure) when (RetirementPendingException.Find(failure) is RetirementTimeoutException)
        {
            generations.Fault(failure);
            throw;
        }
        catch (Exception pendingRetirement) when (RetirementPendingException.Find(pendingRetirement) is not null) { throw; }
        catch (Exception failure)
        {
            generations.Fault(failure);
            throw;
        }
    }

    private void DisposeCore()
    {
        RuntimeSession[] sessions;
        lock (m_sync)
        {
            if (m_disposed)
                return;
            m_stopping = true;
            sessions = [.. m_sessions];
        }
        for (int index = sessions.Length - 1; index >= 0; index--)
        {
            try
            {
                sessions[index].DisposeFromHost();
            }
            catch (Exception pendingRetirement) when (RetirementPendingException.Find(pendingRetirement) is not null) { throw; }
            catch (Exception exception)
            {
                (m_retirementFailures ??= []).Add(exception);
            }
        }
        for (int index = m_hostPipelines.Count - 1; index >= 0; index--)
        {
            try { m_hostPipelines[index].Dispose(); }
            catch (Exception pendingRetirement) when (RetirementPendingException.Find(pendingRetirement) is not null) { throw; }
            catch (Exception exception) { (m_retirementFailures ??= []).Add(exception); }
        }
        m_sessions.Clear();
        m_hostPipelines.Clear();
        m_disposed = true;
        try
        {
            serialization.Dispose();
        }
        catch (Exception exception)
        {
            (m_retirementFailures ??= []).Add(exception);
        }
        try
        {
            types.Dispose();
        }
        catch (Exception exception)
        {
            (m_retirementFailures ??= []).Add(exception);
        }
        try
        {
            modules.Dispose();
        }
        catch (Exception exception)
        {
            (m_retirementFailures ??= []).Add(exception);
        }
        try
        {
            logs.Dispose();
        }
        catch (Exception exception)
        {
            (m_retirementFailures ??= []).Add(exception);
        }
        if (m_retirementFailures is not null)
        {
            var failure = new AggregateException("Engine host disposal encountered one or more failures.", m_retirementFailures);
            m_retirementFailures = null;
            throw failure;
        }
        generations.Wait();
    }

    internal TimeSpan retirementTimeout { get; }

    internal void Release(RuntimeSession session)
    {
        lock (m_sync)
            m_sessions.Remove(session);
    }

    private void RetireFailedStartup(IDisposable owner, Exception startupFailure)
    {
        IDisposable? reservation = null;
        if (generations.state == GenerationState.Ready)
            _ = generations.TryAcquireChange("retire failed runtime startup", out reservation);
        using IDisposable? generationReservation = reservation;
        try
        {
            new RetirementBarrier("Failed runtime startup", retirementTimeout).Wait(owner.Dispose);
        }
        catch (Exception cleanupFailure)
        {
            var failure = new AggregateException("Runtime startup and retirement failed.", startupFailure, cleanupFailure);
            generations.Fault(failure);
            if (RetirementPendingException.Find(cleanupFailure) is not null)
                throw;
            throw failure;
        }
    }
}
