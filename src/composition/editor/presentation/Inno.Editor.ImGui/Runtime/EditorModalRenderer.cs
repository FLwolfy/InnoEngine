using System;
using System.Numerics;

using Inno.Editor.Core;
using Inno.Editor.ImGui.ImGuiWidget;
using Inno.Editor.Interactions;
using EditorWidget = Inno.Editor.ImGui.ImGuiWidget.ImGuiWidget;
using Inno.Native.ImGui;
using NativeImGui = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.ImGui;

/// <summary>
/// Renders centered editor modals consistently on the main viewport.
/// </summary>
internal static class EditorModalRenderer
{
    /// <summary>
    /// Draws a modal in the main viewport work area with a caller-provided opacity.
    /// </summary>
    /// <param name="id">
    /// The stable popup identity independent of the visible title.
    /// </param>
    /// <param name="title">
    /// The visible modal title.
    /// </param>
    /// <param name="alpha">
    /// The opacity applied to the complete modal window.
    /// </param>
    /// <param name="modal">
    /// The modal content and window presentation policy.
    /// </param>
    /// <param name="presentation">
    /// The generation-safe window presentation values.
    /// </param>
    /// <param name="context">
    /// The shared editor context supplied to the modal body.
    /// </param>
    internal static void Draw(
        string id,
        string title,
        float alpha,
        EditorModalExtension modal,
        EditorModalExtension.Presentation presentation,
        EditorContext context)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentNullException.ThrowIfNull(modal);
        ArgumentNullException.ThrowIfNull(context);

        string popupId = $"{title}##{id}";
        ImGuiViewportPtr viewport = NativeImGui.GetMainViewport();
        Vector2 center = viewport.WorkPos + viewport.WorkSize * 0.5f;
        if (presentation.blocksInteraction)
            NativeImGui.OpenPopup(popupId, ImGuiPopupFlags.NoReopen);
        NativeImGui.SetNextWindowViewport(viewport.ID);
        ImGuiCond placementCondition = presentation.canMove || presentation.canResize
            ? ImGuiCond.Appearing
            : ImGuiCond.Always;
        NativeImGui.SetNextWindowPos(center, placementCondition, new Vector2(0.5f, 0.5f));
        Vector2 initialSize = presentation.initialSize;
        if (initialSize.X > 0f && initialSize.Y > 0f)
        {
            NativeImGui.SetNextWindowSize(
                initialSize * EditorWidget.style.zoom,
                placementCondition);
        }
        else if (!presentation.canResize)
        {
            NativeImGui.SetNextWindowSize(
                new Vector2(EditorWidget.style.modalWidth, 0f),
                ImGuiCond.Always);
        }
        if (presentation.canResize)
        {
            Vector2 maximumSize = Vector2.Max(Vector2.One, viewport.WorkSize);
            Vector2 minimumSize = Vector2.Min(
                presentation.minimumSize * EditorWidget.style.zoom,
                maximumSize);
            NativeImGui.SetNextWindowSizeConstraints(minimumSize, maximumSize);
        }
        NativeImGui.PushStyleVar(ImGuiStyleVar.Alpha, alpha);
        ImGuiWindowFlags flags = ImGuiWindowFlags.NoDocking | ImGuiWindowFlags.NoCollapse;
        if (!presentation.canMove)
            flags |= ImGuiWindowFlags.NoMove;
        if (!presentation.canResize)
            flags |= ImGuiWindowFlags.NoResize | ImGuiWindowFlags.AlwaysAutoResize;
        bool beganWindow = false;
        try
        {
            bool visible = presentation.blocksInteraction
                ? NativeImGui.BeginPopupModal(popupId, flags)
                : NativeImGui.Begin(popupId, flags | ImGuiWindowFlags.NoSavedSettings);
            beganWindow = !presentation.blocksInteraction || visible;
            if (visible)
            {
                _ = modal.Draw(context);
            }
        }
        finally
        {
            if (beganWindow)
            {
                if (presentation.blocksInteraction)
                    NativeImGui.EndPopup();
                else
                    NativeImGui.End();
            }
            NativeImGui.PopStyleVar();
        }
    }

    /// <summary>
    /// Closes a modal popup if it is currently open.
    /// </summary>
    /// <param name="id">
    /// The stable popup identity used when the modal was opened.
    /// </param>
    /// <param name="title">
    /// The visible title used when the modal was opened.
    /// </param>
    internal static void Close(string id, string title)
    {
        string popupId = $"{title}##{id}";
        if (!NativeImGui.IsPopupOpen(popupId) || !NativeImGui.BeginPopupModal(popupId))
            return;
        try
        {
            NativeImGui.CloseCurrentPopup();
        }
        finally
        {
            NativeImGui.EndPopup();
        }
    }
}
