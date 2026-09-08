using Inno.References;
using System;
using System.Collections.Generic;
using Inno.Core.Execution;

namespace Inno.Audio;

/// <summary>
/// Marks a reloadable provider that converts host content into immutable audio snapshots.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class AudioContentProviderExtensionAttribute : Attribute
{
    /// <summary>
    /// Creates an audio content provider declaration.
    /// </summary>
    /// <param name="id">
    /// Globally stable provider identifier.
    /// </param>
    /// <param name="priority">
    /// Provider invocation priority; lower values run first.
    /// </param>
    public AudioContentProviderExtensionAttribute(string id, int priority = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        this.id = id;
        this.priority = priority;
    }

    /// <summary>
    /// Gets the globally stable provider identifier.
    /// </summary>
    public string id { get; }

    /// <summary>
    /// Gets the provider invocation priority.
    /// </summary>
    public int priority { get; }
}

/// <summary>
/// Collects one bounded, atomic provider contribution on the owning control thread.
/// </summary>
/// <remarks>
/// The host disposes this borrowed context when the provider returns. A rejected submission invalidates the
/// entire contribution, even if the provider catches the exception. The context does not own the content scope.
/// </remarks>
public sealed class AudioContentProviderContext : IDisposable
{
    private readonly List<AudioEmitterSnapshot> m_emitters = [];
    private readonly List<AudioListenerSnapshot> m_listeners = [];
    private readonly HashSet<Guid> m_ids = [];
    private readonly int m_ownerThread = Environment.CurrentManagedThreadId;
    private ContentReadScope? m_content;
    private bool m_rejected;

