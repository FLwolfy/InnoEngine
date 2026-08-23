
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
using Inno.Engine.Scene;
using Inno.Engine.Scene.Components;
using Inno.Native.ImGui;
using Inno.Platform.ImGui;
using NativeImGui = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.Panel.Inspector;

[InspectionDrawer(typeof(GameObject))]
internal sealed class GameObjectInspectionDrawer : InspectionDrawer<GameObject>
{
    private const nuint C_SEARCH_BUFFER_SIZE = 256;

    private readonly InspectorCardControls m_cardControls = new();
    private readonly SceneEdits m_edits;
    private readonly GameObjectTagSelector m_tagSelector;
    private readonly GameObjectLayerSelector m_layerSelector;
    private string m_componentSearch = string.Empty;

    /// <summary>
    /// Creates a GameObject drawer backed by the current project tag catalog.
    /// </summary>
    /// <param name="edits">The Scene editing service used for compact Undo/Redo records.</param>
    /// <param name="tags">The project tag catalog displayed in the target header.</param>
    /// <param name="layerSettings">The project layer catalog displayed in the target header.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="edits"/>, <paramref name="tags"/>, or
    /// <paramref name="layerSettings"/> is <see langword="null"/>.
    /// </exception>
    internal GameObjectInspectionDrawer(
        SceneEdits edits,
        GameObjectTagCatalog tags,
        GameLayerSettingsModule layerSettings)
    {
        m_edits = edits ?? throw new ArgumentNullException(nameof(edits));
        m_tagSelector = new GameObjectTagSelector(
            tags ?? throw new ArgumentNullException(nameof(tags)),
            edits);
        m_layerSelector = new GameObjectLayerSelector(
            layerSettings ?? throw new ArgumentNullException(nameof(layerSettings)),
            edits);
    }

    public override string icon => ImGuiIcon.Cube;

    protected override string GetName(InspectionDrawContext context, GameObject target)
        => target.name;

    protected override Action<string>? GetNameSetter(
        InspectionDrawContext context,
        GameObject target)
        => name => m_edits.RenameGameObject(target, name);

    protected override void DrawHeader(InspectionDrawContext context, GameObject target)
    {
        bool active = target.activeSelf;
        if (EditorWidget.CompactCheckbox(
                $"target_active_{target.identity.persistentId:N}",
                ref active))
        {
            m_edits.SetGameObjectActive(target, active);
        }
        NativeImGui.SameLine();
        NativeImGui.TextUnformatted("Active");
        NativeImGui.SameLine(0f, EditorWidget.style.inspectorHeaderSectionSpacing);
        float available = NativeImGui.GetContentRegionAvail().X;
        float tagLabelWidth = EditorWidget.GetLabelChipSize("Tag").X;
        float layerLabelWidth = EditorWidget.GetLabelChipSize("Layer").X;
        float controlWidth = MathF.Max(
            1f,
            (available - tagLabelWidth - layerLabelWidth -
             EditorWidget.style.inspectorHeaderSectionSpacing) * 0.5f);
        m_tagSelector.Draw(context, target, controlWidth);
        NativeImGui.SameLine(0f, EditorWidget.style.inspectorHeaderSectionSpacing);
        m_layerSelector.Draw(target, controlWidth);
    }

    protected override void Draw(InspectionDrawContext context, GameObject gameObject)
    {
        if (!gameObject.isRuntimeValid || !gameObject.scene.isLoaded)
        {
            _ = context.interactions.For(context.interactions.focusedArea).Select();
            NativeImGui.TextUnformatted("Selected GameObject is not available in the active scene.");
            return;
        }

        if (!m_layerSelector.IsLayerDefined(gameObject.layer))
        {
            NativeImGui.PushStyleColor(ImGuiCol.Text, EditorPalette.error);
            ImGuiWidget.WrappedText(
                $"Layer slot {gameObject.layer.index} is not defined in the current project settings. " +
                "Choose Default or restore that layer definition.");
            NativeImGui.PopStyleColor();
            NativeImGui.Spacing();
        }

        NativeImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, EditorWidget.style.compactItemSpacing);
        NativeImGui.PushStyleVar(ImGuiStyleVar.FramePadding, EditorWidget.style.compactFramePadding);
        try
        {
            DrawComponents(context, gameObject);
            DrawAddComponent(context, gameObject);
        }
        finally
        {
            NativeImGui.PopStyleVar(2);
        }
    }

    private void DrawComponents(InspectionDrawContext context, GameObject gameObject)
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
                            _ = m_edits.ChangeProperty(
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
                        m_edits,
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

    private void DrawAddComponent(InspectionDrawContext context, GameObject gameObject)
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
