using System;

using Inno.Runtime;

namespace Inno.Editor.Scene;

/// <summary>
/// Creates isolated runtime scene sessions from the current editable scene set.
/// </summary>
public interface IEditorScenePlayMode
{
    /// <summary>
    /// Captures the editable scene set and materializes independent runtime copies in the supplied session.
    /// </summary>
    /// <param name="runtimeSession">
    /// The isolated Play Mode session that receives the immutable start snapshot and owns editor-side
    /// operations performed against the runtime copies.
    /// </param>
    /// <returns>
    /// A lease that presents the runtime copies to editor features, prevents their persistence, and
    /// restores the Edit presentation when disposed.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="runtimeSession"/> is null.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when another Play Mode lease is active, the target world is not empty, or the snapshot
    /// cannot be materialized atomically.
    /// </exception>
    IDisposable BeginPlayMode(RuntimeSession runtimeSession);
}
