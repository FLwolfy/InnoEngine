using Inno.Editor.Core;
using Inno.Editor.Scene.Workspace;

namespace Inno.Editor.Scene.Inspection;

/// <summary>Owns the scene inspection extension registries for one editor runtime.</summary>
[EditorModule(order: 210)]
public sealed class SceneInspectionModule : EditorModule, System.IDisposable
{
    private readonly InspectorDrawerRegistry m_inspectors;
    private readonly PropertyDrawerRegistry m_properties;
    private readonly SerializedPropertyRenderer m_renderer;

    /// <summary>Creates the scene inspection module.</summary>
    public SceneInspectionModule(EditorSceneWorkspace workspace)
    {
        System.ArgumentNullException.ThrowIfNull(workspace);
        m_inspectors = new InspectorDrawerRegistry(workspace);
        m_properties = new PropertyDrawerRegistry(workspace);
        m_renderer = new SerializedPropertyRenderer(m_properties);
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
