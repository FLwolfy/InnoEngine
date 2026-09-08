using System;

using Inno.Core.Events;

namespace Inno.Animation;

/// <summary>
/// Defines backend-neutral animation playback and control for one isolated runtime session.
/// </summary>
public interface IAnimationService
{
    /// <summary>
    /// Starts one clip with default playback options.
    /// </summary>
    /// <param name="clip">
    /// Valid imported animation clip.
    /// </param>
    /// <returns>
    /// A generation-checked playback handle.
    /// </returns>
    /// <param name="target">
    /// The weak destination or explicit sampling-only policy.
    /// </param>
    AnimationPlaybackHandle Play(AnimationClipAsset clip, AnimationTarget target);

    /// <summary>
    /// Starts one clip with explicit playback options.
    /// </summary>
    /// <param name="clip">
    /// Valid imported animation clip.
    /// </param>
    /// <param name="options">
    /// Playback clock, speed, blend, layer, and looping policy.
    /// </param>
    /// <returns>
    /// A generation-checked playback handle.
    /// </returns>
    /// <param name="target">
    /// The weak destination or explicit sampling-only policy.
    /// </param>
    AnimationPlaybackHandle Play(AnimationClipAsset clip, AnimationTarget target, AnimationPlayOptions options);

    /// <summary>
    /// Stops one live playback.
    /// </summary>
    /// <param name="handle">
    /// Playback handle from this runtime generation.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when a live playback was stopped.
    /// </returns>
    bool Stop(AnimationPlaybackHandle handle);

    /// <summary>
    /// Pauses one live playback at its current position.
    /// </summary>
    /// <param name="handle">
    /// Playback handle from this runtime generation.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the playback state changed.
    /// </returns>
    bool Pause(AnimationPlaybackHandle handle);

    /// <summary>
    /// Resumes one paused playback.
    /// </summary>
    /// <param name="handle">
    /// Playback handle from this runtime generation.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the playback state changed.
    /// </returns>
    bool Resume(AnimationPlaybackHandle handle);

    /// <summary>
    /// Seeks one live playback to a clip-local time.
    /// </summary>
    /// <param name="handle">
    /// Playback handle from this runtime generation.
    /// </param>
    /// <param name="time">
    /// Finite clip-local time in seconds, clamped to the clip duration.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the handle was live and the position was updated.
    /// </returns>
    bool Seek(AnimationPlaybackHandle handle, float time);

    /// <summary>
    /// Tries to read one live playback's state and position.
    /// </summary>
    /// <param name="handle">
    /// Playback handle from this runtime generation.
    /// </param>
    /// <param name="state">
    /// Receives the current playback state when successful.
    /// </param>
    /// <param name="time">
    /// Receives the current clip-local time in seconds when successful.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the handle still identifies a live playback.
    /// </returns>
    bool TryGetState(
        AnimationPlaybackHandle handle,
        out AnimationPlaybackState state,
        out float time);
}

/// <summary>
/// Publishes a stable marker reached by one animation playback on the main runtime thread.
/// </summary>
public sealed class AnimationMarkerEvent : Event
{
    /// <summary>
    /// Creates one immutable animation marker event.
    /// </summary>
    /// <param name="playback">
    /// Playback that crossed the marker.
    /// </param>
    /// <param name="eventId">
    /// Stable marker protocol identifier.
    /// </param>
    /// <param name="payload">
    /// Opaque reload-safe marker payload bytes.
    /// </param>
    public AnimationMarkerEvent(
        AnimationPlaybackHandle playback,
        string eventId,
        ReadOnlyMemory<byte> payload)
    {
        if (!playback.isValid)
            throw new ArgumentException("A valid animation playback handle is required.", nameof(playback));
        ArgumentException.ThrowIfNullOrWhiteSpace(eventId);
        this.playback = playback;
        this.eventId = eventId;
        this.payload = payload.ToArray();
    }

    /// <summary>
    /// Gets the playback that crossed the marker.
    /// </summary>
    public AnimationPlaybackHandle playback { get; }

    /// <summary>
    /// Gets the stable marker protocol identifier.
    /// </summary>
    public string eventId { get; }

    /// <summary>
    /// Gets an immutable copy of the marker payload.
    /// </summary>
    public ReadOnlyMemory<byte> payload { get; }
}
