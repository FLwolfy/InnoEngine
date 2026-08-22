using Inno.Editor.Core;
using Inno.Editor.Interactions;
using Inno.Editor.Scene;

namespace Inno.Editor.Panel.Inspector;

/// <summary>Owns the scene inspection extension registries for one editor runtime.</summary>
[EditorModule(order: 210)]
public sealed class SceneInspectionModule : EditorModule, System.IDisposable
{
    private readonly InspectorDrawerRegistry m_inspectors;
    private readonly PropertyDrawerRegistry m_properties;
    private readonly SerializedPropertyRenderer m_renderer;
    private readonly SceneEdits m_edits;

    /// <summary>
    /// Creates the scene inspection module and its generation-aware drawer registries.
    /// </summary>
    /// <param name="interactions">The active editor interaction entry point supplied to drawers.</param>
    /// <param name="edits">The scene editing service used to record granular property changes.</param>
    /// <exception cref="System.ArgumentNullException">
    /// Thrown when <paramref name="interactions"/> or <paramref name="edits"/> is
    /// <see langword="null"/>.
    /// </exception>
    public SceneInspectionModule(EditorInteractions interactions, SceneEdits edits)
    {
        System.ArgumentNullException.ThrowIfNull(interactions);
        System.ArgumentNullException.ThrowIfNull(edits);
        m_edits = edits;
        m_inspectors = new InspectorDrawerRegistry(interactions, edits);
        m_properties = new PropertyDrawerRegistry(interactions);
        m_renderer = new SerializedPropertyRenderer(m_properties, interactions, edits);
    }

    internal bool TryResolve(
        EditorContext editorContext,
        object target,
        out IInspectorDrawer? drawer,
        out InspectorDrawContext? drawContext)
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
