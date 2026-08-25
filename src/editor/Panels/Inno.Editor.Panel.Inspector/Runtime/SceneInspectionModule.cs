using System;

using Inno.Assets;
using Inno.Assets.Core;
using Inno.Assets.File;
using Inno.Editor.Core;
using Inno.Editor.Inspection;
using Inno.Editor.Interactions;
using Inno.Editor.Scene;
using Inno.Editor.Settings;

namespace Inno.Editor.Panel.Inspector;

/// <summary>
/// Composes the Inspector panel with the shared inspection registries and scene-specific dependencies.
/// </summary>
[EditorModule("scene-inspection", order: 210)]
internal sealed class SceneInspectionModule : EditorModule
{
    private readonly InspectionDrawerRegistry m_inspectors;
    private readonly PropertyDrawerRegistry m_properties;
    private readonly SerializedPropertyRenderer m_renderer;
    private readonly GameObjectTagCatalog m_tags = new();

    /// <summary>
    /// Creates the scene inspection module and its generation-aware drawer registries.
    /// </summary>
    /// <param name="interactions">
    /// The active editor interaction entry point supplied to drawers.
    /// </param>
    /// <param name="edits">
    /// The scene editing service used to record granular property changes.
    /// </param>
    /// <param name="assetIcons">
    /// The Asset Browser presentation provider used by Asset inspection drawers.
    /// </param>
    /// <param name="layerSettings">
    /// The project Settings layer catalog module.
    /// </param>
    /// <param name="settings">
    /// The project Settings service supplied to built-in drawers.
    /// </param>
    /// <exception cref="System.ArgumentNullException">
    /// Thrown when <paramref name="interactions"/>, <paramref name="edits"/>,
    /// <paramref name="assetIcons"/>, <paramref name="layerSettings"/>, or <paramref name="settings"/>
    /// is <see langword="null"/>.
    /// </exception>
    internal SceneInspectionModule(
        EditorInteractions interactions,
        SceneEdits edits,
        IInspectionIconProvider<AssetFileEntry> assetIcons,
        GameLayerSettingsModule layerSettings,
        EditorSettings settings)
    {
        System.ArgumentNullException.ThrowIfNull(interactions);
        System.ArgumentNullException.ThrowIfNull(edits);
        System.ArgumentNullException.ThrowIfNull(assetIcons);
        System.ArgumentNullException.ThrowIfNull(layerSettings);
        System.ArgumentNullException.ThrowIfNull(settings);
        var activator = new InspectionDrawerActivator(
            interactions,
            edits,
            assetIcons,
            m_tags,
            layerSettings,
            settings);
        m_inspectors = new InspectionDrawerRegistry(interactions, activator.Create);
        m_properties = new PropertyDrawerRegistry(interactions);
        m_renderer = new SerializedPropertyRenderer(
            m_properties,
            interactions,
            new SceneInspectionPropertyEditService(edits));
    }

    /// <inheritdoc />
    protected override void Capture(EditorState state)
        => state.Set("tags", m_tags.GetTags());

    /// <inheritdoc />
    protected override void Restore(EditorState state)
        => m_tags.Restore(state.Get("tags", Array.Empty<string>()));

    internal bool TryResolve(
        EditorContext editorContext,
        object target,
        out IInspectionDrawer? drawer,
        out InspectionDrawContext? drawContext)
    {
        if (target is AssetFileEntry { isDirectory: false } entry &&
            AssetManager.TryLoad(entry.relativePath, out AssetObject? asset) &&
            asset is not null &&
            m_inspectors.TryResolveExact(
                editorContext,
                asset,
                m_renderer,
                out drawer,
                out drawContext))
        {
            return true;
        }
        return m_inspectors.TryResolve(
            editorContext,
            target,
            m_renderer,
            out drawer,
            out drawContext);
    }

    /// <inheritdoc />
    protected override void OnDispose()
    {
        m_inspectors.Dispose();
        m_properties.Dispose();
    }
}
