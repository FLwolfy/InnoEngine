using System;
using System.Numerics;

using Inno.Adapter.Presentation.ImGui;
using Inno.Core.Identity;
using Inno.Core.Logging;
using Inno.Editor.ImGui;
using Inno.Editor.ImGui.ImGuiWidget;
using EditorWidget = Inno.Editor.ImGui.ImGuiWidget.ImGuiWidget;
using Inno.Editor.Interactions;
using Inno.Editor.Scene;
using Inno.Native.ImGui;
using Inno.Scene;
using Inno.Scene.Components;
using NativeImGui = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.Panel.Inspector;

internal sealed class InspectorCardControls
{
    private const string C_COMPONENT_PAYLOAD = "INNO_INSPECTOR_COMPONENT";
    private const string C_SYSTEM_PAYLOAD = "INNO_INSPECTOR_SYSTEM";

    private readonly Logger m_log;

    internal InspectorCardControls(LogRouter logs)
    {
        ArgumentNullException.ThrowIfNull(logs);
        m_log = logs.CreateLogger<InspectorCardControls>();
    }

    internal float GetWidth(bool canRemove)
        => EditorWidget.GetCompactClickableTextSize().X * (canRemove ? 2f : 1f);

    internal void DrawComponent(
        SceneEdits edits,
        GameComponent component,
        bool canRemove,
        Action requestRemove)
    {
        ArgumentNullException.ThrowIfNull(edits);
        ArgumentNullException.ThrowIfNull(component);
        ArgumentNullException.ThrowIfNull(requestRemove);
        Guid componentId = component.identity.persistentId;
        if (EditorWidget.ClickableText(
                $"reset_Component_{componentId:N}",
                ImGuiIcon.ArrowRotateLeft,
                "Reset Component"))
        {
            TryEdit(() => edits.ResetComponent(component), "Component reset");
        }
        if (!canRemove)
            return;

        NativeImGui.SameLine(0f, 0f);
        if (EditorWidget.ClickableText(
                $"remove_Component_{componentId:N}",
                ImGuiIcon.Xmark,
                "Remove Component"))
        {
            requestRemove();
        }
    }

    internal void DrawSystem(
        SceneEdits edits,
        GameScene owner,
        GameSystem system,
        Action requestRemove)
    {
        ArgumentNullException.ThrowIfNull(edits);
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(system);
        ArgumentNullException.ThrowIfNull(requestRemove);
        Guid systemId = system.identity.persistentId;
        if (EditorWidget.ClickableText(
                $"reset_System_{systemId:N}",
                ImGuiIcon.ArrowRotateLeft,
                "Reset System"))
        {
            TryEdit(() => edits.ResetSystem(owner, system), "System reset");
        }
        NativeImGui.SameLine(0f, 0f);
        if (EditorWidget.ClickableText(
                $"remove_System_{systemId:N}",
                ImGuiIcon.Xmark,
                "Remove System"))
        {
            requestRemove();
        }
    }

    internal void DrawComponentDragSource(
        GameComponent component,
        string title,
        bool dimmed)
    {
        ArgumentNullException.ThrowIfNull(component);
        ArgumentNullException.ThrowIfNull(title);
        if (component.identity.runtimeIdentity is not RuntimeIdentity identity)
            return;
        _ = EditorWidget.DragDropSource(
            C_COMPONENT_PAYLOAD,
            identity,
            () => DrawDragPreview(title, dimmed),
            allowHoldToOpenOthers: false);
    }

    internal void DrawSystemDragSource(GameSystem system, string title, bool dimmed)
    {
        ArgumentNullException.ThrowIfNull(system);
        ArgumentNullException.ThrowIfNull(title);
        if (system.identity.runtimeIdentity is not RuntimeIdentity identity)
            return;
        _ = EditorWidget.DragDropSource(
            C_SYSTEM_PAYLOAD,
            identity,
            () => DrawDragPreview(title, dimmed),
            allowHoldToOpenOthers: false);
    }

    internal void DrawComponentDropTarget(
        EditorInteractions interactions,
        SceneEdits edits,
        GameComponent target,
        int targetIndex,
        Vector2 cardMinimum,
        Vector2 cardMaximum)
    {
        ArgumentNullException.ThrowIfNull(interactions);
        ArgumentNullException.ThrowIfNull(edits);
        ArgumentNullException.ThrowIfNull(target);
        DrawDropTarget(
            C_COMPONENT_PAYLOAD,
            $"component_{target.identity.persistentId:N}",
            interactions,
            cardMinimum,
            cardMaximum,
            source => source is GameComponent component &&
                      ReferenceEquals(component.gameObject, target.gameObject) &&
                      !ReferenceEquals(component, target),
            (source, insertAfter) =>
            {
                var component = (GameComponent)source;
                int sourceIndex = component.gameObject.GetComponentIndex(component);
                int insertionBoundary = targetIndex + (insertAfter ? 1 : 0);
                int destinationIndex = insertionBoundary > sourceIndex
                    ? insertionBoundary - 1
                    : insertionBoundary;
                edits.SetComponentIndex(component, destinationIndex);
            },
            "Component reorder");
    }

