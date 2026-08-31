using System;

using Inno.Editor.Core;
using Inno.Editor.Interactions;

namespace Inno.Editor.Inspection;

/// <summary>
/// Provides services and state to an inspector drawer.
/// </summary>
public sealed class InspectionDrawContext
{
    /// <summary>
    /// Gets the shared editor context.
    /// </summary>
    public EditorContext editorContext { get; }

    /// <summary>Gets the active editor interaction entry point.</summary>
    public EditorInteractions interactions { get; }

    /// <summary>
    /// Gets the selected target.
    /// </summary>
    public object target { get; }

    /// <summary>
    /// Gets the serialized property renderer.
    /// </summary>
    public SerializedPropertyRenderer properties { get; }

    internal InspectionDrawContext(
        EditorContext editorContext,
        EditorInteractions interactions,
        object target,
        SerializedPropertyRenderer properties)
    {
        this.editorContext = editorContext ?? throw new ArgumentNullException(nameof(editorContext));
        this.interactions = interactions ?? throw new ArgumentNullException(nameof(interactions));
        this.target = target ?? throw new ArgumentNullException(nameof(target));
        this.properties = properties ?? throw new ArgumentNullException(nameof(properties));
    }
}
