using System;

using Inno.Editor.Interactions;
using Inno.Editor.Interactions.DragDrop;
using Inno.Editor.ImGui.Widgets;

namespace Inno.Editor.ImGui.Renderers;

/// <summary>Bridges managed editor drag sessions to the native ImGui payload API.</summary>
public static class EditorDragDropRenderer
{
    private const string C_EDITOR_PAYLOAD = "INNO_EDITOR_INTERACTION";

    /// <summary>
    /// Publishes managed drag data for the most recently submitted ImGui item.
    /// </summary>
    /// <param name="interaction">The interaction area and target that produced the drag source.</param>
    /// <param name="data">The managed drag data published by the source.</param>
    /// <param name="drawPreview">An optional callback that draws the native drag preview.</param>
    /// <returns><see langword="true"/> while the item is an active drag source.</returns>
    public static bool Source(
        EditorInteraction interaction,
        EditorDragData data,
        Action? drawPreview = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        return ImGuiWidget.DragDropSource(
            C_EDITOR_PAYLOAD,
            () => interaction.BeginDrag(data),
            drawPreview);
    }

    /// <summary>
    /// Evaluates and, on delivery, accepts a managed drag on the most recently submitted ImGui item.
    /// </summary>
    /// <param name="interaction">The interaction area and managed drop target.</param>
    /// <param name="placement">The requested position relative to the target.</param>
    /// <returns>The native preview state together with the managed compatibility and delivery results.</returns>
    public static EditorDropWidgetResult Target(
        EditorInteraction interaction,
        EditorDropPlacement placement = EditorDropPlacement.None)
    {
        bool delivered = ImGuiWidget.DragDropTarget(
            C_EDITOR_PAYLOAD,
            out Guid token,
            out bool isPreviewing,
            drawDefaultHighlight: false);
        if (!isPreviewing && !delivered)
            return EditorDropWidgetResult.none;
        EditorDropStatus status = interaction.QueryDrop(token, placement);
        EditorDropResult result = delivered && status.canDrop
            ? interaction.Drop(token, placement)
            : EditorDropResult.rejected;
        return new EditorDropWidgetResult(isPreviewing, status, result);
    }
}

/// <summary>Reports the preview and delivery state of an ImGui editor drop target.</summary>
public readonly record struct EditorDropWidgetResult
{
    /// <summary>
    /// Creates a combined native-preview and managed-drop result.
    /// </summary>
    /// <param name="isPreviewing">Whether a compatible native payload is currently hovering the target.</param>
    /// <param name="status">The compatibility and visual state returned by the managed drop router.</param>
    /// <param name="result">The result produced when the payload was delivered.</param>
    public EditorDropWidgetResult(
        bool isPreviewing,
        EditorDropStatus status,
        EditorDropResult result)
    {
        this.isPreviewing = isPreviewing;
        this.status = status;
        this.result = result;
    }

    /// <summary>Gets whether a compatible native payload is hovering the target.</summary>
    public bool isPreviewing { get; }

    /// <summary>Gets the managed drop compatibility status.</summary>
    public EditorDropStatus status { get; }

    /// <summary>Gets the delivered drop result.</summary>
    public EditorDropResult result { get; }

    /// <summary>Gets an inactive drop target result.</summary>
    public static EditorDropWidgetResult none => new(false, EditorDropStatus.rejected, EditorDropResult.rejected);
}