    internal void DrawSystemDropTarget(
        EditorInteractions interactions,
        SceneEdits edits,
        GameScene scene,
        GameSystem target,
        int targetIndex,
        Vector2 cardMinimum,
        Vector2 cardMaximum)
    {
        ArgumentNullException.ThrowIfNull(interactions);
        ArgumentNullException.ThrowIfNull(edits);
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(target);
        DrawDropTarget(
            C_SYSTEM_PAYLOAD,
            $"system_{target.identity.persistentId:N}",
            interactions,
            cardMinimum,
            cardMaximum,
            source => source is GameSystem system &&
                      !ReferenceEquals(system, target) &&
                      IsOwnedBy(scene, system),
            (source, insertAfter) =>
            {
                var system = (GameSystem)source;
                int sourceIndex = scene.GetSystemIndex(system);
                int insertionBoundary = targetIndex + (insertAfter ? 1 : 0);
                int destinationIndex = insertionBoundary > sourceIndex
                    ? insertionBoundary - 1
                    : insertionBoundary;
                edits.SetSystemIndex(scene, system, destinationIndex);
            },
            "System reorder");
    }

    private void DrawDropTarget(
        string payloadType,
        string id,
        EditorInteractions interactions,
        Vector2 cardMinimum,
        Vector2 cardMaximum,
        Func<IdentityObject, bool> accepts,
        Action<IdentityObject, bool> move,
        string operation)
    {
        ImGuiPayloadPtr activePayload = NativeImGui.GetDragDropPayload();
        if (activePayload.IsNull ||
            !activePayload.IsDataType(payloadType) ||
            cardMaximum.X <= cardMinimum.X ||
            cardMaximum.Y <= cardMinimum.Y)
        {
            return;
        }

        uint targetId = NativeImGui.GetID($"##inspector_card_drop_{id}");
        bool delivered = EditorWidget.DragDropTarget(
            payloadType,
            cardMinimum,
            cardMaximum,
            targetId,
            out RuntimeIdentity identity,
            out bool isPreviewing,
            drawDefaultHighlight: false);
        if (!isPreviewing && !delivered)
            return;
        if (!interactions.TryResolveIdentity(identity, out IdentityObject? source) ||
            source is null ||
            !accepts(source))
        {
            return;
        }

        bool insertAfter = NativeImGui.GetMousePos().Y >= (cardMinimum.Y + cardMaximum.Y) * 0.5f;
        float markerY = insertAfter ? cardMaximum.Y : cardMinimum.Y;
        if (isPreviewing)
            EditorWidget.InsertionLine(cardMinimum.X, cardMaximum.X, markerY);
        if (delivered)
            TryEdit(() => move(source, insertAfter), operation);
    }

    private static void DrawDragPreview(string title, bool dimmed)
    {
        Vector2 origin = NativeImGui.GetCursorScreenPos();
        Vector2 padding = EditorWidget.style.inspectorCardHeaderPadding;
        Vector2 gripSize = NativeImGui.CalcTextSize(ImGuiIcon.GripVertical);
        Vector2 titleSize = NativeImGui.CalcTextSize(title);
        Vector2 size = new(
            padding.X * 2f + gripSize.X + EditorWidget.style.inspectorHeaderControlSpacing + titleSize.X,
            MathF.Max(gripSize.Y, titleSize.Y) + padding.Y * 2f);
        ImDrawListPtr draw = NativeImGui.GetWindowDrawList();
        draw.AddRectFilled(
            origin,
            origin + size,
            NativeImGui.ColorConvertFloat4ToU32(EditorPalette.inspectorCardHeader),
            EditorWidget.style.frameRounding);
        uint color = NativeImGui.ColorConvertFloat4ToU32(
            dimmed ? EditorPalette.inspectorCardDisabledText : EditorPalette.text);
        Vector2 textOrigin = origin + padding;
        draw.AddText(textOrigin, color, ImGuiIcon.GripVertical);
        draw.AddText(
            new Vector2(textOrigin.X + gripSize.X + EditorWidget.style.inspectorHeaderControlSpacing, textOrigin.Y),
            color,
            title);
        NativeImGui.Dummy(size);
    }

    private void TryEdit(Action edit, string operation)
    {
        try
        {
            edit();
        }
        catch (InvalidOperationException exception)
        {
            m_log.Write(LogLevel.Warn, "{0} was rejected: {1}", [operation, exception.Message]);
        }
    }

    private static bool IsOwnedBy(GameScene scene, GameSystem system)
    {
        try
        {
            _ = scene.GetSystemIndex(system);
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }
}
