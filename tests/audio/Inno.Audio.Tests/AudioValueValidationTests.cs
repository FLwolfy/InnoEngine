using System;
using Inno.Core.Mathematics;
using Inno.References;
using Xunit;

namespace Inno.Audio.Tests;

public sealed class AudioValueValidationTests
{
    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity)]
    public void PlaybackAndLiveParametersRejectAllNonFiniteScalars(float invalid)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new AudioPlayOptions(volume: invalid));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AudioPlayOptions(pitch: invalid));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AudioPlayOptions(pan: invalid));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AudioVoiceParameters(invalid, 1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AudioVoiceParameters(1, invalid, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AudioVoiceParameters(1, 1, invalid));
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity)]
    public void SpatialParametersRejectNonFiniteAttenuationAndConeScalars(float invalid)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new AudioSpatialOptions(default, default, default, minDistance: invalid));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AudioSpatialOptions(default, default, default, maxDistance: invalid));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AudioSpatialOptions(default, default, default, rolloff: invalid));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AudioSpatialOptions(default, default, default, coneInnerAngle: invalid));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AudioSpatialOptions(default, default, default, coneOuterAngle: invalid));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AudioSpatialOptions(default, default, default, coneOuterGain: invalid));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AudioSpatialOptions(default, default, default, dopplerFactor: invalid));
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity)]
    public void ListenerAndSourceRejectNonFiniteComponentsOnEveryVectorAxis(float invalid)
    {
        foreach (Vector3 vector in new[] { new Vector3(invalid, 0, 0), new Vector3(0, invalid, 0), new Vector3(0, 0, invalid) })
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new AudioSpatialOptions(vector, default, default));
            Assert.Throws<ArgumentOutOfRangeException>(() => new AudioSpatialOptions(default, vector, default));
            Assert.Throws<ArgumentOutOfRangeException>(() => new AudioSpatialOptions(default, default, vector));
            Assert.Throws<ArgumentOutOfRangeException>(() => new AudioListenerState(vector, default, default, default));
            Assert.Throws<ArgumentOutOfRangeException>(() => new AudioListenerState(default, vector, default, default));
            Assert.Throws<ArgumentOutOfRangeException>(() => new AudioListenerState(default, default, vector, default));
            Assert.Throws<ArgumentOutOfRangeException>(() => new AudioListenerState(default, default, default, vector));
        }
    }

    [Fact]
    public void UndefinedModesAreRejectedAndFiniteBoundaryValuesRemainUsable()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new AudioPlayOptions(loadMode: (AudioClipLoadMode)999));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AudioSpatialOptions(default, default, default, (AudioDistanceModel)999));
        var options = new AudioPlayOptions(volume: 0, pitch: float.Epsilon, pan: -1);
        Assert.Equal(0, options.volume);
        Assert.Equal(float.Epsilon, options.pitch);
        Assert.Equal(-1, options.pan);
        var spatial = new AudioSpatialOptions(default, default, default, minDistance: 0, maxDistance: 0,
            rolloff: 0, coneInnerAngle: 0, coneOuterAngle: 360, coneOuterGain: 0, dopplerFactor: 0);
        Assert.Equal(360, spatial.coneOuterAngle);
    }

    [Fact]
    public void AnEmitterWithDefaultPlaybackOptionsInvalidatesItsContribution()
    {
        using ContentReadScope content = ContentReadScope.empty;
        using var context = new AudioContentProviderContext(content, 0);
        var emitter = new AudioEmitterSnapshot(Guid.NewGuid(), new AudioClipAsset(), default, true);
        Assert.Throws<ArgumentException>(() => context.Submit(emitter));
        Assert.Throws<InvalidOperationException>(() => _ = context.emitters);
    }
}
