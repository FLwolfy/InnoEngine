using System;
using System.Collections.Generic;
using System.Linq;

using Inno.Animation.Runtime;
using Inno.Core.Events;
using Inno.Core.Identity;
using Xunit;

namespace Inno.Animation.Tests;

public sealed class AnimationRuntimeTests
{
    [Fact]
    public void PlaybackAdmissionReusesRetiredSlotsWithoutReusingHandles()
    {
        using var runtime = new AnimationRuntime(new EventDispatcher(), new RecordingSink(), maxPlaybacks: 1);
        AnimationPlaybackHandle previous = default;
        for (int frame = 0; frame < 256; frame++)
        {
            AnimationPlaybackHandle current = runtime.Play(CreateClip(), AnimationTarget.samplingOnly);
            Assert.NotEqual(previous, current);
            Assert.Throws<InvalidOperationException>(() => runtime.Play(CreateClip(), AnimationTarget.samplingOnly));
            Assert.Equal(1, runtime.playbackCount);
            Assert.True(runtime.Stop(current));
            Assert.False(runtime.Stop(previous));
            Assert.Equal(0, runtime.playbackCount);
            previous = current;
        }
        Assert.Equal(256, runtime.rejectedPlaybacks);
    }

    [Fact]
    public void RuntimeSamplesLinearlyAndRejectsRetiredHandles()
    {
        var events = new EventDispatcher();
        var sink = new RecordingSink();
        using var runtime = new AnimationRuntime(events, sink);
        AnimationPlaybackHandle handle = runtime.Play(CreateClip(), AnimationTarget.samplingOnly);

        runtime.Update(0.5f, 0.5f);

        AnimationSample sample = Assert.Single(sink.lastSamples);
        Assert.Equal("visual.opacity", sample.binding.value);
        Assert.Equal(0.5f, sample.value.x, 4);
        Assert.True(runtime.TryGetState(handle, out AnimationPlaybackState state, out float time));
        Assert.Equal(AnimationPlaybackState.Playing, state);
        Assert.Equal(0.5f, time, 4);
        Assert.True(runtime.Stop(handle));
        Assert.False(runtime.TryGetState(handle, out _, out _));

        AnimationPlaybackHandle replacement = runtime.Play(CreateClip(), AnimationTarget.samplingOnly);
        Assert.NotEqual(handle, replacement);
        Assert.False(runtime.Stop(handle));
    }

    [Fact]
    public void HighestLayerWinsAndEqualLayerValuesBlendByWeight()
    {
        var sink = new RecordingSink();
        using var runtime = new AnimationRuntime(new EventDispatcher(), sink);
        _ = runtime.Play(CreateConstantClip(0f), AnimationTarget.samplingOnly, new AnimationPlayOptions
        {
            speed = 1f,
            weight = 1f,
            layer = 0,
            clock = AnimationClock.Scaled
        });
        _ = runtime.Play(CreateConstantClip(0.25f), AnimationTarget.samplingOnly, new AnimationPlayOptions
        {
            speed = 1f,
            weight = 1f,
            layer = 2,
            clock = AnimationClock.Scaled
        });
        _ = runtime.Play(CreateConstantClip(0.75f), AnimationTarget.samplingOnly, new AnimationPlayOptions
        {
            speed = 1f,
            weight = 3f / 4f,
            layer = 2,
            clock = AnimationClock.Scaled
        });

        runtime.Update(0f, 0f);

        AnimationSample sample = Assert.Single(sink.lastSamples);
        Assert.Equal((0.25f + (0.75f * 0.75f)) / 1.75f, sample.value.x, 4);
    }

