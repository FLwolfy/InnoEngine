
using System;
using System.Collections.Generic;
using System.Numerics;

using Inno.Core.Serialization;
using Inno.Editor.Core;
using Inno.Editor.Inspection;
using Inno.Editor.Interactions;
using Inno.Editor.ImGui;
using Inno.Editor.ImGui.ImGuiWidget;
using EditorWidget = Inno.Editor.ImGui.ImGuiWidget.ImGuiWidget;
using Inno.Editor.Scene;
using Inno.Editor.Settings;
using Inno.Engine.Scene;
using Inno.Native.ImGui;
using Inno.Platform.ImGui;
using NativeImGui = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.Panel.Inspector;

[InspectionDrawer(typeof(GameScene))]
internal sealed class GameSceneInspectionDrawer : InspectionDrawer<GameScene>
{
    private const nuint C_SEARCH_BUFFER_SIZE = 256;

    private readonly InspectorCardControls m_cardControls = new();
    private readonly SceneEdits m_edits;
    private readonly EditorSettings m_settings;
    private string m_systemSearch = string.Empty;

    /// <summary>
    /// Creates a Scene drawer backed by the Scene editing service.
    /// </summary>
    /// <param name="edits">
    /// The Scene editing service used for compact Undo/Redo records.
    /// </param>
    /// <param name="settings">
    /// The project Settings service that owns semantic icon values.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="edits"/> or <paramref name="settings"/> is <see langword="null"/>.
    /// </exception>
    internal GameSceneInspectionDrawer(SceneEdits edits, EditorSettings settings)
    {
        m_edits = edits ?? throw new ArgumentNullException(nameof(edits));
        m_settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    /// <inheritdoc />
    public override string icon => m_settings
        .Get("Global/Appearance/Icons/Scene")
        .GetAsString("value", ImGuiIcon.Cubes)!;

    protected override (string name, Action<string>? setter) BindName(
        InspectionDrawContext context,
        GameScene target)
        => (target.name, name => m_edits.RenameScene(target, name));

    protected override void DrawHeader(InspectionDrawContext context, GameScene target)
        => NativeImGui.TextUnformatted(target.isLoaded ? "Loaded Scene" : "Scene");

    protected override void Draw(InspectionDrawContext context, GameScene scene)
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

    private void DrawSystems(InspectionDrawContext context, GameScene scene)
    {
        IReadOnlyList<GameSystem> systems = scene.GetSystems();
        for (int i = 0; i < systems.Count; i++)
        {
            GameSystem system = systems[i];
            MissingGameSystem? missing = system as MissingGameSystem;
            string systemId = system.identity.persistentId.ToString("N");
            var editorTarget = new SystemEditorTarget(scene, system);
            bool open = EditorWidget.CollapsingCard(
                systemId,
                missing?.missingTypeName ?? system.GetType().Name,
                missing is null ? () =>
                {
                    bool enabled = system.enabled;
                    if (EditorWidget.CompactCheckbox($"enabled_{systemId}", ref enabled))
                    {
                        _ = m_edits.ChangeProperty(
                            system,
                            "enabled",
                            () => system.enabled = enabled,
                            enabled ? "Enable System" : "Disable System",
                            mergeKey: null);
                    }
                } : null,
                () => m_cardControls.DrawSystem(
                    m_edits,
                    scene,
                    system,
                    i,
                    systems.Count,
                    () => context.interactions
                        .For(
                            InspectorInteractionIds.C_SYSTEM_AREA,
                            editorTarget)
                        .Enqueue(InspectorInteractionIds.C_REMOVE_SYSTEM)),
                dimmed: !system.enabled,
                trailingControlWidth: m_cardControls.width,
                drawContextMenu: () => _ = EditorMenuRenderer.ContextMenu(
                    $"##system_menu_{systemId}",
                    context.interactions.For(InspectorInteractionIds.C_SYSTEM_AREA, editorTarget)));
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
                    if (missing is not null)
                    {
                        NativeImGui.PushStyleColor(ImGuiCol.Text, EditorPalette.error);
                        ImGuiWidget.WrappedText(
                            $"Missing system script ({missing.missingTypeId:D}). " +
                            "Its serialized state is preserved and will recover automatically when the type returns.");
                        NativeImGui.PopStyleColor();
                        return;
                    }
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

    private void DrawAddSystem(InspectionDrawContext context, GameScene scene)
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

        try
        {
            EditorInteraction interaction = context.interactions.For(InspectorInteractionIds.C_SYSTEM_AREA, scene);
            if (EditorMenuRenderer.DrawSearchItems(
                    interaction,
                    interaction.BuildMenu().items,
                    m_systemSearch))
                NativeImGui.CloseCurrentPopup();
        }
        finally
        {
            EditorWidget.EndSearchPopup();
        }
    }

}
