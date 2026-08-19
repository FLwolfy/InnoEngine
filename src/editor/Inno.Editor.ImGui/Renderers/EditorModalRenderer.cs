using System;
using System.Numerics;

using Inno.Native.ImGui;
using NativeImGui = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.ImGui.Renderers;

/// <summary>Renders centered editor modals consistently on the main viewport.</summary>
public static class EditorModalRenderer
{
    /// <summary>Draws a centered modal with a caller-provided opacity.</summary>
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

    /// <summary>Closes a modal popup if it is currently open.</summary>
    public static void Close(string id, string title)
    {
        string popupId = $"{title}##{id}";
        if (!NativeImGui.IsPopupOpen(popupId) || !NativeImGui.BeginPopupModal(popupId))
            return;
        NativeImGui.CloseCurrentPopup();
        NativeImGui.EndPopup();
    }
}