    [Fact]
    public void MarkerIsQueuedAndNaturalCompletionInvalidatesPlayback()
    {
        var dispatcher = new EventDispatcher();
        using EventHub hub = dispatcher.CreateHub();
        var markers = new List<AnimationMarkerEvent>();
        using IDisposable subscription = hub.Listen<AnimationMarkerEvent>(markers.Add);
        using var runtime = new AnimationRuntime(dispatcher, new RecordingSink());
        AnimationPlaybackHandle handle = runtime.Play(CreateClip(), AnimationTarget.samplingOnly);

        runtime.Update(1f, 1f);
        dispatcher.Flush();

        AnimationMarkerEvent marker = Assert.Single(markers);
        Assert.Equal("midpoint", marker.eventId);
        Assert.Equal(handle, marker.playback);
        Assert.False(runtime.TryGetState(handle, out _, out _));
    }

    [Fact]
    public void ClipValidationRejectsDuplicateBindingsAndInvalidTimes()
    {
        AnimationClipAsset clip = CreateClip();
        clip.tracks = [clip.tracks[0], clip.tracks[0]];
        Assert.Throws<InvalidOperationException>(clip.Validate);

        clip = CreateClip();
        clip.tracks[0].keyframes[1] = new AnimationKeyframe(2f, new AnimationValue(
            AnimationValueKind.Scalar,
            1f));
        Assert.Throws<InvalidOperationException>(clip.Validate);
    }

    [Fact]
    public void DestinationsDoNotBlendTogetherAndPlaybackFreezesAuthoringData()
    {
        var allocator = new IdentityAllocator();
        var first = new Destination();
        var second = new Destination();
        allocator.Register(first);
        allocator.Register(second);
        var firstTarget = new AnimationTarget(first.identity);
        var secondTarget = new AnimationTarget(second.identity);
        var sink = new RecordingSink();
        using var runtime = new AnimationRuntime(new EventDispatcher(), sink);
        AnimationClipAsset clip = CreateConstantClip(0.25f);
        runtime.Play(clip, firstTarget);
        runtime.Play(CreateConstantClip(0.75f), secondTarget);
        clip.tracks[0].keyframes[0] = new AnimationKeyframe(0f, new AnimationValue(AnimationValueKind.Scalar, 99f));
        runtime.Update(0f, 0f);
        Assert.Equal(2, sink.lastSamples.Count);
        Assert.Equal(0.25f, sink.lastSamples.Single(sample => sample.target.Equals(firstTarget)).value.x);
        Assert.Equal(0.75f, sink.lastSamples.Single(sample => sample.target.Equals(secondTarget)).value.x);
        allocator.Unregister(first);
        allocator.Register(new Destination(), firstTarget.persistentId);
        Assert.Null(firstTarget.Resolve<Destination>());
        Assert.Throws<ArgumentException>(() => runtime.Play(clip, default));
    }

    private sealed class Destination : IdentityObject;

    private static AnimationClipAsset CreateClip()
        => new()
        {
            duration = 1f,
            tracks =
            [
                new AnimationTrack(
                    "visual.opacity",
                    AnimationInterpolation.Linear,
                    [
                        new AnimationKeyframe(0f, new AnimationValue(AnimationValueKind.Scalar, 0f)),
                        new AnimationKeyframe(1f, new AnimationValue(AnimationValueKind.Scalar, 1f))
                    ])
            ],
            events =
            [
                new AnimationEventMarker
                {
                    time = 0.5f,
                    eventId = "midpoint",
                    payload = [1, 2, 3]
                }
            ]
        };

    private static AnimationClipAsset CreateConstantClip(float value)
        => new()
        {
            duration = 1f,
            tracks =
            [
                new AnimationTrack(
                    "visual.opacity",
                    AnimationInterpolation.Step,
                    [new AnimationKeyframe(0f, new AnimationValue(AnimationValueKind.Scalar, value))])
            ]
        };

    private sealed class RecordingSink : IAnimationBindingSink
    {
        internal IReadOnlyList<AnimationSample> lastSamples { get; private set; } = [];

        public void Apply(ReadOnlySpan<AnimationSample> samples)
        {
            lastSamples = samples.ToArray();
        }
    }
}
