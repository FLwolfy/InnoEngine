using System;
using System.Collections.Generic;

using Inno.Editor.Utility;

using ImGuiNET;
using ImGuizmoNET;
using ImGuiNet = ImGuiNET.ImGui;

namespace Inno.Editor.Core;

public static class EditorManager
{
    private static readonly Dictionary<string, EditorPanel> PANELS = new();

    // =========================
    // Editor runtime (Play/Pause)
    // =========================
    public static EditorMode mode { get; private set; } = EditorMode.Edit;

    /// <summary>
    /// Fired when editor mode changes.
    /// </summary>
    public static event Action<EditorMode, EditorMode>? ModeChanged;

    // Manager Properties
    public static EditorSelection selection { get; } = new();

    internal static void SetMode(EditorMode newMode)
    {
        if (mode == newMode) return;
        var prev = mode;
        mode = newMode;
        ModeChanged?.Invoke(prev, newMode);
    }

    public static void RegisterPanel(EditorPanel panel)
    {
        if (!PANELS.TryAdd(panel.title, panel))
            throw new Exception("Panel already registered");
    }
    
    internal static void DrawPanels()
    {
        foreach (var panel in PANELS.Values)
        {
            if (!panel.isOpen) continue;

            ImGuiNet.Begin(panel.title, ImGuiWindowFlags.NoCollapse);
            ImGuizmo.SetDrawlist();
            panel.OnGUI();
            ImGuiNet.End();
        }
    }
}