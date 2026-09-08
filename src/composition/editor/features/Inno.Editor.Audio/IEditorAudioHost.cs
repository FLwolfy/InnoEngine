using Inno.Runtime.Contracts;
using System;
using Inno.Audio;
using Inno.Runtime;

namespace Inno.Editor.Audio;

/// <summary>
/// Coordinates isolated audio runtime generations for Edit and Play Mode sessions.
/// </summary>
public interface IEditorAudioHost : IDisposable
{
    /// <summary>
    /// Creates the reusable factory that contributes isolated audio runtimes to Editor sessions.
    /// </summary>
    /// <returns>
    /// A factory whose features are owned and released by their runtime sessions.
    /// </returns>
    /// <param name="session">
    /// The explicit session being composed.
    /// </param>
    IRuntimeSubsystemFactory CreateRuntimeSubsystemFactory(RuntimeSession session);

    /// <summary>
    /// Binds the script-facing <see cref="Audio"/> façade to one session's audio runtime.
    /// </summary>
    /// <param name="session">
    /// Session whose feature pipeline owns an active Editor audio generation.
    /// </param>
    /// <returns>
    /// A strict last-in-first-out execution scope.
    /// </returns>
    IDisposable EnterExecutionScope(RuntimeSession session);

    /// <summary>
    /// Starts an Editor-owned preview voice through an active Edit session.
    /// </summary>
    /// <param name="session">
    /// Active Edit Mode session.
    /// </param>
    /// <param name="clip">
    /// Imported audio clip to preview.
    /// </param>
    /// <param name="options">
    /// Optional playback parameters; omitted values use engine defaults.
    /// </param>
    /// <returns>
    /// The preview voice handle, initially in the preparing state.
    /// </returns>
    AudioVoiceHandle PlayPreview(
        RuntimeSession session,
        AudioClipAsset clip,
        AudioPlayOptions? options = null);

    /// <summary>
    /// Stops one preview voice owned by an active Edit session.
    /// </summary>
    /// <param name="session">
    /// Active Edit Mode session.
    /// </param>
    /// <param name="voice">
    /// Preview voice to stop.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when a live preview was stopped.
    /// </returns>
    bool StopPreview(RuntimeSession session, AudioVoiceHandle voice);
}
