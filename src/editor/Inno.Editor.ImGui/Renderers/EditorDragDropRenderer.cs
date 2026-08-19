using System;

using Inno.Editor.Core;
using Inno.Editor.Core.DragDrop;

namespace Inno.Editor.ImGui;

/// <summary>Bridges managed editor drag sessions to the native ImGui payload API.</summary>
public static class EditorDragDropRenderer
{
    private const string C_EDITOR_PAYLOAD = "INNO_EDITOR_INTERACTION";

    /// <summary>Publishes managed drag data for the most recently submitted item.</summary>
    public static bool Source(EditorDragContext context, Action? drawPreview = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        return ImGuiWidget.DragDropSource(
            C_EDITOR_PAYLOAD,
            () => context.editorContext.BeginDrag(context),
            drawPreview);
    }

    /// <summary>Evaluates and accepts a managed drag on the most recently submitted item.</summary>
    public static EditorDropWidgetResult Target(
        EditorContext editorContext,
        Type surface,
        object target,
        EditorDropPlacement placement = EditorDropPlacement.None)
    {
        ArgumentNullException.ThrowIfNull(editorContext);
        ArgumentNullException.ThrowIfNull(target);
        bool delivered = ImGuiWidget.DragDropTarget(
            C_EDITOR_PAYLOAD,
            out Guid token,
            out bool isPreviewing,
            drawDefaultHighlight: false);
        if (!isPreviewing && !delivered)
            return EditorDropWidgetResult.none;
        if (!editorContext.TryGetDragData(token, out EditorDragData? data) || data is null)
            return EditorDropWidgetResult.none;

        var context = new EditorDropContext(editorContext, surface, data, target, placement);
        EditorDropStatus status = editorContext.QueryDrop(token, context);
        EditorDropResult result = delivered && status.canDrop
            ? editorContext.Drop(token, context)
            : EditorDropResult.rejected;
        return new EditorDropWidgetResult(isPreviewing, status, result);
    }
}

/// <summary>Reports the preview and delivery state of an ImGui editor drop target.</summary>
public readonly record struct EditorDropWidgetResult
{
    /// <summary>Creates a drag-and-drop widget result.</summary>
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
