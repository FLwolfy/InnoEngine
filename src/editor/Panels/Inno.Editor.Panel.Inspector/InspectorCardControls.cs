using System;

using Inno.Core.Logging;
using Inno.Editor.ImGui;
using Inno.Editor.ImGui.Widgets;
using Inno.Engine.Scene;
using Inno.Platform.ImGui;
using NativeImGui = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.Panel.Inspector;

internal sealed class InspectorCardControls
{
    private const int C_CONTROL_COUNT = 3;

    internal float width => ImGuiWidget.GetIconButtonSize().X * C_CONTROL_COUNT;

    internal void DrawComponent(
        GameObject owner,
        GameComponent component,
        int componentIndex,
        int componentCount,
        Action requestRemove)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(component);
        Draw(
            component.identity.persistentId,
            componentIndex > 1,
            componentIndex < componentCount - 1,
            targetIndex => owner.SetComponentIndex(component, targetIndex),
            componentIndex,
            requestRemove,
            "Component");
    }

    internal void DrawSystem(
        GameScene owner,
        GameSystem system,
        int systemIndex,
        int systemCount,
        Action requestRemove)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(system);
        Draw(
            system.identity.persistentId,
            systemIndex > 0,
            systemIndex < systemCount - 1,
            targetIndex => owner.SetSystemIndex(system, targetIndex),
            systemIndex,
            requestRemove,
            "System");
    }

    private static void Draw(
        Guid targetId,
        bool canMoveUp,
        bool canMoveDown,
        Action<int> move,
        int currentIndex,
        Action requestRemove,
        string targetKind)
    {
        ArgumentNullException.ThrowIfNull(move);
        ArgumentNullException.ThrowIfNull(requestRemove);
        DrawMoveButton(
            targetId,
            canMoveUp,
            currentIndex - 1,
            move,
            ImGuiIcon.ArrowUp,
            $"Move {targetKind} Up",
            targetKind);
        NativeImGui.SameLine(0f, 0f);
        DrawMoveButton(
            targetId,
            canMoveDown,
            currentIndex + 1,
            move,
            ImGuiIcon.ArrowDown,
            $"Move {targetKind} Down",
            targetKind);
        NativeImGui.SameLine(0f, 0f);
        if (ImGuiWidget.IconButton(
                $"remove_{targetKind}_{targetId:N}",
                ImGuiIcon.Xmark,
                $"Remove {targetKind}"))
        {
            requestRemove();
        }
    }

    private static void DrawMoveButton(
        Guid targetId,
        bool canMove,
        int targetIndex,
        Action<int> move,
        string icon,
        string tooltip,
        string targetKind)
    {
        NativeImGui.BeginDisabled(!canMove);
        bool pressed = ImGuiWidget.IconButton(
            $"move_{targetKind}_{targetId:N}_{targetIndex}",
            icon,
            tooltip);
        NativeImGui.EndDisabled();
        if (!pressed)
            return;

        try
        {
            move(targetIndex);
        }
        catch (InvalidOperationException exception)
        {
            Log.Warn("{0} reorder was rejected: {1}", targetKind, exception.Message);
        }
    }
}
