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
using Inno.Native.ImGui;
using Inno.Platform.ImGui;
using NativeImGui = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.Scene.Inspection;

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
            context.editorContext.selection.Clear();
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
                    () => context.editorContext.Enqueue(
                        EditorActionIds.Remove,
                        typeof(SceneSurface.System),
                        new SystemEditorTarget(scene, system))),
                dimmed: !system.enabled,
                trailingControlWidth: m_cardControls.width);
            _ = EditorMenuRenderer.ContextMenu(
                $"##system_menu_{systemId}",
                new EditorMenuContext(
                    context.editorContext,
                    typeof(SceneSurface.System),
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

        var menuContext = new EditorMenuContext(
            context.editorContext,
            typeof(SceneSurface.AddSystem),
            scene);
        if (EditorMenuRenderer.DrawSearchItems(
                menuContext,
                context.editorContext.BuildMenu(menuContext.surface, menuContext.target).items,
                m_systemSearch))
            NativeImGui.CloseCurrentPopup();
        ImGuiWidget.EndSearchPopup();
    }
}
