using System;
using System.Numerics;

using Inno.Editor.ImGui.Widgets;
using Inno.Native.ImGui;
using NativeImGui = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.ImGui.Renderers;

/// <summary>Renders centered editor modals consistently on the main viewport.</summary>
public static class EditorModalRenderer
{
    /// <summary>
    /// Draws a fixed-width modal centered in the main viewport work area with a caller-provided opacity.
    /// </summary>
    /// <param name="id">The stable popup identity independent of the visible title.</param>
    /// <param name="title">The visible modal title.</param>
    /// <param name="alpha">The opacity applied to the complete modal window.</param>
    /// <param name="content">The callback that draws the modal body.</param>
    public static void Draw(
        string id,
        string title,
        float alpha,
        Action content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentNullException.ThrowIfNull(content);

        string popupId = $"{title}##{id}";
        ImGuiViewportPtr viewport = NativeImGui.GetMainViewport();
        Vector2 center = viewport.WorkPos + viewport.WorkSize * 0.5f;
        NativeImGui.OpenPopup(popupId, ImGuiPopupFlags.NoReopen);
        NativeImGui.SetNextWindowViewport(viewport.ID);
        NativeImGui.SetNextWindowPos(center, ImGuiCond.Always, new Vector2(0.5f, 0.5f));
        NativeImGui.SetNextWindowSize(
            new Vector2(ImGuiWidget.style.modalWidth, 0f),
            ImGuiCond.Always);
        NativeImGui.PushStyleVar(ImGuiStyleVar.Alpha, alpha);
        ImGuiWindowFlags flags = ImGuiWindowFlags.AlwaysAutoResize |
                                 ImGuiWindowFlags.NoMove |
                                 ImGuiWindowFlags.NoResize;
        if (NativeImGui.BeginPopupModal(popupId, flags))
        {
            content();
            NativeImGui.EndPopup();
        }
        NativeImGui.PopStyleVar();
    }

    /// <summary>
    /// Closes a modal popup if it is currently open.
    /// </summary>
    /// <param name="id">The stable popup identity used when the modal was opened.</param>
    /// <param name="title">The visible title used when the modal was opened.</param>
    public static void Close(string id, string title)
    {
        string popupId = $"{title}##{id}";
        if (!NativeImGui.IsPopupOpen(popupId) || !NativeImGui.BeginPopupModal(popupId))
            return;
        NativeImGui.CloseCurrentPopup();
        NativeImGui.EndPopup();
    }
}
