
using System;
using System.Collections.Generic;
using System.Numerics;

using Inno.Core.Serialization;
using Inno.Editor.Core;
using Inno.Editor.Interactions.Actions;
using Inno.Editor.Interactions.Menus;
using Inno.Editor.Interactions;
using Inno.Editor.ImGui;
using Inno.Editor.ImGui.Renderers;
using Inno.Editor.ImGui.Widgets;
using Inno.Engine.Scene;
using Inno.Native.ImGui;
using Inno.Platform.ImGui;
using NativeImGui = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.Panel.Inspector;

[InspectorDrawer(typeof(GameScene))]
internal sealed class GameSceneInspectorDrawer : IInspectorDrawer
{
    private const nuint C_NAME_BUFFER_SIZE = 512;
    private const nuint C_SEARCH_BUFFER_SIZE = 256;

    private readonly InspectorCardControls m_cardControls = new();
    private string m_systemSearch = string.Empty;

    /// <inheritdoc />
    public void Draw(InspectorDrawContext context)
    {
        var scene = (GameScene)context.target;
        if (!scene.isLoaded || scene.isDestroyed)
        {
            _ = context.interactions.For(context.interactions.focusedArea).Select();
            NativeImGui.TextUnformatted("Selected scene is no longer loaded.");
            return;
        }

        NativeImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, ImGuiWidget.style.compactItemSpacing);
        NativeImGui.PushStyleVar(ImGuiStyleVar.FramePadding, ImGuiWidget.style.compactFramePadding);
        try
        {
            string name = scene.name;
            NativeImGui.SetNextItemWidth(-1f);
            if (NativeImGui.InputText(
                    $"##scene_name_{scene.identity.persistentId:N}",
                    ref name,
                    C_NAME_BUFFER_SIZE,
                    ImGuiInputTextFlags.None))
                scene.name = name;
            NativeImGui.Spacing();
            DrawSystems(context, scene);
            DrawAddSystem(context, scene);
        }
        finally
        {
            NativeImGui.PopStyleVar(2);
        }
    }

    private void DrawSystems(InspectorDrawContext context, GameScene scene)
    {
        IReadOnlyList<GameSystem> systems = scene.GetSystems();
        for (int i = 0; i < systems.Count; i++)
        {
            GameSystem system = systems[i];
            string systemId = system.identity.persistentId.ToString("N");
            bool open = ImGuiWidget.CollapsingCard(
                systemId,
                system.GetType().Name,
                () =>
                {
                    bool enabled = system.enabled;
                    if (ImGuiWidget.CompactCheckbox($"enabled_{systemId}", ref enabled))
                        system.enabled = enabled;
                },
                () => m_cardControls.DrawSystem(
                    scene,
                    system,
                    i,
                    systems.Count,
                    () => context.interactions
                        .For(
                            InspectorAreas.System,
                            new SystemEditorTarget(scene, system))
                        .Enqueue(InspectorActions.RemoveSystem)),
                dimmed: !system.enabled,
                trailingControlWidth: m_cardControls.width);
            _ = EditorMenuRenderer.ContextMenu(
                $"##system_menu_{systemId}",
                context.interactions.For(
                    InspectorAreas.System,
                    new SystemEditorTarget(scene, system)));
            if (!open)
            {
                NativeImGui.Dummy(new Vector2(0f, ImGuiWidget.style.inspectorCardSpacing));
                continue;
            }

            NativeImGui.Unindent();
            ImGuiWidget.CardBody(
                systemId,
                () =>
                {
                    foreach (SerializedProperty property in SerializationManager.GetProperties(system))
                    {
                        context.properties.Draw(
                            context.editorContext,
                            $"scene.{scene.identity.persistentId:N}.{systemId}",
                            property);
                    }
                },
                dimmed: !system.enabled);
            NativeImGui.Indent();
            NativeImGui.TreePop();
            NativeImGui.Dummy(new Vector2(0f, ImGuiWidget.style.inspectorCardSpacing));
        }

    }

    private void DrawAddSystem(InspectorDrawContext context, GameScene scene)
    {
        if (ImGuiWidget.CenteredButton(
                "Add System",
                ImGuiWidget.style.inspectorAddButtonTopPadding))
        {
            m_systemSearch = string.Empty;
            NativeImGui.OpenPopup("##add_system_popup");
        }
        if (!ImGuiWidget.BeginSearchPopup(
                "##add_system_popup",
                ref m_systemSearch,
                "Search systems...",
                C_SEARCH_BUFFER_SIZE))
            return;

        EditorInteraction interaction = context.interactions.For(InspectorAreas.System, scene);
        if (EditorMenuRenderer.DrawSearchItems(
                interaction,
                interaction.BuildMenu().items,
                m_systemSearch))
            NativeImGui.CloseCurrentPopup();
        ImGuiWidget.EndSearchPopup();
    }
}
