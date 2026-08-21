using System;
using System.Linq;

using Inno.Core.Identity;
using Inno.Core.Serialization;
using Inno.Editor.Interactions;
using Inno.Engine.Scene;

namespace Inno.Editor.Panel.Hierarchy;

internal sealed class SceneSnapshotOperation : EditorHistoryOperation
{
    private readonly string m_name;
    private readonly Guid m_sceneId;
    private readonly byte[] m_before;
    private readonly byte[] m_after;
    private readonly EditorInteractions m_interactions;
    private readonly Guid? m_selectionId;

    private SceneSnapshotOperation(
        string name,
        GameScene scene,
        byte[] before,
        byte[] after,
        EditorInteractions interactions,
        Guid? selectionId)
    {
        m_name = name;
        m_sceneId = scene.identity.persistentId;
        m_before = before;
        m_after = after;
        m_interactions = interactions;
        m_selectionId = selectionId;
    }

    public override string name => m_name;

    public override bool canUndo => ResolveScene() is { isLoaded: true, isDestroyed: false };

    public override bool canRedo => canUndo;

    internal static void Execute(
        EditorInteractions interactions,
        string name,
        GameScene scene,
        Action mutation)
    {
        ArgumentNullException.ThrowIfNull(interactions);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(mutation);
        byte[] before = SerializationManager.Serialize(scene);
        Guid? selectionId = (interactions.selection.selectedTarget as EngineObject)?.identity.persistentId;
        mutation();
        byte[] after = SerializationManager.Serialize(scene);
        interactions.history.RecordApplied(new SceneSnapshotOperation(
            name,
            scene,
            before,
            after,
            interactions,
            selectionId));
    }

    protected override EditorHistoryResult Undo() => Restore(m_before);

    protected override EditorHistoryResult Redo() => Restore(m_after);

    private EditorHistoryResult Restore(byte[] snapshot)
    {
        GameScene? current = ResolveScene();
        if (current is not { isLoaded: true, isDestroyed: false })
            return EditorHistoryResult.Failure($"Scene '{m_sceneId}' is no longer loaded.");

        byte[] rollback = SerializationManager.Serialize(current);
        try
        {
            SerializationManager.Restore(current, snapshot);
        }
        catch (Exception exception)
        {
            SerializationManager.Restore(current, rollback);
            return EditorHistoryResult.Failure(exception.Message);
        }
        object target = ResolveSelection() ?? current;
        _ = m_interactions.For(HierarchyAreas.Hierarchy, target).Select();
        return EditorHistoryResult.Success();
    }

    private EngineObject? ResolveSelection()
    {
        if (m_selectionId is not Guid id)
            return null;
        return IdentityManager.Get<EngineObject>(id);
    }

    private GameScene? ResolveScene() => IdentityManager.Get<GameScene>(m_sceneId);

}
