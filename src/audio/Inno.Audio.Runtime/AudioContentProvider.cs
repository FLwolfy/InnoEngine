using System;
using System.Collections.Generic;

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
/// Collects immutable emitter and listener snapshots during one control-thread update.
/// </summary>
public sealed class AudioContentProviderContext
{
    private readonly List<AudioEmitterSnapshot> m_emitters = [];
    private readonly List<AudioListenerSnapshot> m_listeners = [];
    private readonly HashSet<Guid> m_ids = [];

    internal AudioContentProviderContext(AudioContentScope content, float deltaTime)
    {
        this.content = content;
        this.deltaTime = deltaTime;
    }

    /// <summary>
    /// Gets explicit host content visible during this update.
    /// </summary>
    public AudioContentScope content { get; }

    /// <summary>
    /// Gets non-negative elapsed frame time in seconds.
    /// </summary>
    public float deltaTime { get; }

    /// <summary>
    /// Submits one immutable emitter snapshot for runtime synchronization.
    /// </summary>
    /// <param name="emitter">
    /// Current state for one stable emitter identity.
    /// </param>
    /// <exception cref="ArgumentException">
    /// Thrown when any provider repeats an emitter or listener identity during this update.
    /// </exception>
    public void Submit(AudioEmitterSnapshot emitter)
    {
        if (!m_ids.Add(emitter.id))
            throw new ArgumentException($"Audio content identity '{emitter.id:D}' was submitted more than once.");
        m_emitters.Add(emitter);
    }

    /// <summary>
    /// Submits one immutable listener snapshot for runtime selection.
    /// </summary>
    /// <param name="listener">
    /// Current state for one stable listener identity.
    /// </param>
    /// <exception cref="ArgumentException">
    /// Thrown when any provider repeats an emitter or listener identity during this update.
    /// </exception>
    public void Submit(AudioListenerSnapshot listener)
    {
        if (!m_ids.Add(listener.id))
            throw new ArgumentException($"Audio content identity '{listener.id:D}' was submitted more than once.");
        m_listeners.Add(listener);
    }

    internal IReadOnlyList<AudioEmitterSnapshot> emitters => m_emitters;

    internal IReadOnlyList<AudioListenerSnapshot> listeners => m_listeners;
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
    public void Dispose()
    {
        if (m_disposed)
            return;
        m_disposed = true;
        Dispose(true);
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
