using Inno.Core.Execution;
using System;
using System.Threading;

namespace Inno.Animation;

/// <summary>
/// Binds one animation service to the current asynchronous execution context.
/// </summary>
public static class AnimationExecutionContext
{
    private static readonly ExecutionSlot<IAnimationService> S_CURRENT_SCOPE = new("animation");

    /// <summary>
    /// Gets the animation service bound to the current execution context.
    /// </summary>
    public static IAnimationService current
        => S_CURRENT_SCOPE.current;

    /// <summary>
    /// Binds one animation service until the returned strict scope is disposed.
    /// </summary>
    /// <param name="animation">
    /// Session-owned animation service.
    /// </param>
    /// <returns>
    /// A strict last-in-first-out execution scope.
    /// </returns>
    public static IDisposable EnterScope(IAnimationService animation)
    {
        ArgumentNullException.ThrowIfNull(animation);
        return S_CURRENT_SCOPE.Enter(animation);
    }

}

/// <summary>
/// Provides script-friendly animation control through the current runtime context.
/// </summary>
public static class Animation
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
    public static AnimationPlaybackHandle Play(AnimationClipAsset clip, AnimationTarget target)
        => AnimationExecutionContext.current.Play(clip, target);

    /// <summary>
    /// Starts one clip with explicit playback options.
    /// </summary>
    /// <param name="clip">
    /// Valid imported animation clip.
    /// </param>
    /// <param name="options">
    /// Playback and blend options.
    /// </param>
    /// <returns>
    /// A generation-checked playback handle.
    /// </returns>
    /// <param name="target">
    /// The weak destination or explicit sampling-only policy.
    /// </param>
    public static AnimationPlaybackHandle Play(AnimationClipAsset clip, AnimationTarget target, AnimationPlayOptions options)
        => AnimationExecutionContext.current.Play(clip, target, options);

    /// <summary>
    /// Stops one live playback.
    /// </summary>
    /// <param name="handle">
    /// Playback handle to stop.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the playback was live.
    /// </returns>
    public static bool Stop(AnimationPlaybackHandle handle)
        => AnimationExecutionContext.current.Stop(handle);

    /// <summary>
    /// Pauses one live playback.
    /// </summary>
    /// <param name="handle">
    /// Playback handle to pause.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the state changed.
    /// </returns>
    public static bool Pause(AnimationPlaybackHandle handle)
        => AnimationExecutionContext.current.Pause(handle);

    /// <summary>
    /// Resumes one paused playback.
    /// </summary>
    /// <param name="handle">
    /// Playback handle to resume.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the state changed.
    /// </returns>
    public static bool Resume(AnimationPlaybackHandle handle)
        => AnimationExecutionContext.current.Resume(handle);

    /// <summary>
    /// Seeks one live playback to a clip-local time.
    /// </summary>
    /// <param name="handle">
    /// Playback handle to seek.
    /// </param>
    /// <param name="time">
    /// Clip-local time in seconds.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the handle was live.
    /// </returns>
    public static bool Seek(AnimationPlaybackHandle handle, float time)
        => AnimationExecutionContext.current.Seek(handle, time);

    /// <summary>
    /// Tries to read one live playback's state and position.
    /// </summary>
    /// <param name="handle">
    /// Playback handle to query.
    /// </param>
    /// <param name="state">
    /// Receives current playback state.
    /// </param>
    /// <param name="time">
    /// Receives clip-local time in seconds.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the handle was live.
    /// </returns>
    public static bool TryGetState(
        AnimationPlaybackHandle handle,
        out AnimationPlaybackState state,
        out float time)
        => AnimationExecutionContext.current.TryGetState(handle, out state, out time);
}
