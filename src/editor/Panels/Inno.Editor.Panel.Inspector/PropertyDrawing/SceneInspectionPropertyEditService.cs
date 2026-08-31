using System;

using Inno.Editor.Inspection;
using Inno.Editor.Scene;
using Inno.Engine.Scene;

namespace Inno.Editor.Panel.Inspector;

/// <summary>
/// Adapts generic inspection property edits to compact Scene history records.
/// </summary>
internal sealed class SceneInspectionPropertyEditService : IInspectionPropertyEditService
{
    private readonly SceneEdits m_edits;

    /// <summary>
    /// Creates the Scene property edit adapter.
    /// </summary>
    /// <param name="edits">The Scene editing service that owns Undo/Redo recording.</param>
    internal SceneInspectionPropertyEditService(SceneEdits edits)
    {
        m_edits = edits ?? throw new ArgumentNullException(nameof(edits));
    }

    /// <inheritdoc />
    public bool ChangeProperty(
        object owner,
        string propertyName,
        Action mutation,
        string historyName)
    {
        ArgumentNullException.ThrowIfNull(owner);
        EngineObject sceneObject = owner as EngineObject ?? throw new ArgumentException(
            $"Scene property edits require an '{typeof(EngineObject).FullName}' owner.",
            nameof(owner));
        return m_edits.ChangeProperty(
            sceneObject,
            propertyName,
            mutation,
            historyName,
            $"scene-property:{sceneObject.identity.persistentId:N}:{propertyName}");
    }
}
