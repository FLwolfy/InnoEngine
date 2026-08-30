namespace Inno.Editor.Interactions;

/// <summary>Provides the narrow selection boundary used by editor features that replace object instances.</summary>
public interface IEditorSelectionCoordinator
{
    /// <summary>Gets the currently selected target, or <see langword="null"/> when selection is empty.</summary>
    object? selectedTarget { get; }

    /// <summary>Replaces the current target after closing presentations owned by another target.</summary>
    /// <param name="target">The new target, or <see langword="null"/> to clear selection.</param>
    void SetSelection(object? target);
}
