using System;

namespace Inno.Editor.Interactions;

/// <summary>
/// Creates temporary history branches for transient editor workflows without exposing history internals.
/// </summary>
public interface IEditorHistoryIsolation
{
    /// <summary>
    /// Starts an independently disposable history branch while retaining the current editing branch.
    /// </summary>
    /// <returns>
    /// A lease that releases the temporary branch and restores the retained branch when disposed.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown while another history transition, transaction, or isolated branch is active.
    /// </exception>
    IDisposable BeginHistoryIsolation();
}
