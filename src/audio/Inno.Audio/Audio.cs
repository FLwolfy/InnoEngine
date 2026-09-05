using System;
using System.Threading;
using System.Threading.Tasks;

namespace Inno.Audio;

/// <summary>
/// Binds one host-owned audio service to the current asynchronous execution context.
/// </summary>
public static class AudioExecutionContext
{
    private static readonly AsyncLocal<Scope?> S_CURRENT_SCOPE = new();

    /// <summary>
    /// Gets the audio service bound to the current asynchronous execution context.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no audio service is active for the caller.
    /// </exception>
    public static IAudioService current
        => S_CURRENT_SCOPE.Value?.audio
            ?? throw new InvalidOperationException(
                "No audio service is bound to the current runtime execution context.");

    /// <summary>
    /// Binds an audio service until the returned strict last-in-first-out scope is disposed.
    /// </summary>
    /// <param name="audio">
    /// Host-owned audio service exposed to script-facing operations.
    /// </param>
    /// <returns>
    /// A strict last-in-first-out scope owned by the caller.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="audio"/> is <see langword="null"/>.
    /// </exception>
    public static IDisposable EnterScope(IAudioService audio)
    {
        ArgumentNullException.ThrowIfNull(audio);
        var scope = new Scope(audio, S_CURRENT_SCOPE.Value);
        S_CURRENT_SCOPE.Value = scope;
        return scope;
    }

    private sealed class Scope(IAudioService audio, Scope? parent) : IDisposable
    {
        private bool m_disposed;

        internal IAudioService audio { get; } = audio;

        /// <summary>
        /// Restores the parent audio execution scope in strict last-in-first-out order.
        /// </summary>
        public void Dispose()
        {
            if (m_disposed)
                return;
            if (!ReferenceEquals(S_CURRENT_SCOPE.Value, this))
            {
                throw new InvalidOperationException(
                    "Audio execution scopes must be disposed in last-in-first-out order.");
            }
            m_disposed = true;
            S_CURRENT_SCOPE.Value = parent;
        }
    }
}

/// <summary>
/// Provides script-friendly access to the audio service in the current execution context.
/// </summary>
public static class Audio
{
    /// <summary>
    /// Gets immutable capabilities for the current audio device generation.
    /// </summary>
    public static AudioCapabilities capabilities => AudioExecutionContext.current.capabilities;

    /// <summary>
    /// Gets the current output availability state.
    /// </summary>
    public static AudioDeviceState deviceState => AudioExecutionContext.current.deviceState;

    /// <summary>
    /// Gets the monotonic audio clock in seconds.
    /// </summary>
    public static double dspTime => AudioExecutionContext.current.dspTime;

    /// <summary>
    /// Gets current audio resource statistics.
    /// </summary>
    public static AudioStatistics statistics => AudioExecutionContext.current.statistics;

    /// <summary>
    /// Starts one clip using default playback parameters.
    /// </summary>
    /// <param name="clip">
    /// Imported clip to prepare and play.
    /// </param>
    /// <returns>
    /// A voice handle that may initially be preparing.
    /// </returns>
    public static AudioVoiceHandle Play(AudioClipAsset clip)
        => AudioExecutionContext.current.Play(clip);

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
    /// A voice handle that may initially be preparing.
    /// </returns>
    public static AudioVoiceHandle Play(AudioClipAsset clip, AudioPlayOptions options)
        => AudioExecutionContext.current.Play(clip, options);

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
    public static AudioVoiceHandle PlayScheduled(
        AudioClipAsset clip,
        double scheduledDspTime,
        AudioPlayOptions options)
        => AudioExecutionContext.current.PlayScheduled(clip, scheduledDspTime, options);

    /// <summary>
    /// Stops a live voice.
    /// </summary>
    /// <param name="voice">
    /// Voice handle to stop.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when a live voice was stopped.
    /// </returns>
    public static bool Stop(AudioVoiceHandle voice) => AudioExecutionContext.current.Stop(voice);

    /// <summary>
    /// Pauses a live voice.
    /// </summary>
    /// <param name="voice">
    /// Voice handle to pause.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the voice entered the paused state.
    /// </returns>
    public static bool Pause(AudioVoiceHandle voice) => AudioExecutionContext.current.Pause(voice);

    /// <summary>
    /// Resumes a paused voice.
    /// </summary>
    /// <param name="voice">
    /// Voice handle to resume.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the voice resumed.
    /// </returns>
    public static bool Resume(AudioVoiceHandle voice) => AudioExecutionContext.current.Resume(voice);

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
    public static bool Seek(AudioVoiceHandle voice, TimeSpan position)
        => AudioExecutionContext.current.Seek(voice, position);

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
    public static bool SetVoiceParameters(AudioVoiceHandle voice, AudioVoiceParameters parameters)
        => AudioExecutionContext.current.SetVoiceParameters(voice, parameters);

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
    public static bool TryGetVoiceState(AudioVoiceHandle voice, out AudioPlaybackState playbackState)
        => AudioExecutionContext.current.TryGetVoiceState(voice, out playbackState);

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
    public static bool SetBusVolume(AudioBusId bus, float volume)
        => AudioExecutionContext.current.SetBusVolume(bus, volume);

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
    public static bool SetBusMuted(AudioBusId bus, bool muted)
        => AudioExecutionContext.current.SetBusMuted(bus, muted);

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
    public static bool SetBusPaused(AudioBusId bus, bool paused)
        => AudioExecutionContext.current.SetBusPaused(bus, paused);

    /// <summary>
    /// Prepares a clip and retains it in the runtime cache.
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
    public static ValueTask PreloadAsync(
        AudioClipAsset clip,
        AudioClipLoadMode loadMode = AudioClipLoadMode.Automatic,
        CancellationToken cancellationToken = default)
        => AudioExecutionContext.current.PreloadAsync(clip, loadMode, cancellationToken);

    /// <summary>
    /// Releases one explicit preload retention without interrupting active voices.
    /// </summary>
    /// <param name="clip">
    /// Imported clip whose preload retention should be released.
    /// </param>
    public static void ReleasePreload(AudioClipAsset clip)
        => AudioExecutionContext.current.ReleasePreload(clip);
}
