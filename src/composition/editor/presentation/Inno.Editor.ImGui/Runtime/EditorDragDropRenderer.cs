using System;
using System.Globalization;

using Inno.Core.Identity;
using Inno.Editor.Interactions;
using Inno.Editor.ImGui.ImGuiWidget;
using EditorWidget = Inno.Editor.ImGui.ImGuiWidget.ImGuiWidget;

namespace Inno.Editor.ImGui;

/// <summary>
/// Bridges managed editor drag sessions to the native ImGui payload API.
/// </summary>
public static class EditorDragDropRenderer
{
    private const string C_IDENTITY_PAYLOAD_PREFIX = "INNO_ID_";

    /// <summary>
    /// Publishes managed drag data for the most recently submitted ImGui item.
    /// </summary>
    /// <param name="interaction">
    /// The interaction area and target that produced the drag source.
    /// </param>
    /// <param name="data">
    /// The managed drag data published by the source.
    /// </param>
    /// <param name="drawPreview">
    /// An optional callback that draws the native drag preview.
    /// </param>
    /// <returns>
    /// <see langword="true"/> while the item is an active drag source.
    /// </returns>
    public static bool Source(
        EditorInteraction interaction,
        EditorDragData data,
        Action? drawPreview = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        return EditorWidget.DragDropSource(
            GetPayloadType(data.sourceIdentity.domainId),
            () => interaction.BeginDrag(data).runtimeId,
            drawPreview);
    }

    /// <summary>
    /// Evaluates and, on delivery, accepts a managed drag on the most recently submitted ImGui item.
    /// </summary>
    /// <param name="interaction">
    /// The interaction area and managed drop target.
    /// </param>
    /// <param name="placement">
    /// The requested position relative to the target.
    /// </param>
    /// <returns>
    /// The native preview state together with the managed compatibility and delivery results.
    /// </returns>
    public static EditorDropWidgetResult Target(
        EditorInteraction interaction,
        EditorDropPlacement placement = EditorDropPlacement.None)
    {
        if (!interaction.TryGetActiveDragIdentity(out RuntimeIdentity activeIdentity))
            return EditorDropWidgetResult.none;
        bool delivered = EditorWidget.DragDropTarget(
            GetPayloadType(activeIdentity.domainId),
            out int runtimeId,
            out bool isPreviewing,
            drawDefaultHighlight: false);
        if (!isPreviewing && !delivered)
            return EditorDropWidgetResult.none;
        if (runtimeId <= 0)
            return EditorDropWidgetResult.none;
        var identity = new RuntimeIdentity(activeIdentity.domainId, runtimeId);
        EditorDropStatus status = interaction.QueryDrop(identity, placement);
        EditorDropResult result = delivered && status.canDrop
            ? interaction.Drop(identity, placement)
            : EditorDropResult.rejected;
        return new EditorDropWidgetResult(isPreviewing, status, result);
    }

    private static string GetPayloadType(IdentityDomainId domainId)
        => C_IDENTITY_PAYLOAD_PREFIX + domainId.value.ToString("X8", CultureInfo.InvariantCulture);
}

/// <summary>
/// Reports the preview and delivery state of an ImGui editor drop target.
/// </summary>
public readonly record struct EditorDropWidgetResult
{
    /// <summary>
    /// Creates a combined native-preview and managed-drop result.
    /// </summary>
    /// <param name="isPreviewing">
    /// Whether a compatible native payload is currently hovering the target.
    /// </param>
    /// <param name="status">
    /// The compatibility and visual state returned by the managed drop router.
    /// </param>
    /// <param name="result">
    /// The result produced when the payload was delivered.
    /// </param>
    public EditorDropWidgetResult(
        bool isPreviewing,
        EditorDropStatus status,
        EditorDropResult result)
    {
        this.isPreviewing = isPreviewing;
        this.status = status;
        this.result = result;
    }

    /// <summary>
    /// Gets whether a compatible native payload is hovering the target.
    /// </summary>
    public bool isPreviewing { get; }

    /// <summary>
    /// Gets the managed drop compatibility status.
    /// </summary>
    public EditorDropStatus status { get; }

    /// <summary>
    /// Gets the delivered drop result.
    /// </summary>
    public EditorDropResult result { get; }

    /// <summary>
    /// Gets an inactive drop target result.
    /// </summary>
    public static EditorDropWidgetResult none => new(false, EditorDropStatus.rejected, EditorDropResult.rejected);
}
