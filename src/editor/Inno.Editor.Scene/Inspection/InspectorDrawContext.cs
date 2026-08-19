using System;

using Inno.Editor.Core;

namespace Inno.Editor.Scene.Inspection;

/// <summary>
/// Provides services and state to an inspector drawer.
/// </summary>
public sealed class InspectorDrawContext
{
    /// <summary>
    /// Gets the shared editor context.
    /// </summary>
    public EditorContext editorContext { get; }

    /// <summary>
    /// Gets the selected target.
    /// </summary>
    public object target { get; }

    /// <summary>
    /// Gets the serialized property renderer.
    /// </summary>
    public SerializedPropertyRenderer properties { get; }

    internal InspectorDrawContext(
        EditorContext editorContext,
        object target,
        SerializedPropertyRenderer properties)
    {
        this.editorContext = editorContext ?? throw new ArgumentNullException(nameof(editorContext));
        this.target = target ?? throw new ArgumentNullException(nameof(target));
        this.properties = properties ?? throw new ArgumentNullException(nameof(properties));
    }
}
