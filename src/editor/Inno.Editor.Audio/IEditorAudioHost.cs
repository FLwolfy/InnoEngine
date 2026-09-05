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
    /// Creates and owns one audio runtime for an isolated editor runtime session.
    /// </summary>
    /// <param name="session">
    /// Edit or Play Mode session to bind.
    /// </param>
    /// <returns>
    /// A lease that releases the audio runtime before the owning session is disposed.
    /// </returns>
    IDisposable BeginSession(RuntimeSession session);

    /// <summary>
    /// Binds the script-facing <see cref="Audio"/> façade to one session's audio runtime.
    /// </summary>
    /// <param name="session">
    /// Session with an active audio lease.
    /// </param>
    /// <returns>
    /// A strict last-in-first-out execution scope.
    /// </returns>
    IDisposable EnterExecutionScope(RuntimeSession session);

    /// <summary>
    /// Advances one session's providers, native graph, and completion dispatch at a frame-safe point.
    /// </summary>
    /// <param name="session">
    /// Session with an active audio lease.
    /// </param>
    /// <param name="deltaTime">
    /// Non-negative elapsed frame time in seconds.
    /// </param>
    void Update(RuntimeSession session, float deltaTime);

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
