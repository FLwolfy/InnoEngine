namespace Inno.Editor.Core;

/// <summary>
/// Allows an editor module or panel to persist project-specific semantic state across editor sessions.
/// </summary>
public interface IEditorWorkspaceState
{
    /// <summary>
    /// Gets the stable, globally unique identifier used to store this provider's state.
    /// </summary>
    string workspaceStateId { get; }

    /// <summary>
    /// Captures the provider's current project-specific state.
    /// </summary>
    /// <param name="writer">The isolated state writer assigned to this provider.</param>
    void CaptureWorkspaceState(EditorWorkspaceStateWriter writer);

    /// <summary>
    /// Restores state previously captured for the same project and provider identifier.
    /// </summary>
    /// <param name="reader">
    /// The isolated state reader. Its <see cref="EditorWorkspaceStateReader.hasState"/> property is
    /// <see langword="false"/> when the project has no compatible saved state.
    /// </param>
    void RestoreWorkspaceState(EditorWorkspaceStateReader reader);
}
