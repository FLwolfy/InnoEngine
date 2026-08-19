using Inno.Editor.Scene;

using System;
using System.Collections.Generic;
using System.Numerics;

using Inno.Core.Serialization;
using Inno.Editor.Core;
using Inno.Editor.Core.Commands;
using Inno.Editor.Core.Menus;
using Inno.Editor.ImGui;
using Inno.Engine.Scene;
using Inno.Engine.Scene.Components;
using Inno.Native.ImGui;
using Inno.Platform.ImGui;
using NativeImGui = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.Scene.Inspection;

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
            context.editorContext.selection.Clear();
            NativeImGui.TextUnformatted("Selected GameObject is not available in the active scene.");
            return;
        }

        NativeImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, ImGuiWidget.style.compactItemSpacing);
        NativeImGui.PushStyleVar(ImGuiStyleVar.FramePadding, ImGuiWidget.style.compactFramePadding);
        try
        {
            DrawObjectHeader(gameObject);
            NativeImGui.Spacing();
            DrawComponents(context, gameObject);
            DrawAddComponent(context, gameObject);
        }
        finally
        {
            NativeImGui.PopStyleVar(2);
        }
    }

    private static void DrawObjectHeader(GameObject gameObject)
    {
        string name = gameObject.name;
        NativeImGui.SetNextItemWidth(-1f);
        if (NativeImGui.InputText(
                $"##name_{gameObject.identity.persistentId:N}",
                ref name,
                C_NAME_BUFFER_SIZE,
                ImGuiInputTextFlags.None))
        {
            gameObject.name = name;
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
            bool open = ImGuiWidget.CollapsingCard(
                componentId,
                componentType.Name,
                behavior is not null
                    ? () =>
                    {
                        bool enabled = behavior.enabled;
                        if (ImGuiWidget.CompactCheckbox($"enabled_{componentId}", ref enabled))
                        {
                            behavior.enabled = enabled;
                        }
                    }
                    : null,
                (Action?)(componentType == typeof(Transform)
                    ? null
                    : () => m_cardControls.DrawComponent(
                        gameObject,
                        component,
                        i,
                        components.Count,
                        () => context.editorContext.Enqueue(
                            EditorActionIds.Remove,
                            typeof(SceneSurface.Component),
                            new ComponentEditorTarget(gameObject, component)))),
                dimmed: behavior is { enabled: false },
                trailingControlWidth: componentType == typeof(Transform)
                    ? 0f
                    : m_cardControls.width);

            _ = EditorMenuRenderer.ContextMenu(
                $"##component_menu_{componentId}",
                new EditorMenuContext(
                    context.editorContext,
                    typeof(SceneSurface.Component),
                    new ComponentEditorTarget(gameObject, component)));

            if (!open)
            {
                NativeImGui.Dummy(new Vector2(0f, ImGuiWidget.style.inspectorCardSpacing));
                continue;
            }

            NativeImGui.Unindent();
            ImGuiWidget.CardBody(
                componentId,
                () =>
                {
                    IReadOnlyList<SerializedProperty> properties = SerializationManager.GetProperties(component);
                    for (int propertyIndex = 0; propertyIndex < properties.Count; propertyIndex++)
                    {
                        context.properties.Draw(
                            context.editorContext,
                            $"gameObject.{gameObject.identity.persistentId:N}.{componentId}",
                            properties[propertyIndex]);
                    }
                },
                dimmed: behavior is { enabled: false });

            NativeImGui.Indent();
            NativeImGui.TreePop();
            NativeImGui.Dummy(new Vector2(0f, ImGuiWidget.style.inspectorCardSpacing));
        }

    }

    private void DrawAddComponent(InspectorDrawContext context, GameObject gameObject)
    {
        if (ImGuiWidget.CenteredButton(
                "Add Component",
                ImGuiWidget.style.inspectorAddButtonTopPadding))
        {
            m_componentSearch = string.Empty;
            NativeImGui.OpenPopup("##add_component_popup");
        }

        if (!ImGuiWidget.BeginSearchPopup(
                "##add_component_popup",
                ref m_componentSearch,
                "Search components...",
                C_SEARCH_BUFFER_SIZE))
        {
            return;
        }

        var menuContext = new EditorMenuContext(
            context.editorContext,
            typeof(SceneSurface.AddComponent),
            gameObject);
        if (EditorMenuRenderer.DrawSearchItems(
                menuContext,
                context.editorContext.BuildMenu(menuContext.surface, menuContext.target).items,
                m_componentSearch))
            NativeImGui.CloseCurrentPopup();

        ImGuiWidget.EndSearchPopup();
    }
}
