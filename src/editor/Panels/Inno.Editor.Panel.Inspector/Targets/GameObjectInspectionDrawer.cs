
using System;
using System.Collections.Generic;
using System.Numerics;

using Inno.Core.Serialization;
using Inno.Core.Logging;
using Inno.Editor.Core;
using Inno.Editor.Inspection;
using Inno.Editor.Interactions;
using Inno.Editor.ImGui;
using Inno.Editor.ImGui.ImGuiWidget;
using EditorWidget = Inno.Editor.ImGui.ImGuiWidget.ImGuiWidget;
using Inno.Editor.Scene;
using Inno.Editor.Settings;
using Inno.Scene;
using Inno.Scene.Components;
using Inno.Native.ImGui;
using Inno.Platform.Sdl3.ImGui;
using NativeImGui = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.Panel.Inspector;

[InspectionDrawer(typeof(GameObject))]
internal sealed class GameObjectInspectionDrawer : InspectionDrawer<GameObject>
{
    private const nuint C_SEARCH_BUFFER_SIZE = 256;

    private readonly InspectorCardControls m_cardControls;
    private readonly SceneEdits m_edits;
    private readonly SerializationRegistry m_serialization;
    private readonly GameObjectTagSelector m_tagSelector;
    private readonly GameObjectLayerSelector m_layerSelector;
    private readonly EditorSettings m_settings;
    private string m_componentSearch = string.Empty;

    /// <summary>
    /// Creates a GameObject drawer backed by the current project classification settings.
    /// </summary>
    /// <param name="edits">
    /// The Scene editing service used for compact Undo/Redo records.
    /// </param>
    /// <param name="classificationSettings">
    /// The project tag and layer catalogs displayed in the target header.
    /// </param>
    /// <param name="settings">
    /// The project Settings service that owns semantic icon values.
    /// </param>
    /// <param name="serialization">
    /// The serialization registry that describes component properties in the active generation.
    /// </param>
    /// <param name="logs">
    /// The application log router used by component card controls.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="edits"/>, <paramref name="classificationSettings"/>, or
    /// <paramref name="settings"/> is <see langword="null"/>.
    /// </exception>
    internal GameObjectInspectionDrawer(
        SceneEdits edits,
        SceneProjectSettingsModule classificationSettings,
        EditorSettings settings,
        SerializationRegistry serialization,
        LogRouter logs)
    {
        m_edits = edits ?? throw new ArgumentNullException(nameof(edits));
        ArgumentNullException.ThrowIfNull(classificationSettings);
        m_tagSelector = new GameObjectTagSelector(
            classificationSettings,
            edits);
        m_layerSelector = new GameObjectLayerSelector(
            classificationSettings,
            edits);
        m_settings = settings ?? throw new ArgumentNullException(nameof(settings));
        m_serialization = serialization ?? throw new ArgumentNullException(nameof(serialization));
        m_cardControls = new InspectorCardControls(logs);
    }

    /// <summary>
    /// Gets the icon glyph used to represent this item in the editor.
    /// </summary>
    public override string icon => m_settings
        .Get("Editor/Appearance/Icons/GameObject")
        .GetAsString("value", ImGuiIcon.Cube)!;

    /// <summary>
    /// Binds a caller-visible label to the current inspection target.
    /// </summary>
    /// <param name="context">
    /// The operation scope that provides state, services, and ownership boundaries.
    /// </param>
    /// <param name="target">
    /// The existing target that receives the validated result.
    /// </param>
    /// <returns>
    /// The validated (string name, actionstring? setter) that represents the completed operation.
    /// </returns>
    protected override (string name, Action<string>? setter) BindName(
        InspectionDrawContext context,
        GameObject target)
        => (target.name, name => m_edits.RenameGameObject(target, name));

    /// <summary>
    /// Renders the header presentation for the current editor frame.
    /// </summary>
    /// <param name="context">
    /// The operation scope that provides state, services, and ownership boundaries.
    /// </param>
    /// <param name="target">
    /// The existing target that receives the validated result.
    /// </param>
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
        m_tagSelector.Draw(target, controlWidth);
        NativeImGui.SameLine(0f, EditorWidget.style.inspectorHeaderSectionSpacing);
        m_layerSelector.Draw(target, controlWidth);
    }

    /// <summary>
    /// Renders the value presentation for the current editor frame.
    /// </summary>
    /// <param name="context">
    /// The operation scope that provides state, services, and ownership boundaries.
    /// </param>
    /// <param name="gameObject">
    /// The scene object captured by this structural snapshot.
    /// </param>
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

        if (!m_tagSelector.IsTagDefined(gameObject.tag))
        {
            NativeImGui.PushStyleColor(ImGuiCol.Text, EditorPalette.error);
            ImGuiWidget.WrappedText(
                $"Tag '{gameObject.tag}' is not defined in the current Project Settings. " +
                "Choose another tag or restore that tag definition.");
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
            MissingGameComponent? missing = component as MissingGameComponent;
            IReadOnlyList<SerializedProperty> properties = missing is null
                ? m_serialization.GetProperties(component)
                : Array.Empty<SerializedProperty>();
            bool hasBody = missing is not null || properties.Count > 0;
            string componentId = component.identity.persistentId.ToString("N");
            GameBehavior? behavior = component as GameBehavior;
            var editorTarget = new ComponentEditorTarget(gameObject, component);
            bool open = EditorWidget.CollapsingCard(
                componentId,
                missing?.missingTypeName ?? componentType.Name,
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
                                InspectorInteractionIds.C_COMPONENT_AREA,
                                editorTarget)
                            .Enqueue(InspectorInteractionIds.C_REMOVE_COMPONENT))),
                dimmed: behavior is { enabled: false },
                trailingControlWidth: componentType == typeof(Transform)
                    ? 0f
                    : m_cardControls.width,
                drawContextMenu: () => _ = EditorMenuRenderer.ContextMenu(
                    $"##component_menu_{componentId}",
                    context.interactions.For(InspectorInteractionIds.C_COMPONENT_AREA, editorTarget)));

            if (!open)
            {
                NativeImGui.Dummy(new Vector2(0f, EditorWidget.style.inspectorCardSpacing));
                continue;
            }

            if (hasBody)
            {
                NativeImGui.Unindent();
                EditorWidget.CardBody(
                    componentId,
                    () =>
                    {
                        if (missing is not null)
                        {
                            NativeImGui.PushStyleColor(ImGuiCol.Text, EditorPalette.error);
                            ImGuiWidget.WrappedText(
                                $"Missing component script ({missing.missingType.stableId:D}). " +
                                "Its serialized state is preserved and will recover automatically when the type returns.");
                            NativeImGui.PopStyleColor();
                            return;
                        }
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
            }
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

        try
        {
            EditorInteraction interaction = context.interactions.For(InspectorInteractionIds.C_COMPONENT_AREA, gameObject);
            if (EditorMenuRenderer.DrawSearchItems(
                    interaction,
                    interaction.BuildMenu().items,
                    m_componentSearch))
                NativeImGui.CloseCurrentPopup();
        }
        finally
        {
            EditorWidget.EndSearchPopup();
        }
    }

}
