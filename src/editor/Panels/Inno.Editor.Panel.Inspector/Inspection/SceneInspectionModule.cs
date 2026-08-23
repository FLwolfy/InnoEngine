using Inno.Assets.File;
using Inno.Editor.Core;
using Inno.Editor.Inspection;
using Inno.Editor.Interactions;
using Inno.Editor.Scene;

namespace Inno.Editor.Panel.Inspector;

/// <summary>
/// Composes the Inspector panel with the shared inspection registries and scene-specific dependencies.
/// </summary>
[EditorModule(order: 210)]
public sealed class SceneInspectionModule : EditorModule, IEditorWorkspaceState, System.IDisposable
{
    private readonly InspectionDrawerRegistry m_inspectors;
    private readonly PropertyDrawerRegistry m_properties;
    private readonly SerializedPropertyRenderer m_renderer;
    private readonly GameObjectTagCatalog m_tags = new();

    /// <inheritdoc />
    public string workspaceStateId => "scene-inspection";

    /// <summary>
    /// Creates the scene inspection module and its generation-aware drawer registries.
    /// </summary>
    /// <param name="interactions">The active editor interaction entry point supplied to drawers.</param>
    /// <param name="edits">The scene editing service used to record granular property changes.</param>
    /// <param name="assetIcons">The Asset Browser presentation provider used by Asset inspection drawers.</param>
    /// <exception cref="System.ArgumentNullException">
    /// Thrown when <paramref name="interactions"/>, <paramref name="edits"/>,
    /// or <paramref name="assetIcons"/> is <see langword="null"/>.
    /// </exception>
    internal SceneInspectionModule(
        EditorInteractions interactions,
        SceneEdits edits,
        IInspectionIconProvider<AssetFileEntry> assetIcons)
    {
        System.ArgumentNullException.ThrowIfNull(interactions);
        System.ArgumentNullException.ThrowIfNull(edits);
        System.ArgumentNullException.ThrowIfNull(assetIcons);
        var activator = new InspectionDrawerActivator(interactions, edits, assetIcons, m_tags);
        m_inspectors = new InspectionDrawerRegistry(interactions, activator.Create);
        m_properties = new PropertyDrawerRegistry(interactions);
        m_renderer = new SerializedPropertyRenderer(
            m_properties,
            interactions,
            new SceneInspectionPropertyEditService(edits));
    }

    /// <inheritdoc />
    public void CaptureWorkspaceState(EditorWorkspaceStateWriter writer)
        => m_tags.Capture(writer);

    /// <inheritdoc />
    public void RestoreWorkspaceState(EditorWorkspaceStateReader reader)
        => m_tags.Restore(reader);

    internal bool TryResolve(
        EditorContext editorContext,
        object target,
        out IInspectionDrawer? drawer,
        out InspectionDrawContext? drawContext)
        => m_inspectors.TryResolve(
            editorContext,
            target,
            m_renderer,
            out drawer,
            out drawContext);

    /// <inheritdoc />
    public void Dispose()
    {
        m_inspectors.Dispose();
        m_properties.Dispose();
        System.GC.SuppressFinalize(this);
    }
}
