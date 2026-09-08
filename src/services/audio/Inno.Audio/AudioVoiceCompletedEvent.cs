using System;
using Inno.Core.Events;

namespace Inno.Audio;

/// <summary>
/// Reports one voice terminal transition on the owning runtime's main-thread event dispatcher.
/// </summary>
public sealed class AudioVoiceCompletedEvent : Event
{
    /// <summary>
    /// Creates a voice completion event.
    /// </summary>
    /// <param name="voice">
    /// Voice that reached a terminal state.
    /// </param>
    /// <param name="reason">
    /// Reason playback ended.
    /// </param>
    public AudioVoiceCompletedEvent(AudioVoiceHandle voice, AudioCompletionReason reason)
    {
        if (!voice.isValid)
            throw new ArgumentException("A valid voice handle is required.", nameof(voice));
        this.voice = voice;
        this.reason = reason;
    }

    /// <summary>
    /// Gets the completed voice handle.
    /// </summary>
    public AudioVoiceHandle voice { get; }

    /// <summary>
    /// Gets the terminal playback reason.
    /// </summary>
    public AudioCompletionReason reason { get; }
}
