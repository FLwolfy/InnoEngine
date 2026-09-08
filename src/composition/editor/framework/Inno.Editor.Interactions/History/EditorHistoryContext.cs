using System;

using Inno.Editor.Core;

namespace Inno.Editor.Interactions;

/// <summary>
/// Provides current-generation editor services to a history change handler.
/// </summary>
public sealed class EditorHistoryContext
{
    internal EditorHistoryContext(EditorContext editor, EditorInteractions interactions)
    {
        this.editor = editor ?? throw new ArgumentNullException(nameof(editor));
        this.interactions = interactions ?? throw new ArgumentNullException(nameof(interactions));
    }

    /// <summary>
    /// Gets the passive editor context for the active project.
    /// </summary>
    public EditorContext editor { get; }

    /// <summary>
    /// Gets the active interaction entry point used for selection and feature coordination.
    /// </summary>
    public EditorInteractions interactions { get; }
}
