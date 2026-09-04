using System;

using Inno.Assets;
using Inno.Assets.Pipeline;
using Inno.Extensibility.Types;
using Inno.Core.Logging;
using Inno.Core.Serialization;
using Inno.Editor.Core;
using Inno.Editor.Inspection;
using Inno.Editor.Interactions;
using Inno.Editor.Scene;
using Inno.Editor.Settings;
using Inno.Runtime;

namespace Inno.Editor.Panel.Inspector;

/// <summary>
/// Composes the Inspector panel with the shared inspection registries and scene-specific dependencies.
/// </summary>
[EditorModule("scene-inspection", order: 210)]
internal sealed class SceneInspectionModule : EditorModule
{
    private readonly AssetPipeline m_assets;
    private readonly InspectionDrawerRegistry m_inspectors;
    private readonly PropertyDrawerRegistry m_properties;
    private readonly SerializedPropertyRenderer m_renderer;

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
    /// <param name="classificationSettings">
    /// The project tag and layer settings module.
    /// </param>
    /// <param name="settings">
    /// The project Settings service supplied to built-in drawers.
    /// </param>
    /// <param name="assets">
    /// The authoring asset pipeline used to resolve selected asset entries.
    /// </param>
    /// <param name="types">
    /// The host-owned type catalog used by drawer registries.
    /// </param>
    /// <param name="serialization">
    /// The host-owned serialization registry used by structured-property drawers.
    /// </param>
    /// <param name="runtimeSession">
    /// The isolated Edit session whose scene world supplies object-reference candidates.
    /// </param>
    /// <param name="logs">
    /// The host-owned log router used by inspection error isolation.
    /// </param>
    /// <exception cref="System.ArgumentNullException">
    /// Thrown when <paramref name="interactions"/>, <paramref name="edits"/>,
    /// <paramref name="assetIcons"/>, <paramref name="classificationSettings"/>, or <paramref name="settings"/>
    /// is <see langword="null"/>.
    /// </exception>
    internal SceneInspectionModule(
        EditorInteractions interactions,
        SceneEdits edits,
        IInspectionIconProvider<AssetFileEntry> assetIcons,
        SceneProjectSettingsModule classificationSettings,
        EditorSettings settings,
        AssetPipeline assets,
        TypeCatalog types,
        SerializationRegistry serialization,
        RuntimeSession runtimeSession,
        LogRouter logs)
    {
        System.ArgumentNullException.ThrowIfNull(interactions);
        System.ArgumentNullException.ThrowIfNull(edits);
        System.ArgumentNullException.ThrowIfNull(assetIcons);
        System.ArgumentNullException.ThrowIfNull(classificationSettings);
        System.ArgumentNullException.ThrowIfNull(settings);
        System.ArgumentNullException.ThrowIfNull(assets);
        System.ArgumentNullException.ThrowIfNull(types);
        System.ArgumentNullException.ThrowIfNull(serialization);
        System.ArgumentNullException.ThrowIfNull(runtimeSession);
        System.ArgumentNullException.ThrowIfNull(logs);
        m_assets = assets;
        var activator = new InspectionDrawerActivator(
            interactions,
            edits,
            assetIcons,
            classificationSettings,
            settings,
            serialization,
            logs);
        m_inspectors = new InspectionDrawerRegistry(interactions, activator.Create, types);
        m_properties = new PropertyDrawerRegistry(
            interactions,
            types,
            serialization,
            [assets, runtimeSession]);
        m_renderer = new SerializedPropertyRenderer(
            m_properties,
            interactions,
            new SceneInspectionPropertyEditService(edits),
            logs);
    }

    internal bool TryResolve(
        EditorContext editorContext,
        object target,
        out IInspectionDrawer? drawer,
        out InspectionDrawContext? drawContext)
    {
        if (target is AssetFileEntry { isDirectory: false } entry &&
            m_assets.TryLoad(entry.assetPath, out AssetObject? asset) &&
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

    /// <summary>
    /// Releases resources retained by this feature after it has stopped.
    /// </summary>
    protected override void OnDispose()
    {
        m_inspectors.Dispose();
        m_properties.Dispose();
    }
}
