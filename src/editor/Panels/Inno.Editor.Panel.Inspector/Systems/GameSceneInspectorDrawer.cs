
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
using Inno.Native.ImGui;
using Inno.Platform.ImGui;
using NativeImGui = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.Panel.Inspector;

[InspectorDrawer(typeof(GameScene))]
internal sealed class GameSceneInspectorDrawer : InspectorDrawer<GameScene>
{
    private const nuint C_SEARCH_BUFFER_SIZE = 256;

    private readonly InspectorCardControls m_cardControls = new();
    private string m_systemSearch = string.Empty;

    public override string icon => ImGuiIcon.LayerGroup;

    protected override string GetName(InspectorDrawContext context, GameScene target)
        => target.name;

    protected override Action<string>? GetNameSetter(
        InspectorDrawContext context,
        GameScene target)
        => name => context.edits.RenameScene(target, name);

    protected override void DrawHeader(InspectorDrawContext context, GameScene target)
        => NativeImGui.TextUnformatted(target.isLoaded ? "Loaded Scene" : "Scene");

    protected override void Draw(InspectorDrawContext context, GameScene scene)
    {
        if (!scene.isLoaded || scene.isDestroyed)
        {
            _ = context.interactions.For(context.interactions.focusedArea).Select();
            NativeImGui.TextUnformatted("Selected scene is no longer loaded.");
            return;
        }

        NativeImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, EditorWidget.style.compactItemSpacing);
        NativeImGui.PushStyleVar(ImGuiStyleVar.FramePadding, EditorWidget.style.compactFramePadding);
        try
        {
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
            var editorTarget = new SystemEditorTarget(scene, system);
            bool open = EditorWidget.CollapsingCard(
                systemId,
                system.GetType().Name,
                () =>
                {
                    bool enabled = system.enabled;
                    if (EditorWidget.CompactCheckbox($"enabled_{systemId}", ref enabled))
                    {
                        _ = context.edits.ChangeProperty(
                            system,
                            "enabled",
                            () => system.enabled = enabled,
                            enabled ? "Enable System" : "Disable System",
                            mergeKey: null);
                    }
                },
                () => m_cardControls.DrawSystem(
                    context.edits,
                    scene,
                    system,
                    i,
                    systems.Count,
                    () => context.interactions
                        .For(
                            InspectorAreas.System,
                            editorTarget)
                        .Enqueue(InspectorActions.RemoveSystem)),
                dimmed: !system.enabled,
                trailingControlWidth: m_cardControls.width,
                drawContextMenu: () => _ = EditorMenuRenderer.ContextMenu(
                    $"##system_menu_{systemId}",
                    context.interactions.For(InspectorAreas.System, editorTarget)));
            if (!open)
            {
                NativeImGui.Dummy(new Vector2(0f, EditorWidget.style.inspectorCardSpacing));
                continue;
            }

            NativeImGui.Unindent();
            EditorWidget.CardBody(
                systemId,
                () =>
                {
                    foreach (SerializedProperty property in SerializationManager.GetProperties(system))
                    {
                        context.properties.Draw(
                            context.editorContext,
                            system,
                            $"scene.{scene.identity.persistentId:N}.{systemId}",
                            property);
                    }
                },
                dimmed: !system.enabled);
            NativeImGui.Indent();
            NativeImGui.TreePop();
            NativeImGui.Dummy(new Vector2(0f, EditorWidget.style.inspectorCardSpacing));
        }

    }

    private void DrawAddSystem(InspectorDrawContext context, GameScene scene)
    {
        if (EditorWidget.CenteredButton(
                "Add System",
                EditorWidget.style.inspectorAddButtonTopPadding))
        {
            m_systemSearch = string.Empty;
            NativeImGui.OpenPopup("##add_system_popup");
        }
        if (!EditorWidget.BeginSearchPopup(
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
        EditorWidget.EndSearchPopup();
    }

}
