using Inno.Editor.Core;
using Inno.Editor.Interactions;

namespace Inno.Editor.Panel.Inspector;

/// <summary>Owns the scene inspection extension registries for one editor runtime.</summary>
[EditorModule(order: 210)]
public sealed class SceneInspectionModule : EditorModule, System.IDisposable
{
    private readonly InspectorDrawerRegistry m_inspectors;
    private readonly PropertyDrawerRegistry m_properties;
    private readonly SerializedPropertyRenderer m_renderer;

    /// <summary>
    /// Creates the scene inspection module and its generation-aware drawer registries.
    /// </summary>
    /// <param name="interactions">The active editor interaction entry point supplied to drawers.</param>
    /// <exception cref="System.ArgumentNullException">Thrown when <paramref name="interactions"/> is <see langword="null"/>.</exception>
    public SceneInspectionModule(EditorInteractions interactions)
    {
        System.ArgumentNullException.ThrowIfNull(interactions);
        m_inspectors = new InspectorDrawerRegistry(interactions);
        m_properties = new PropertyDrawerRegistry(interactions);
        m_renderer = new SerializedPropertyRenderer(m_properties, interactions);
    }

    internal bool Draw(EditorContext context, object target)
        => m_inspectors.Draw(context, target, m_renderer);

    /// <inheritdoc />
    public void Dispose()
    {
        m_inspectors.Dispose();
        m_properties.Dispose();
        System.GC.SuppressFinalize(this);
    }
}
