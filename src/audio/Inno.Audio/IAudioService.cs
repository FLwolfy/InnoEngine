using System;
using System.Threading;
using System.Threading.Tasks;

namespace Inno.Audio;

/// <summary>
/// Provides backend-neutral audio playback, caching, mixer, and device-state services.
/// </summary>
public interface IAudioService
{
    /// <summary>
    /// Gets immutable capabilities for the current device generation.
    /// </summary>
    AudioCapabilities capabilities { get; }

    /// <summary>
    /// Gets the current audio output state.
    /// </summary>
    AudioDeviceState deviceState { get; }

    /// <summary>
    /// Gets the monotonic audio clock in seconds.
    /// </summary>
    double dspTime { get; }

    /// <summary>
    /// Gets current runtime and backend resource statistics.
    /// </summary>
    AudioStatistics statistics { get; }

    /// <summary>
    /// Starts one clip using default playback parameters.
    /// </summary>
    /// <param name="clip">
    /// Imported clip to prepare and play.
    /// </param>
    /// <returns>
    /// A voice handle that may initially be in the preparing state.
    /// </returns>
    AudioVoiceHandle Play(AudioClipAsset clip);

    /// <summary>
    /// Starts one clip using explicit playback parameters.
    /// </summary>
    /// <param name="clip">
    /// Imported clip to prepare and play.
    /// </param>
    /// <param name="options">
    /// Immutable playback parameters.
    /// </param>
    /// <returns>
    /// A voice handle that may initially be in the preparing state.
    /// </returns>
    AudioVoiceHandle Play(AudioClipAsset clip, AudioPlayOptions options);

    /// <summary>
    /// Schedules one clip against the monotonic audio clock.
    /// </summary>
    /// <param name="clip">
    /// Imported clip to prepare and play.
    /// </param>
    /// <param name="scheduledDspTime">
    /// Absolute audio-clock start time in seconds.
    /// </param>
    /// <param name="options">
    /// Immutable playback parameters.
    /// </param>
    /// <returns>
    /// A voice handle in preparing or scheduled state.
    /// </returns>
    AudioVoiceHandle PlayScheduled(
        AudioClipAsset clip,
        double scheduledDspTime,
        AudioPlayOptions options);

    /// <summary>
    /// Stops a live voice.
    /// </summary>
    /// <param name="voice">
    /// Voice handle to stop.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when a live voice was stopped.
    /// </returns>
    bool Stop(AudioVoiceHandle voice);

    /// <summary>
    /// Pauses a live voice.
    /// </summary>
    /// <param name="voice">
    /// Voice handle to pause.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the voice entered the paused state.
    /// </returns>
    bool Pause(AudioVoiceHandle voice);

    /// <summary>
    /// Resumes a paused voice.
    /// </summary>
    /// <param name="voice">
    /// Voice handle to resume.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the voice resumed.
    /// </returns>
    bool Resume(AudioVoiceHandle voice);

    /// <summary>
    /// Moves a live voice cursor to a clip-relative position.
    /// </summary>
    /// <param name="voice">
    /// Voice handle to seek.
    /// </param>
    /// <param name="position">
    /// Non-negative clip-relative position.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the cursor was updated.
    /// </returns>
    bool Seek(AudioVoiceHandle voice, TimeSpan position);

    /// <summary>
    /// Replaces mutable parameters for a live voice.
    /// </summary>
    /// <param name="voice">
    /// Voice handle to update.
    /// </param>
    /// <param name="parameters">
    /// Current voice parameter snapshot.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the voice was updated.
    /// </returns>
    bool SetVoiceParameters(AudioVoiceHandle voice, AudioVoiceParameters parameters);

    /// <summary>
    /// Queries the current state of a voice.
    /// </summary>
    /// <param name="voice">
    /// Voice handle to query.
    /// </param>
    /// <param name="playbackState">
    /// Receives the current state when the handle is known.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the handle belongs to a known voice.
    /// </returns>
    bool TryGetVoiceState(AudioVoiceHandle voice, out AudioPlaybackState playbackState);

    /// <summary>
    /// Updates linear gain for a semantic mixer bus.
    /// </summary>
    /// <param name="bus">
    /// Stable bus identifier.
    /// </param>
    /// <param name="volume">
    /// Non-negative linear gain.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the bus exists and was updated.
    /// </returns>
    bool SetBusVolume(AudioBusId bus, float volume);

    /// <summary>
    /// Updates mute state for a semantic mixer bus.
    /// </summary>
    /// <param name="bus">
    /// Stable bus identifier.
    /// </param>
    /// <param name="muted">
    /// Whether output from the bus is silenced.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the bus exists and was updated.
    /// </returns>
    bool SetBusMuted(AudioBusId bus, bool muted);

    /// <summary>
    /// Updates pause state for a semantic mixer bus.
    /// </summary>
    /// <param name="bus">
    /// Stable bus identifier.
    /// </param>
    /// <param name="paused">
    /// Whether routed voices should pause.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the bus exists and was updated.
    /// </returns>
    bool SetBusPaused(AudioBusId bus, bool paused);

    /// <summary>
    /// Prepares a clip and retains it in the decoded or streamed cache.
    /// </summary>
    /// <param name="clip">
    /// Imported clip to prepare.
    /// </param>
    /// <param name="loadMode">
    /// Storage strategy override.
    /// </param>
    /// <param name="cancellationToken">
    /// Cancellation token observed before the cache commit.
    /// </param>
    /// <returns>
    /// An asynchronous operation that completes after the cache entry is ready.
    /// </returns>
    ValueTask PreloadAsync(
        AudioClipAsset clip,
        AudioClipLoadMode loadMode = AudioClipLoadMode.Automatic,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Releases one explicit preload retention without interrupting active voices.
    /// </summary>
    /// <param name="clip">
    /// Imported clip whose preload retention should be released.
    /// </param>
    void ReleasePreload(AudioClipAsset clip);
}
