using System;

using Inno.Core.Logging;

namespace Inno.Editor.PlayMode;

/// <summary>
/// Controls the editor's isolated game-simulation session.
/// </summary>
public interface IEditorPlayMode
{
    /// <summary>
    /// Gets the current Play Mode transition state.
    /// </summary>
    EditorPlayModeState state { get; }

    /// <summary>
    /// Gets whether isolated runtime scenes are actively simulating.
    /// </summary>
    bool isPlaying { get; }

    /// <summary>
    /// Gets the most recent transition or simulation failure, or <see langword="null"/> when no failure is active.
    /// </summary>
    string? lastFailure { get; }

    /// <summary>
    /// Gets the active isolated runtime log session, or <see cref="LogSessionId.none"/> outside a Play Mode request.
    /// </summary>
    LogSessionId activeSessionId { get; }

    /// <summary>
    /// Occurs after <see cref="state"/> changes.
    /// </summary>
    event Action<EditorPlayModeState>? stateChanged;

    /// <summary>
    /// Requests entry after the active script generation becomes ready.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when a new entry request was accepted.
    /// </returns>
    bool EnterPlayMode();

    /// <summary>
    /// Requests disposal of the active runtime session or dismisses a failed transition.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when entry was cancelled or a new exit request was accepted.
    /// </returns>
    bool ExitPlayMode();
}
