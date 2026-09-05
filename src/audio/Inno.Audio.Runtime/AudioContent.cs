using System;
using System.Collections.Generic;
using System.Linq;

namespace Inno.Audio;

/// <summary>
/// Identifies one host-owned content root within an audio update.
/// </summary>
public readonly record struct AudioContentId
{
    /// <summary>
    /// Creates a stable audio content identity.
    /// </summary>
    /// <param name="value">
    /// Persistent identity of the host-owned content root.
    /// </param>
    public AudioContentId(Guid value)
    {
        if (value == Guid.Empty)
            throw new ArgumentException("An audio content ID cannot be empty.", nameof(value));
        this.value = value;
    }

    /// <summary>
    /// Gets the persistent content identity.
    /// </summary>
    public Guid value { get; }

    /// <summary>
    /// Gets whether this identifier contains a usable value.
    /// </summary>
    public bool isValid => value != Guid.Empty;
}

/// <summary>
/// Associates a stable identity with one update-scoped host content object.
/// </summary>
public readonly record struct AudioContentReference
{
    /// <summary>
    /// Creates an update-scoped content reference.
    /// </summary>
    /// <param name="id">
    /// Stable content identity.
    /// </param>
    /// <param name="value">
    /// Current-generation host object that must not be retained by providers.
    /// </param>
    public AudioContentReference(AudioContentId id, object value)
    {
        if (!id.isValid)
            throw new ArgumentException("A valid audio content ID is required.", nameof(id));
        this.id = id;
        this.value = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>
    /// Gets the stable content identity.
    /// </summary>
    public AudioContentId id { get; }

    /// <summary>
    /// Gets the update-scoped host content object.
    /// </summary>
    public object value { get; }
}

/// <summary>
/// Carries an ordered model-neutral set of host content roots into audio providers.
/// </summary>
public sealed class AudioContentScope
{
    private readonly AudioContentReference[] m_contents;

    /// <summary>
    /// Creates an immutable audio content scope.
    /// </summary>
    /// <param name="contents">
    /// Ordered host-owned content roots visible during one update.
    /// </param>
    public AudioContentScope(IEnumerable<AudioContentReference> contents)
    {
        ArgumentNullException.ThrowIfNull(contents);
        m_contents = contents.ToArray();
        if (m_contents.Select(static item => item.id).Distinct().Count() != m_contents.Length)
            throw new ArgumentException("Audio content identities must be unique within one scope.", nameof(contents));
    }

    /// <summary>
    /// Gets a shared empty content scope.
    /// </summary>
    public static AudioContentScope empty { get; } = new([]);

    /// <summary>
    /// Gets the ordered immutable content references.
    /// </summary>
    public IReadOnlyList<AudioContentReference> contents => m_contents;

    /// <summary>
    /// Returns all content values assignable to the requested type in scope order.
    /// </summary>
    /// <typeparam name="TValue">
    /// Host content type understood by the provider.
    /// </typeparam>
    /// <returns>
    /// An update-scoped array that may be empty.
    /// </returns>
    public IReadOnlyList<TValue> GetValues<TValue>() where TValue : class
        => m_contents.Select(static item => item.value).OfType<TValue>().ToArray();
}

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
