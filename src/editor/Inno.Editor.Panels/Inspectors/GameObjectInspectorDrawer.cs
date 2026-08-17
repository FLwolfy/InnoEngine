using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

using Inno.Core.Reflection;
using Inno.Core.Serialization;
using Inno.Editor.ImGui;
using Inno.Editor.Inspection;
using Inno.Engine.Scene;
using Inno.Engine.Scene.Components;
using Inno.Native.ImGui;
using Inno.Platform.ImGui;
using NativeImGui = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.Panels.Inspectors;

[InspectorDrawer(typeof(GameObject))]
internal sealed class GameObjectInspectorDrawer : IInspectorDrawer
{
    private const nuint C_NAME_BUFFER_SIZE = 512;
    private const nuint C_SEARCH_BUFFER_SIZE = 256;

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

        NativeImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(4f, 2f));
        NativeImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(4f, 1f));
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

    private static void DrawComponents(InspectorDrawContext context, GameObject gameObject)
    {
        IReadOnlyList<GameComponent> components = gameObject.GetComponents();
        GameComponent? componentToRemove = null;
        for (int i = 0; i < components.Count; i++)
        {
            GameComponent component = components[i];
            Type componentType = component.GetType();
            string componentId = component.identity.persistentId.ToString("N");
            bool open = ImGuiWidget.CollapsingCard(
                componentId,
                componentType.Name,
                component is GameBehavior behavior
                    ? () =>
                    {
                        bool enabled = behavior.enabled;
                        if (ImGuiWidget.CompactCheckbox($"enabled_{componentId}", ref enabled))
                        {
                            behavior.enabled = enabled;
                        }
                    }
                    : null,
                componentType == typeof(Transform)
                    ? null
                    : () =>
                    {
                        if (ImGuiWidget.IconButton($"remove_component_{componentId}", ImGuiIcon.Xmark,
                                "Remove Component"))
                        {
                            componentToRemove = component;
                        }
                    });

            if (ImGuiWidget.BeginContextMenu($"##component_menu_{componentId}"))
            {
                if (NativeImGui.MenuItem("Reset Component"))
                {
                    gameObject.ResetComponent(component);
                }

                bool canRemove = componentType != typeof(Transform);
                if (NativeImGui.MenuItem("Remove Component", string.Empty, false, canRemove))
                {
                    componentToRemove = component;
                }

                ImGuiWidget.EndContextMenu();
            }

            if (!open)
            {
                continue;
            }

            NativeImGui.Unindent();
            IReadOnlyList<SerializedProperty> properties = SerializationManager.GetProperties(component);
            for (int propertyIndex = 0; propertyIndex < properties.Count; propertyIndex++)
            {
                context.properties.Draw(
                    context.editorContext,
                    $"gameObject.{gameObject.identity.persistentId:N}.{componentId}",
                    properties[propertyIndex]);
            }

            NativeImGui.Indent();
            NativeImGui.TreePop();
        }

        if (componentToRemove is not null)
        {
            _ = gameObject.RemoveComponent(componentToRemove);
        }
    }

    private void DrawAddComponent(InspectorDrawContext context, GameObject gameObject)
    {
        if (ImGuiWidget.CenteredButton("Add Component", 7f))
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

        IReadOnlyList<Type> componentTypes = TypeCache.GetSubTypesOf<GameComponent>();
        for (int i = 0; i < componentTypes.Count; i++)
        {
            Type componentType = componentTypes[i];
            if (!IsAddable(componentType, gameObject) ||
                (!string.IsNullOrWhiteSpace(m_componentSearch) &&
                 componentType.Name.IndexOf(m_componentSearch, StringComparison.OrdinalIgnoreCase) < 0))
            {
                continue;
            }

            if (NativeImGui.Selectable(componentType.Name))
            {
                _ = gameObject.AddComponent(componentType);
                NativeImGui.CloseCurrentPopup();
                break;
            }
        }

        ImGuiWidget.EndSearchPopup();
    }

    private static bool IsAddable(Type componentType, GameObject gameObject)
    {
        if (componentType.IsAbstract ||
            componentType.GetConstructor(
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.NonPublic,
                binder: null,
                Type.EmptyTypes,
                modifiers: null) is null)
        {
            return false;
        }

        return componentType.IsDefined(typeof(AllowMultipleComponentAttribute), inherit: false) ||
            !gameObject.GetComponents().Any(component => component.GetType() == componentType);
    }
}
