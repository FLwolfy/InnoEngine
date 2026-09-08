using Inno.Runtime.Contracts;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

using Inno.Core.Events;

namespace Inno.Animation.Runtime;

/// <summary>
/// Owns animation playback, sampling, blending, and marker dispatch for one runtime session.
/// </summary>
public sealed class AnimationRuntime : RuntimeSubsystem, IAnimationService
{
    private readonly AnimationPlaybackAllocator m_handles = new();

    private readonly List<Playback?> m_playbacks = [];
    private readonly List<uint> m_slotGenerations = [];
    private readonly Stack<int> m_freeSlots = [];
    private readonly IAnimationBindingSink m_bindings;
    private readonly EventDispatcher m_events;
    private readonly uint m_runtimeGeneration;
    private bool m_disposed;
    private readonly int m_maxPlaybacks;
    private long m_rejectedPlaybacks;
    /// <summary>
    /// Captures snapshots and binds service façades.
    /// </summary>
    /// <param name="frame">
    /// The variable frame timing.
    /// </param>
    protected override void OnBeginFrame(RuntimeFrame frame) => OwnFrameScope(AnimationExecutionContext.EnterScope(this));
    /// <summary>
    /// Advances domain state on the variable clock.
    /// </summary>
    /// <param name="frame">
    /// The variable frame timing.
    /// </param>
    protected override void OnUpdate(RuntimeFrame frame) => Update(frame.deltaTime, frame.unscaledDeltaTime);

