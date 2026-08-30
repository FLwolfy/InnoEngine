using System;

namespace Inno.Editor.Scene;

/// <summary>Creates isolated runtime scene sessions from the current editable scene set.</summary>
public interface IEditorScenePlayMode
{
    /// <summary>
    /// Captures every editable scene and replaces the loaded set with independent runtime copies.
    /// </summary>
    /// <returns>A session that restores the complete captured editing state.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when another runtime scene session is active or the scene set cannot be replaced atomically.
    /// </exception>
    IEditorScenePlayModeSession BeginPlayMode();
}

/// <summary>Owns one captured editing scene set while its runtime copies are active.</summary>
public interface IEditorScenePlayModeSession : IDisposable
{
    /// <summary>
    /// Discards every runtime scene and restores the captured editable scenes, active scene, and selection.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the captured editing scene set cannot be restored completely.
    /// </exception>
    void Restore();
}
