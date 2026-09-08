using System;
using System.Collections.Generic;
using System.Linq;

using Inno.Assets;
using Inno.Core.Serialization;
using Inno.Extensibility.Types;

namespace Inno.Animation;

/// <summary>
/// Stores one timestamped value inside an animation track.
/// </summary>
public struct AnimationKeyframe
{
    /// <summary>
    /// Creates one animation keyframe.
    /// </summary>
    /// <param name="time">
    /// Non-negative clip-local time in seconds.
    /// </param>
    /// <param name="value">
    /// Value sampled at the keyframe time.
    /// </param>
    public AnimationKeyframe(float time, AnimationValue value)
    {
        if (!float.IsFinite(time) || time < 0f)
            throw new ArgumentOutOfRangeException(nameof(time));
        this.time = time;
        this.value = value;
    }

    /// <summary>
    /// Gets or sets clip-local time in seconds.
    /// </summary>
    [SerializableProperty]
    public float time { get; set; }

    /// <summary>
    /// Gets or sets the sampled value.
    /// </summary>
    [SerializableProperty]
    public AnimationValue value { get; set; }
}

/// <summary>
/// Stores one ordered value track targeting a stable binding.
/// </summary>
public sealed class AnimationTrack : ISerializable
{
    /// <summary>
    /// Creates an empty track for structured deserialization.
    /// </summary>
    public AnimationTrack()
    {
    }

    /// <summary>
    /// Creates one validated animation track.
    /// </summary>
    /// <param name="bindingId">
    /// Stable target binding identifier.
    /// </param>
    /// <param name="interpolation">
    /// Sampling rule between adjacent keyframes.
    /// </param>
    /// <param name="keyframes">
    /// Keyframes ordered by non-decreasing clip-local time.
    /// </param>
    public AnimationTrack(
        string bindingId,
        AnimationInterpolation interpolation,
        IEnumerable<AnimationKeyframe> keyframes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bindingId);
        ArgumentNullException.ThrowIfNull(keyframes);
        this.bindingId = bindingId;
        this.interpolation = interpolation;
        this.keyframes = keyframes.ToArray();
        Validate(float.PositiveInfinity);
    }

    /// <summary>
    /// Gets or sets the stable target binding identifier.
    /// </summary>
    [SerializableProperty]
    public string bindingId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the sampling rule between adjacent keyframes.
    /// </summary>
    [SerializableProperty]
    public AnimationInterpolation interpolation { get; set; }

    /// <summary>
    /// Gets or sets ordered keyframes.
    /// </summary>
    [SerializableProperty]
    public AnimationKeyframe[] keyframes { get; set; } = [];

    internal void Validate(float duration)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bindingId);
        if (keyframes is null || keyframes.Length == 0)
            throw new InvalidOperationException($"Animation track '{bindingId}' requires at least one keyframe.");
        AnimationValueKind kind = keyframes[0].value.kind;
        float previous = -1f;
        for (int index = 0; index < keyframes.Length; index++)
        {
            AnimationKeyframe keyframe = keyframes[index];
            if (!float.IsFinite(keyframe.time) || keyframe.time < previous || keyframe.time > duration)
                throw new InvalidOperationException($"Animation track '{bindingId}' contains invalid keyframe times.");
            if (keyframe.value.kind != kind)
                throw new InvalidOperationException($"Animation track '{bindingId}' mixes incompatible value kinds.");
            previous = keyframe.time;
        }
    }
}

/// <summary>
/// Stores one stable event marker embedded in an animation clip.
/// </summary>
public sealed class AnimationEventMarker : ISerializable
{
    /// <summary>
    /// Gets or sets clip-local marker time in seconds.
    /// </summary>
    [SerializableProperty]
    public float time { get; set; }

    /// <summary>
    /// Gets or sets the stable event protocol identifier.
    /// </summary>
    [SerializableProperty]
    public string eventId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets opaque reload-safe event payload bytes.
    /// </summary>
    [SerializableProperty]
    public byte[] payload { get; set; } = [];
}

/// <summary>
/// Represents one imported backend-neutral collection of tracks and event markers.
/// </summary>
[StableTypeId("da0df43b-f6e4-4c84-b45f-d58aa4030e6f")]
public sealed class AnimationClipAsset : AssetObject
{
    /// <summary>
    /// Gets or sets the positive clip duration in seconds.
    /// </summary>
    [SerializableProperty]
    public float duration { get; set; }

    /// <summary>
    /// Gets or sets the ordered backend-neutral value tracks.
    /// </summary>
    [SerializableProperty]
    public AnimationTrack[] tracks { get; set; } = [];

    /// <summary>
    /// Gets or sets ordered stable event markers.
    /// </summary>
    [SerializableProperty]
    public AnimationEventMarker[] events { get; set; } = [];

    /// <summary>
    /// Validates the complete clip contract before import or playback.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when duration, tracks, bindings, values, or event markers are invalid.
    /// </exception>
    public void Validate()
    {
        if (!float.IsFinite(duration) || duration <= 0f)
            throw new InvalidOperationException("An animation clip requires a positive finite duration.");
        if (tracks is null || events is null)
            throw new InvalidOperationException("Animation clip collections cannot be null.");
        var bindings = new HashSet<string>(StringComparer.Ordinal);
        for (int index = 0; index < tracks.Length; index++)
        {
            AnimationTrack track = tracks[index]
                ?? throw new InvalidOperationException("Animation clip tracks cannot contain null entries.");
            track.Validate(duration);
            if (!bindings.Add(track.bindingId))
                throw new InvalidOperationException($"Animation binding '{track.bindingId}' is duplicated.");
        }
        float previous = -1f;
        for (int index = 0; index < events.Length; index++)
        {
            AnimationEventMarker marker = events[index]
                ?? throw new InvalidOperationException("Animation clip events cannot contain null entries.");
            if (!float.IsFinite(marker.time) || marker.time < previous || marker.time > duration)
                throw new InvalidOperationException("Animation clip event markers must be ordered within its duration.");
            if (string.IsNullOrWhiteSpace(marker.eventId))
                throw new InvalidOperationException("Animation event markers require stable event identifiers.");
            marker.payload ??= [];
            previous = marker.time;
        }
    }
}
