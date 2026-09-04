using System;
using System.Collections.Generic;

using Inno.Extensibility.Modules;
using Inno.Core.Diagnostics;
using Inno.Core.Logging;
using Inno.Extensibility.Types;
using Inno.Core.Serialization;

namespace Inno.Runtime;

/// <summary>
/// Owns application-level engine services and creates isolated runtime sessions.
/// </summary>
public sealed class EngineHost : IDisposable
{
    private readonly object m_sync = new();
    private readonly HashSet<RuntimeSession> m_sessions = [];
    private bool m_disposed;

    internal EngineHost(string metadataCacheDirectory)
    {
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
            var session = new RuntimeSession(this, options);
            m_sessions.Add(session);
            return session;
        }
    }

    /// <summary>
    /// Disposes every owned session before releasing application metadata services.
    /// </summary>
    public void Dispose()
    {
        RuntimeSession[] sessions;
        lock (m_sync)
        {
            if (m_disposed)
                return;
            m_disposed = true;
            sessions = [.. m_sessions];
            m_sessions.Clear();
        }
        List<Exception>? failures = null;
        for (int index = sessions.Length - 1; index >= 0; index--)
        {
            try
            {
                sessions[index].DisposeFromHost();
            }
            catch (Exception exception)
            {
                failures ??= [];
                failures.Add(exception);
            }
        }
        try
        {
            serialization.Dispose();
        }
        catch (Exception exception)
        {
            failures ??= [];
            failures.Add(exception);
        }
        try
        {
            types.Dispose();
        }
        catch (Exception exception)
        {
            failures ??= [];
            failures.Add(exception);
        }
        try
        {
            modules.Dispose();
        }
        catch (Exception exception)
        {
            failures ??= [];
            failures.Add(exception);
        }
        try
        {
            logs.Dispose();
        }
        catch (Exception exception)
        {
            failures ??= [];
            failures.Add(exception);
        }
        if (failures is not null)
            throw new AggregateException("Engine host disposal encountered one or more failures.", failures);
    }

    internal void Release(RuntimeSession session)
    {
        lock (m_sync)
            m_sessions.Remove(session);
    }
}
