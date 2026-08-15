using System;
using System.Collections.Generic;
using System.Numerics;

using Inno.Core.ECS;
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
        IReadOnlyList<Component> components = gameObject.GetComponents();
        Type? removeType = null;
        for (int i = 0; i < components.Count; i++)
        {
            if (components[i] is not GameBehavior behavior)
            {
                continue;
            }

            Type componentType = behavior.GetType();
            string componentId = behavior.identity.persistentId.ToString("N");
            bool open = ImGuiWidget.CollapsingCard(
                componentId,
                componentType.Name,
                () =>
                {
                    bool enabled = behavior.enabled;
                    if (ImGuiWidget.CompactCheckbox($"enabled_{componentId}", ref enabled))
                    {
                        behavior.enabled = enabled;
                    }
                },
                componentType == typeof(Transform)
                    ? null
                    : () =>
                    {
                        if (ImGuiWidget.IconButton($"remove_component_{componentId}", ImGuiIcon.Xmark,
                                "Remove Component"))
                        {
                            removeType = componentType;
                        }
                    });

            if (ImGuiWidget.BeginContextMenu($"##component_menu_{componentId}"))
            {
                bool canRemove = componentType != typeof(Transform);
                if (NativeImGui.MenuItem("Remove Component", string.Empty, false, canRemove))
                {
                    removeType = componentType;
                }

                ImGuiWidget.EndContextMenu();
            }

            if (!open)
            {
                continue;
            }

            NativeImGui.Unindent();
            IReadOnlyList<SerializedProperty> properties = ((ISerializable)behavior).GetSerializedProperties();
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

        if (removeType is not null)
        {
            _ = gameObject.RemoveComponent(removeType);
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

        IReadOnlyList<Type> componentTypes = TypeCache.GetSubTypesOf<GameBehavior>();
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

        IReadOnlyList<Component> components = gameObject.GetComponents();
        for (int i = 0; i < components.Count; i++)
        {
            if (components[i].GetType() == componentType)
            {
                return false;
            }
        }

        return true;
    }
}
