using System;

namespace Inno.Audio;

/// <summary>
/// Describes one immutable source playback request produced by a content provider.
/// </summary>
public readonly record struct AudioEmitterSnapshot
{
    /// <summary>
    /// Creates an immutable emitter snapshot.
    /// </summary>
    /// <param name="id">
    /// Stable emitter identity across updates.
    /// </param>
    /// <param name="clip">
    /// Imported clip requested by the emitter.
    /// </param>
    /// <param name="options">
    /// Current playback and spatial parameters.
    /// </param>
    /// <param name="shouldPlay">
    /// Whether the runtime should retain active playback for this emitter.
    /// </param>
    /// <param name="playbackRevision">
    /// Monotonic source-owned revision used to request a fresh voice for the same emitter.
    /// </param>
    public AudioEmitterSnapshot(
        Guid id,
        AudioClipAsset clip,
        AudioPlayOptions options,
        bool shouldPlay,
        ulong playbackRevision = 0)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("An emitter identity cannot be empty.", nameof(id));
        this.id = id;
        this.clip = clip ?? throw new ArgumentNullException(nameof(clip));
        this.options = options;
        this.shouldPlay = shouldPlay;
        this.playbackRevision = playbackRevision;
    }

    /// <summary>
    /// Gets the stable emitter identity.
    /// </summary>
    public Guid id { get; }

    /// <summary>
    /// Gets the imported clip requested by the emitter.
    /// </summary>
    public AudioClipAsset clip { get; }

    /// <summary>
    /// Gets current playback and spatial parameters.
    /// </summary>
    public AudioPlayOptions options { get; }

    /// <summary>
    /// Gets whether the runtime should retain active playback for this emitter.
    /// </summary>
    public bool shouldPlay { get; }

    /// <summary>
    /// Gets the source-owned revision that distinguishes explicit replay requests.
    /// </summary>
    public ulong playbackRevision { get; }
}

/// <summary>
/// Describes one immutable listener candidate produced by a content provider.
/// </summary>
public readonly record struct AudioListenerSnapshot
{
    /// <summary>
    /// Creates an immutable listener snapshot.
    /// </summary>
    /// <param name="id">
    /// Stable listener identity across updates.
    /// </param>
    /// <param name="priority">
    /// Selection priority; larger values win.
    /// </param>
    /// <param name="state">
    /// Current listener transform.
    /// </param>
    /// <param name="active">
    /// Whether the listener is eligible for selection.
    /// </param>
    public AudioListenerSnapshot(Guid id, int priority, AudioListenerState state, bool active)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("A listener identity cannot be empty.", nameof(id));
        this.id = id;
        this.priority = priority;
        this.state = state;
        this.active = active;
    }

    /// <summary>
    /// Gets the stable listener identity.
    /// </summary>
    public Guid id { get; }

    /// <summary>
    /// Gets the selection priority.
    /// </summary>
    public int priority { get; }

    /// <summary>
    /// Gets the current listener transform.
    /// </summary>
    public AudioListenerState state { get; }

    /// <summary>
    /// Gets whether the listener is eligible for selection.
    /// </summary>
    public bool active { get; }
}
