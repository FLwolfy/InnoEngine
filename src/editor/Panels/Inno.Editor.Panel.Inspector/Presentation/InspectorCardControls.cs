using System;

using Inno.Core.Identity;
using Inno.Core.Logging;
using Inno.Editor.Interactions;
using Inno.Editor.ImGui;
using Inno.Editor.ImGui.ImGuiWidget;
using EditorWidget = Inno.Editor.ImGui.ImGuiWidget.ImGuiWidget;
using Inno.Engine.Scene;
using Inno.Platform.ImGui;
using NativeImGui = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.Panel.Inspector;

internal sealed class InspectorCardControls
{
    private const int C_CONTROL_COUNT = 3;

    internal float width => EditorWidget.GetIconButtonSize().X * C_CONTROL_COUNT;

    internal void DrawComponent(
        EditorInteractions interactions,
        GameObject owner,
        GameComponent component,
        int componentIndex,
        int componentCount,
        Action requestRemove)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(component);
        Guid ownerId = owner.identity.persistentId;
        Guid componentId = component.identity.persistentId;
        Draw(
            interactions,
            componentId,
            componentIndex > 1,
            componentIndex < componentCount - 1,
            targetIndex => ResolveGameObject(ownerId).SetComponentIndex(
                ResolveComponent(componentId),
                targetIndex),
            componentIndex,
            requestRemove,
            "Component");
    }

    internal void DrawSystem(
        EditorInteractions interactions,
        GameScene owner,
        GameSystem system,
        int systemIndex,
        int systemCount,
        Action requestRemove)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(system);
        Guid sceneId = owner.identity.persistentId;
        Guid systemId = system.identity.persistentId;
        Draw(
            interactions,
            systemId,
            systemIndex > 0,
            systemIndex < systemCount - 1,
            targetIndex => ResolveScene(sceneId).SetSystemIndex(
                ResolveSystem(systemId),
                targetIndex),
            systemIndex,
            requestRemove,
            "System");
    }

    private static void Draw(
        EditorInteractions interactions,
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
            interactions,
            targetId,
            canMoveUp,
            currentIndex,
            currentIndex - 1,
            move,
            ImGuiIcon.ArrowUp,
            $"Move {targetKind} Up",
            targetKind);
        NativeImGui.SameLine(0f, 0f);
        DrawMoveButton(
            interactions,
            targetId,
            canMoveDown,
            currentIndex,
            currentIndex + 1,
            move,
            ImGuiIcon.ArrowDown,
            $"Move {targetKind} Down",
            targetKind);
        NativeImGui.SameLine(0f, 0f);
        if (EditorWidget.IconButton(
                $"remove_{targetKind}_{targetId:N}",
                ImGuiIcon.Xmark,
                $"Remove {targetKind}"))
        {
            requestRemove();
        }
    }

    private static void DrawMoveButton(
        EditorInteractions interactions,
        Guid targetId,
        bool canMove,
        int currentIndex,
        int targetIndex,
        Action<int> move,
        string icon,
        string tooltip,
        string targetKind)
    {
        NativeImGui.BeginDisabled(!canMove);
        bool pressed = EditorWidget.IconButton(
            $"move_{targetKind}_{targetId:N}_{targetIndex}",
            icon,
            tooltip);
        NativeImGui.EndDisabled();
        if (!pressed)
            return;

        try
        {
            move(targetIndex);
            interactions.history.RecordValue(
                $"Move {targetKind}",
                currentIndex,
                targetIndex,
                move,
                $"{targetKind}-order:{targetId:N}");
        }
        catch (InvalidOperationException exception)
        {
            Log.Warn("{0} reorder was rejected: {1}", targetKind, exception.Message);
        }
    }

    private static GameObject ResolveGameObject(Guid persistentId)
        => IdentityManager.Get<GameObject>(persistentId)
           ?? throw new InvalidOperationException($"GameObject '{persistentId}' is no longer available.");

    private static GameComponent ResolveComponent(Guid persistentId)
        => IdentityManager.Get<GameComponent>(persistentId)
           ?? throw new InvalidOperationException($"Component '{persistentId}' is no longer available.");

    private static GameScene ResolveScene(Guid persistentId)
        => IdentityManager.Get<GameScene>(persistentId)
           ?? throw new InvalidOperationException($"Scene '{persistentId}' is no longer available.");

    private static GameSystem ResolveSystem(Guid persistentId)
        => IdentityManager.Get<GameSystem>(persistentId)
           ?? throw new InvalidOperationException($"System '{persistentId}' is no longer available.");
}