    /// <summary>
    /// Creates one isolated animation runtime generation.
    /// </summary>
    /// <param name="events">
    /// Session event dispatcher that receives marker events.
    /// </param>
    /// <param name="bindings">
    /// Required output boundary; sampling-only playback must use an explicit sampling target.
    /// </param>
    /// <param name="maxPlaybacks">
    /// Positive active playback capacity; rejection occurs before allocating a handle.
    /// </param>
    public AnimationRuntime(EventDispatcher events, IAnimationBindingSink bindings, int maxPlaybacks = 16384)
    {
        m_events = events ?? throw new ArgumentNullException(nameof(events));
        m_bindings = bindings ?? throw new ArgumentNullException(nameof(bindings));
        m_runtimeGeneration = m_handles.generation;
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxPlaybacks);
        m_maxPlaybacks = maxPlaybacks;
    }

    /// <summary>
    /// Starts one clip with the runtime's default playback policy.
    /// </summary>
    /// <param name="clip">
    /// Valid clip to sample until completion or explicit stop.
    /// </param>
    /// <returns>
    /// A generation-checked handle owned by this runtime.
    /// </returns>
    /// <exception cref="ObjectDisposedException">
    /// Thrown when this runtime has been disposed.
    /// </exception>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="clip"/> is null.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the clip is structurally invalid.
    /// </exception>
    /// <param name="target">
    /// The weak destination or explicit sampling-only policy.
    /// </param>
    public AnimationPlaybackHandle Play(AnimationClipAsset clip, AnimationTarget target)
        => Play(clip, target, AnimationPlayOptions.defaultValue);

    /// <summary>
    /// Starts one clip with an explicit clock, looping, speed, layer, and blend policy.
    /// </summary>
    /// <param name="clip">
    /// Valid clip to sample until completion or explicit stop.
    /// </param>
    /// <param name="options">
    /// Playback options validated before a runtime slot is allocated.
    /// </param>
    /// <returns>
    /// A generation-checked handle owned by this runtime.
    /// </returns>
    /// <exception cref="ObjectDisposedException">
    /// Thrown when this runtime has been disposed.
    /// </exception>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="clip"/> is null.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="options"/> contains a non-finite or unsupported value.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the clip is structurally invalid.
    /// </exception>
    /// <param name="target">
    /// The weak destination or explicit sampling-only policy.
    /// </param>
    public AnimationPlaybackHandle Play(AnimationClipAsset clip, AnimationTarget target, AnimationPlayOptions options)
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
        ArgumentNullException.ThrowIfNull(clip);
        if (!target.isValid)
            throw new ArgumentException("An explicit animation destination is required.", nameof(target));
        clip.Validate();
        ValidateOptions(options);
        if (m_playbacks.Count - m_freeSlots.Count >= m_maxPlaybacks)
        {
            m_rejectedPlaybacks++;
            throw new InvalidOperationException("Animation playback capacity has been reached.");
        }
        AnimationClipSnapshot snapshot = AnimationClipSnapshot.Capture(clip);
        int slot = m_freeSlots.Count > 0 ? m_freeSlots.Pop() : m_playbacks.Count;
        uint generation;
        if (slot == m_playbacks.Count)
        {
            generation = 1;
            m_slotGenerations.Add(generation);
        }
        else
        {
            generation = m_slotGenerations[slot];
        }
        var playback = new Playback(snapshot, target, options, generation);
        if (slot == m_playbacks.Count)
            m_playbacks.Add(playback);
        else
            m_playbacks[slot] = playback;
        return m_handles.Create(slot, generation);
    }

    /// <summary>
    /// Gets active animation playback slots.
    /// </summary>
    public int playbackCount => m_playbacks.Count - m_freeSlots.Count;

    /// <summary>
    /// Gets playback admissions rejected by finite capacity.
    /// </summary>
    public long rejectedPlaybacks => m_rejectedPlaybacks;

    /// <summary>
    /// Stops and retires one live playback slot.
    /// </summary>
    /// <param name="handle">
    /// Playback handle produced by this runtime generation.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when a live playback was retired; otherwise, <see langword="false"/>.
    /// </returns>
    public bool Stop(AnimationPlaybackHandle handle)
    {
        if (!TryGetPlayback(handle, out Playback? playback))
            return false;
        var identity = m_handles.Decode(handle);
        Retire(identity.slot, playback);
        return true;
    }

    /// <summary>
    /// Pauses one live playback without changing its clip-local time.
    /// </summary>
    /// <param name="handle">
    /// Playback handle produced by this runtime generation.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when a playing playback became paused; otherwise, <see langword="false"/>.
    /// </returns>
    public bool Pause(AnimationPlaybackHandle handle)
    {
        if (!TryGetPlayback(handle, out Playback? playback) || playback.state == AnimationPlaybackState.Paused)
            return false;
        playback.state = AnimationPlaybackState.Paused;
        return true;
    }

    /// <summary>
    /// Resumes one paused playback from its current clip-local time.
    /// </summary>
    /// <param name="handle">
    /// Playback handle produced by this runtime generation.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when a paused playback resumed; otherwise, <see langword="false"/>.
    /// </returns>
    public bool Resume(AnimationPlaybackHandle handle)
    {
        if (!TryGetPlayback(handle, out Playback? playback) || playback.state == AnimationPlaybackState.Playing)
            return false;
        playback.state = AnimationPlaybackState.Playing;
        return true;
    }

    /// <summary>
    /// Seeks one live playback to a clamped clip-local time.
    /// </summary>
    /// <param name="handle">
    /// Playback handle produced by this runtime generation.
    /// </param>
    /// <param name="time">
    /// Finite clip-local time in seconds.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the handle was live and its time was updated; otherwise, <see langword="false"/>.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="time"/> is not finite.
    /// </exception>
    public bool Seek(AnimationPlaybackHandle handle, float time)
    {
        if (!float.IsFinite(time))
            throw new ArgumentOutOfRangeException(nameof(time));
        if (!TryGetPlayback(handle, out Playback? playback))
            return false;
        playback.time = Math.Clamp(time, 0f, playback.clip.duration);
        return true;
    }

    /// <summary>
    /// Tries to read the state and clip-local time of one live playback.
    /// </summary>
    /// <param name="handle">
    /// Playback handle produced by this runtime generation.
    /// </param>
    /// <param name="state">
    /// Receives the live playback state, or the default value when the handle is stale.
    /// </param>
    /// <param name="time">
    /// Receives clip-local time in seconds, or zero when the handle is stale.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the handle identifies a live playback; otherwise, <see langword="false"/>.
    /// </returns>
    public bool TryGetState(
        AnimationPlaybackHandle handle,
        out AnimationPlaybackState state,
        out float time)
    {
        if (TryGetPlayback(handle, out Playback? playback))
        {
            state = playback.state;
            time = playback.time;
            return true;
        }
        state = default;
        time = 0f;
        return false;
    }

    /// <summary>
    /// Advances every playback and applies one deterministic blended sample batch.
    /// </summary>
    /// <param name="scaledDeltaTime">
    /// Non-negative scaled frame interval.
    /// </param>
    /// <param name="unscaledDeltaTime">
    /// Non-negative unscaled frame interval.
    /// </param>
    public void Update(float scaledDeltaTime, float unscaledDeltaTime)
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
        if (!float.IsFinite(scaledDeltaTime) || scaledDeltaTime < 0f)
            throw new ArgumentOutOfRangeException(nameof(scaledDeltaTime));
        if (!float.IsFinite(unscaledDeltaTime) || unscaledDeltaTime < 0f)
            throw new ArgumentOutOfRangeException(nameof(unscaledDeltaTime));

        var samples = new Dictionary<(AnimationTarget target, AnimationBindingId binding), BlendAccumulator>();
        for (int slot = 0; slot < m_playbacks.Count; slot++)
        {
            Playback? playback = m_playbacks[slot];
            if (playback is null)
                continue;
            AnimationPlaybackHandle handle = m_handles.Create(
                slot,
                playback.generation);
            float previousTime = playback.time;
            if (playback.state == AnimationPlaybackState.Playing)
            {
                float delta = playback.options.clock == AnimationClock.Scaled
                    ? scaledDeltaTime
                    : unscaledDeltaTime;
                bool completed = Advance(
                    handle,
                    playback,
                    previousTime,
                    delta * playback.options.speed);
                Sample(playback, samples);
                if (completed)
                    Retire(slot, playback);
                continue;
            }
            Sample(playback, samples);
        }

        AnimationSample[] output = samples
            .OrderBy(static pair => pair.Key.target.persistentId)
            .ThenBy(static pair => pair.Key.binding.value, StringComparer.Ordinal)
            .Select(static pair => new AnimationSample(pair.Key.target, pair.Key.binding, pair.Value.Complete()))
            .ToArray();
        m_bindings.Apply(output);
    }

    /// <summary>
    /// Releases every live playback in this runtime generation.
    /// </summary>
    protected override void OnStop()
    {
        if (m_disposed)
            return;
        m_disposed = true;
        m_playbacks.Clear();
        m_slotGenerations.Clear();
        m_freeSlots.Clear();
    }

    private bool Advance(
        AnimationPlaybackHandle handle,
        Playback playback,
        float previousTime,
        float delta)
    {
        if (delta <= 0f)
            return false;
        float duration = playback.clip.duration;
        float total = previousTime + delta;
        if (!playback.options.loop && total >= duration)
        {
            EmitMarkers(handle, playback.clip, previousTime, duration);
            playback.time = duration;
            return true;
        }

        if (!playback.options.loop)
        {
            playback.time = total;
            EmitMarkers(handle, playback.clip, previousTime, playback.time);
            return false;
        }

        float cursor = previousTime;
        float remaining = delta;
        while (remaining > 0f)
        {
            float segment = Math.Min(remaining, duration - cursor);
            EmitMarkers(handle, playback.clip, cursor, cursor + segment);
            remaining -= segment;
            cursor += segment;
            if (cursor >= duration)
                cursor = 0f;
        }
        playback.time = cursor;
        return false;
    }

    private void EmitMarkers(
        AnimationPlaybackHandle handle,
        AnimationClipSnapshot clip,
        float startExclusive,
        float endInclusive)
    {
        for (int index = 0; index < clip.events.Length; index++)
        {
            AnimationEventMarker marker = clip.events[index];
            if (marker.time > startExclusive && marker.time <= endInclusive)
            {
                m_events.Enqueue(new AnimationMarkerEvent(
                    handle,
                    marker.eventId,
                    marker.payload));
            }
        }
    }

    private static void Sample(
        Playback playback,
        IDictionary<(AnimationTarget target, AnimationBindingId binding), BlendAccumulator> samples)
    {
        if (playback.options.weight <= 0f)
            return;
        for (int index = 0; index < playback.clip.tracks.Length; index++)
        {
            AnimationTrack track = playback.clip.tracks[index];
            var binding = new AnimationBindingId(track.bindingId);
            var destination = (playback.target, binding);
            AnimationValue value = SampleTrack(track, playback.time);
            if (!samples.TryGetValue(destination, out BlendAccumulator accumulator) ||
                playback.options.layer > accumulator.layer)
            {
                samples[destination] = new BlendAccumulator(
                    playback.options.layer,
                    value,
                    playback.options.weight);
                continue;
            }
            if (playback.options.layer == accumulator.layer)
            {
                accumulator.Add(value, playback.options.weight);
                samples[destination] = accumulator;
            }
        }
    }

    private static AnimationValue SampleTrack(AnimationTrack track, float time)
    {
        AnimationKeyframe[] keys = track.keyframes;
        if (time <= keys[0].time)
            return keys[0].value;
        for (int index = 1; index < keys.Length; index++)
        {
            if (time > keys[index].time)
                continue;
            AnimationKeyframe left = keys[index - 1];
            AnimationKeyframe right = keys[index];
            if (track.interpolation == AnimationInterpolation.Step || right.time <= left.time)
                return left.value;
            return AnimationSampling.Interpolate(
                left.value,
                right.value,
                (time - left.time) / (right.time - left.time));
        }
        return keys[^1].value;
    }

    private bool TryGetPlayback(
        AnimationPlaybackHandle handle,
        [NotNullWhen(true)] out Playback? playback)
    {
        playback = null;
        var identity = m_handles.Decode(handle);
        if (m_disposed || !handle.isValid || handle.runtimeGeneration != m_runtimeGeneration ||
            identity.slot < 0 || identity.slot >= m_playbacks.Count)
        {
            return false;
        }
        playback = m_playbacks[identity.slot];
        return playback is not null && playback.generation == identity.generation;
    }

    private void Retire(int slot, Playback playback)
    {
        uint next = unchecked(playback.generation + 1);
        if (next == 0)
            next = 1;
        m_slotGenerations[slot] = next;
        m_playbacks[slot] = null;
        m_freeSlots.Push(slot);
    }

    private static void ValidateOptions(AnimationPlayOptions options)
    {
        if (!float.IsFinite(options.speed) || options.speed < 0f)
            throw new ArgumentOutOfRangeException(nameof(options), "Animation speed must be finite and non-negative.");
        if (!float.IsFinite(options.weight) || options.weight < 0f || options.weight > 1f)
            throw new ArgumentOutOfRangeException(nameof(options), "Animation weight must be between zero and one.");
    }

    private sealed class Playback(
        AnimationClipSnapshot clip,
        AnimationTarget target,
        AnimationPlayOptions options,
        uint generation)
    {
        internal AnimationClipSnapshot clip { get; } = clip;

        internal AnimationTarget target { get; } = target;

        internal AnimationPlayOptions options { get; } = options;

        internal uint generation { get; set; } = generation;

        internal AnimationPlaybackState state { get; set; } = AnimationPlaybackState.Playing;

        internal float time { get; set; }
    }

    private struct BlendAccumulator(int layer, AnimationValue value, float weight)
    {
        private AnimationValue m_sum = Multiply(value, weight);
        private float m_weight = weight;

        internal int layer { get; } = layer;

        internal void Add(AnimationValue value, float weight)
        {
            m_sum = AnimationSampling.AddWeighted(m_sum, value, weight);
            m_weight += weight;
        }

        internal AnimationValue Complete()
            => AnimationSampling.CompleteWeighted(m_sum, 1f / m_weight);

        private static AnimationValue Multiply(AnimationValue value, float weight)
            => new(
                value.kind,
                value.x * weight,
                value.y * weight,
                value.z * weight,
                value.w * weight);
    }

}
