
using System;
using System.Collections.Generic;
using System.Numerics;

using Inno.Core.Serialization;
using Inno.Editor.Core;
using Inno.Editor.Interactions;
using Inno.Editor.ImGui;
using Inno.Editor.ImGui.ImGuiWidget;
using EditorWidget = Inno.Editor.ImGui.ImGuiWidget.ImGuiWidget;
using Inno.Engine.Scene;
using Inno.Engine.Scene.Components;
using Inno.Native.ImGui;
using Inno.Platform.ImGui;
using NativeImGui = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.Panel.Inspector;

[InspectorDrawer(typeof(GameObject))]
internal sealed class GameObjectInspectorDrawer : IInspectorDrawer
{
    private const nuint C_NAME_BUFFER_SIZE = 512;
    private const nuint C_SEARCH_BUFFER_SIZE = 256;

    private readonly InspectorCardControls m_cardControls = new();
    private string m_componentSearch = string.Empty;

    /// <inheritdoc />
    public void Draw(InspectorDrawContext context)
    {
        var gameObject = (GameObject)context.target;
        if (!gameObject.isRuntimeValid || !gameObject.scene.isLoaded)
        {
            _ = context.interactions.For(context.interactions.focusedArea).Select();
            NativeImGui.TextUnformatted("Selected GameObject is not available in the active scene.");
            return;
        }

        NativeImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, EditorWidget.style.compactItemSpacing);
        NativeImGui.PushStyleVar(ImGuiStyleVar.FramePadding, EditorWidget.style.compactFramePadding);
        try
        {
            DrawObjectHeader(context, gameObject);
            NativeImGui.Spacing();
            DrawComponents(context, gameObject);
            DrawAddComponent(context, gameObject);
        }
        finally
        {
            NativeImGui.PopStyleVar(2);
        }
    }

    private static void DrawObjectHeader(InspectorDrawContext context, GameObject gameObject)
    {
        string name = gameObject.name;
        NativeImGui.SetNextItemWidth(-1f);
        if (NativeImGui.InputText(
                $"##name_{gameObject.identity.persistentId:N}",
                ref name,
                C_NAME_BUFFER_SIZE,
                ImGuiInputTextFlags.None))
        {
            context.edits.RenameGameObject(gameObject, name);
        }
    }

    private void DrawComponents(InspectorDrawContext context, GameObject gameObject)
    {
        IReadOnlyList<GameComponent> components = gameObject.GetComponents();
        for (int i = 0; i < components.Count; i++)
        {
            GameComponent component = components[i];
            Type componentType = component.GetType();
            string componentId = component.identity.persistentId.ToString("N");
            GameBehavior? behavior = component as GameBehavior;
            var editorTarget = new ComponentEditorTarget(gameObject, component);
            bool open = EditorWidget.CollapsingCard(
                componentId,
                componentType.Name,
                behavior is not null
                    ? () =>
                    {
                        bool enabled = behavior.enabled;
                        if (EditorWidget.CompactCheckbox($"enabled_{componentId}", ref enabled))
                        {
                            _ = context.edits.ChangeProperty(
                                behavior,
                                "enabled",
                                () => behavior.enabled = enabled,
                                enabled ? "Enable Component" : "Disable Component",
                                mergeKey: null);
                        }
                    }
                    : null,
                (Action?)(componentType == typeof(Transform)
                    ? null
                    : () => m_cardControls.DrawComponent(
                        context.edits,
                        component,
                        i,
                        components.Count,
                        () => context.interactions
                            .For(
                                InspectorAreas.Component,
                                editorTarget)
                            .Enqueue(InspectorActions.RemoveComponent))),
                dimmed: behavior is { enabled: false },
                trailingControlWidth: componentType == typeof(Transform)
                    ? 0f
                    : m_cardControls.width,
                drawContextMenu: () => _ = EditorMenuRenderer.ContextMenu(
                    $"##component_menu_{componentId}",
                    context.interactions.For(InspectorAreas.Component, editorTarget)));

            if (!open)
            {
                NativeImGui.Dummy(new Vector2(0f, EditorWidget.style.inspectorCardSpacing));
                continue;
            }

            NativeImGui.Unindent();
            EditorWidget.CardBody(
                componentId,
                () =>
                {
                    IReadOnlyList<SerializedProperty> properties = SerializationManager.GetProperties(component);
                    for (int propertyIndex = 0; propertyIndex < properties.Count; propertyIndex++)
                    {
                        context.properties.Draw(
                            context.editorContext,
                            component,
                            $"gameObject.{gameObject.identity.persistentId:N}.{componentId}",
                            properties[propertyIndex]);
                    }
                },
                dimmed: behavior is { enabled: false });

            NativeImGui.Indent();
            NativeImGui.TreePop();
            NativeImGui.Dummy(new Vector2(0f, EditorWidget.style.inspectorCardSpacing));
        }

    }

    private void DrawAddComponent(InspectorDrawContext context, GameObject gameObject)
    {
        if (EditorWidget.CenteredButton(
                "Add Component",
                EditorWidget.style.inspectorAddButtonTopPadding))
        {
            m_componentSearch = string.Empty;
            NativeImGui.OpenPopup("##add_component_popup");
        }

        if (!EditorWidget.BeginSearchPopup(
                "##add_component_popup",
                ref m_componentSearch,
                "Search components...",
                C_SEARCH_BUFFER_SIZE))
        {
            return;
        }

        EditorInteraction interaction = context.interactions.For(InspectorAreas.Component, gameObject);
        if (EditorMenuRenderer.DrawSearchItems(
                interaction,
                interaction.BuildMenu().items,
                m_componentSearch))
            NativeImGui.CloseCurrentPopup();

        EditorWidget.EndSearchPopup();
    }

}
