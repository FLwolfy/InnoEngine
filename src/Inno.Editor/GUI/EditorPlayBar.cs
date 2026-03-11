using System;

using Inno.Editor.Core;

using ImGuiNET;
using ImGuiNet = ImGuiNET.ImGui;

namespace Inno.Editor.GUI;

/// <summary>
/// Minimal Play/Pause/Stop overlay.
///
/// Rendered as a small undecorated window centered at the top of the main viewport.
/// </summary>
internal static class EditorPlayBar
{
    private const float C_HEIGHT = 28f;

    public static void Draw()
    {
        var vp = ImGuiNet.GetMainViewport();

        ImGuiNet.SetNextWindowPos(new System.Numerics.Vector2(vp.Pos.X + vp.Size.X * 0.5f, vp.Pos.Y + 6f), ImGuiCond.Always, new System.Numerics.Vector2(0.5f, 0f));
        ImGuiNet.SetNextWindowSize(new System.Numerics.Vector2(0, C_HEIGHT), ImGuiCond.Always);

        var flags = ImGuiWindowFlags.NoDecoration
                    | ImGuiWindowFlags.NoDocking
                    | ImGuiWindowFlags.AlwaysAutoResize
                    | ImGuiWindowFlags.NoSavedSettings
                    | ImGuiWindowFlags.NoFocusOnAppearing
                    | ImGuiWindowFlags.NoNav;

        ImGuiNet.PushStyleVar(ImGuiStyleVar.WindowPadding, new System.Numerics.Vector2(10f, 6f));
        ImGuiNet.PushStyleVar(ImGuiStyleVar.WindowRounding, 8f);
        ImGuiNet.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 1f);

        if (ImGuiNet.Begin("##EditorPlayBar", flags))
        {
            DrawButtons();
        }
        ImGuiNet.End();

        ImGuiNet.PopStyleVar(3);
    }

    private static void DrawButtons()
    {
        // Run toggles Play <-> Stop (Unity-style)
        var mode = EditorManager.mode;
        bool inEdit = mode == EditorMode.Edit;
        bool inPlay = mode == EditorMode.Play;
        bool inPause = mode == EditorMode.Pause;

        string runLabel = inEdit ? "Run" : "Stop";
        if (ImGuiNet.Button(runLabel))
        {
            if (inEdit) EditorRuntimeController.Play();
            else EditorRuntimeController.Stop();
        }

        ImGuiNet.SameLine();

        using (new DisabledScope(!inEdit))
        {
            string pauseLabel = inPause ? "Resume" : "Pause";
            if (ImGuiNet.Button(pauseLabel))
            {
                if (inPlay) EditorRuntimeController.Pause();
                else if (inPause) EditorRuntimeController.Resume();
            }
        }
    }

    private readonly struct DisabledScope : IDisposable
    {
        private readonly bool m_enabled;

        public DisabledScope(bool enabled)
        {
            m_enabled = enabled;
            if (!enabled) ImGuiNet.BeginDisabled();
        }

        public void Dispose()
        {
            if (!m_enabled) ImGuiNet.EndDisabled();
        }
    }
}
