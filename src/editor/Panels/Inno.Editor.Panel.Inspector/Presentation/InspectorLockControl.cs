using System;
using System.Numerics;

using Inno.Editor.ImGui.ImGuiWidget;
using EditorWidget = Inno.Editor.ImGui.ImGuiWidget.ImGuiWidget;
using Inno.Engine.Scene;
using Inno.Platform.ImGui;
using NativeImGui = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.Panel.Inspector;

/// <summary>
/// Owns the transient target retained by the Inspector lock control.
/// </summary>
internal sealed class InspectorLockControl
{
    private object? m_lockedTarget;
    private bool m_isLocked;

    /// <summary>
    /// Draws the lock control and resolves the target that the Inspector should present this frame.
    /// </summary>
    /// <param name="selectedTarget">The current global editor selection.</param>
    /// <returns>
    /// The retained target while locked; otherwise, the current valid global selection.
    /// </returns>
    internal object? DrawAndResolve(object? selectedTarget)
    {
        if (m_isLocked && !IsValid(m_lockedTarget))
        {
            m_isLocked = false;
            m_lockedTarget = null;
        }

        DrawToggle(selectedTarget);
        object? target = m_isLocked ? m_lockedTarget : selectedTarget;
        return IsValid(target) ? target : null;
    }

    private void DrawToggle(object? selectedTarget)
    {
        Vector2 origin = NativeImGui.GetCursorScreenPos();
        Vector2 controlSize = EditorWidget.GetCompactClickableTextSize();
        float right = NativeImGui.GetWindowPos().X
            + NativeImGui.GetWindowSize().X
            - NativeImGui.GetStyle().WindowPadding.X
            - controlSize.X;
        NativeImGui.SetCursorScreenPos(new Vector2(MathF.Max(origin.X, right), origin.Y));
        string icon = m_isLocked ? ImGuiIcon.Lock : ImGuiIcon.LockOpen;
        string tooltip = m_isLocked ? "Unlock Inspector" : "Lock Inspector";
        if (EditorWidget.ClickableText("inspector_target_lock", icon, controlSize, tooltip))
        {
            m_isLocked = !m_isLocked;
            m_lockedTarget = m_isLocked && IsValid(selectedTarget)
                ? selectedTarget
                : null;
        }

        float nextY = MathF.Max(origin.Y + controlSize.Y, NativeImGui.GetCursorScreenPos().Y);
        NativeImGui.SetCursorScreenPos(new Vector2(origin.X, nextY));
    }

    private static bool IsValid(object? target)
        => target is not null && target is not EngineObject { isDestroyed: true };
}
