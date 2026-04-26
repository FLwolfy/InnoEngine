namespace Inno.Editor.Core;

/// <summary>
/// Contract for one editor panel.
/// </summary>
public interface IEditorPanel
{
    /// <summary>
    /// Stable panel identifier.
    /// </summary>
    string id { get; }

    /// <summary>
    /// User-visible panel title.
    /// </summary>
    string title { get; }

    /// <summary>
    /// Gets or sets whether this panel is visible.
    /// </summary>
    bool isOpen { get; set; }

    /// <summary>
    /// Invoked once when panel is registered.
    /// </summary>
    /// <param name="context">Shared editor context.</param>
    void OnAttach(EditorContext context);

    /// <summary>
    /// Invoked once when panel is unregistered.
    /// </summary>
    /// <param name="context">Shared editor context.</param>
    void OnDetach(EditorContext context);

    /// <summary>
    /// Renders panel UI for current frame.
    /// </summary>
    /// <param name="context">Shared editor context.</param>
    void OnRender(EditorContext context);
}