    /// <summary>
    /// Opens a control-thread snapshot collector over one content read scope.
    /// </summary>
    /// <param name="content">
    /// The caller-owned identity read scope.
    /// </param>
    /// <param name="deltaTime">
    /// Finite non-negative time elapsed since the previous update.
    /// </param>
    /// <param name="capacity">
    /// Maximum combined emitter and listener count accepted by this contribution. Zero permits an empty contribution only.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The elapsed time is invalid or capacity is negative.
    /// </exception>
    /// <exception cref="ArgumentNullException">
    /// The borrowed content scope is null.
    /// </exception>
    public AudioContentProviderContext(ContentReadScope content, float deltaTime, int capacity = 1024)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (!float.IsFinite(deltaTime) || deltaTime < 0f)
            throw new ArgumentOutOfRangeException(nameof(deltaTime));
        ArgumentOutOfRangeException.ThrowIfNegative(capacity);
        m_content = content;
        this.deltaTime = deltaTime;
        this.capacity = capacity;
    }

    /// <summary>
    /// Gets explicit host content visible during this update.
    /// </summary>
    /// <exception cref="ObjectDisposedException">
    /// This provider invocation has ended.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Access occurs off the owning control thread or a submission was rejected.
    /// </exception>
    public ContentReadScope content
    {
        get { EnsureActive(); return m_content!; }
    }

    /// <summary>
    /// Gets non-negative elapsed frame time in seconds.
    /// </summary>
    public float deltaTime { get; }

    /// <summary>
    /// Gets the immutable combined snapshot budget assigned to this invocation.
    /// </summary>
    public int capacity { get; }

    /// <summary>
    /// Submits one immutable emitter snapshot for runtime synchronization.
    /// </summary>
    /// <param name="emitter">
    /// Current state for one stable emitter identity.
    /// </param>
    /// <exception cref="ArgumentException">
    /// The emitter is uninitialized or its identity duplicates a submission in this contribution.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The budget is exhausted, the contribution was rejected, or access occurs off the owner thread.
    /// </exception>
    /// <exception cref="ObjectDisposedException">
    /// This provider invocation has ended.
    /// </exception>
    public void Submit(AudioEmitterSnapshot emitter)
    {
        EnsureActive();
        if (emitter.clip is null || !emitter.options.bus.isValid)
        {
            m_rejected = true;
            throw new ArgumentException("An audio emitter requires an initialized clip and playback options.", nameof(emitter));
        }
        Admit(emitter.id);
        m_emitters.Add(emitter);
    }

    /// <summary>
    /// Submits one immutable listener snapshot for runtime selection.
    /// </summary>
    /// <param name="listener">
    /// Current state for one stable listener identity.
    /// </param>
    /// <exception cref="ArgumentException">
    /// The listener is uninitialized or its identity duplicates a submission in this contribution.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The budget is exhausted, the contribution was rejected, or access occurs off the owner thread.
    /// </exception>
    /// <exception cref="ObjectDisposedException">
    /// This provider invocation has ended.
    /// </exception>
    public void Submit(AudioListenerSnapshot listener)
    {
        EnsureActive();
        Admit(listener.id);
        m_listeners.Add(listener);
    }

    /// <summary>
    /// Gets a frozen copy of submitted source state in provider order.
    /// </summary>
    /// <exception cref="ObjectDisposedException">
    /// This provider invocation has ended.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The contribution was rejected or access occurs off the owning control thread.
    /// </exception>
    public IReadOnlyList<AudioEmitterSnapshot> emitters
    {
        get { EnsureActive(); return Array.AsReadOnly(m_emitters.ToArray()); }
    }

    /// <summary>
    /// Gets a frozen copy of submitted listener state in provider order.
    /// </summary>
    /// <exception cref="ObjectDisposedException">
    /// This provider invocation has ended.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The contribution was rejected or access occurs off the owning control thread.
    /// </exception>
    public IReadOnlyList<AudioListenerSnapshot> listeners
    {
        get { EnsureActive(); return Array.AsReadOnly(m_listeners.ToArray()); }
    }

    /// <summary>
    /// Revokes submission and releases collected references without disposing the borrowed content scope.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Disposal occurs off the owning control thread.
    /// </exception>
    public void Dispose()
    {
        EnsureOwnerThread();
        m_content = null;
        m_emitters.Clear();
        m_listeners.Clear();
        m_ids.Clear();
    }

    private void Admit(Guid id)
    {
        if (id == Guid.Empty || m_ids.Contains(id))
        {
            m_rejected = true;
            throw new ArgumentException($"Audio content identity '{id:D}' is empty or duplicated.");
        }
        if (m_ids.Count >= capacity)
        {
            m_rejected = true;
            throw new InvalidOperationException("The audio content contribution exceeds its snapshot capacity.");
        }
        m_ids.Add(id);
    }

    private void EnsureActive()
    {
        EnsureOwnerThread();
        ObjectDisposedException.ThrowIf(m_content is null, this);
        if (m_rejected)
            throw new InvalidOperationException("The audio content contribution contains a rejected submission.");
    }

    private void EnsureOwnerThread()
    {
        if (Environment.CurrentManagedThreadId != m_ownerThread)
            throw new InvalidOperationException("Audio content collection requires its owning control thread.");
    }
}

/// <summary>
/// Converts arbitrary host-owned content into backend-neutral emitter and listener snapshots.
/// </summary>
public abstract class AudioContentProvider : IDisposable
{
    private bool m_disposed;

    /// <summary>
    /// Submits current immutable audio snapshots on the host control thread.
    /// </summary>
    /// <param name="context">
    /// Update-scoped content and snapshot collector.
    /// </param>
    public abstract void Submit(AudioContentProviderContext context);

    /// <summary>
    /// Releases generation-scoped provider state.
    /// </summary>
    /// <exception cref="RetirementPendingException">
    /// Owned work is still draining. The generation owner must retain this provider and retry disposal.
    /// </exception>
    public void Dispose()
    {
        if (m_disposed)
            return;
        try { Dispose(true); }
        catch (Exception pendingRetirement) when (RetirementPendingException.Find(pendingRetirement) is not null) { throw; }
        catch
        {
            m_disposed = true;
            throw;
        }
        m_disposed = true;
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Releases managed generation-scoped state.
    /// </summary>
    /// <param name="disposing">
    /// Always <see langword="true"/> for explicit disposal.
    /// </param>
    protected virtual void Dispose(bool disposing)
    {
    }
}
