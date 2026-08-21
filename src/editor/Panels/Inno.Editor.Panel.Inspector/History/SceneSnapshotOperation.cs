using System;
using System.Diagnostics;
using System.Linq;

using Inno.Core.Identity;
using Inno.Core.Serialization;
using Inno.Editor.Interactions;
using Inno.Engine.Scene;

namespace Inno.Editor.Panel.Inspector;

internal sealed class SceneSnapshotOperation : EditorHistoryOperation
{
    private const double C_MERGE_WINDOW_SECONDS = 1.0;

    private readonly string m_name;
    private readonly Guid m_sceneId;
    private readonly byte[] m_before;
    private byte[] m_after;
    private readonly EditorInteractions m_interactions;
    private readonly Guid? m_selectionId;
    private readonly object? m_mergeKey;
    private long m_lastEditTimestamp;

    private SceneSnapshotOperation(
        string name,
        GameScene scene,
        byte[] before,
        byte[] after,
        EditorInteractions interactions,
        Guid? selectionId,
        object? mergeKey)
    {
        m_name = name;
        m_sceneId = scene.identity.persistentId;
        m_before = before;
        m_after = after;
        m_interactions = interactions;
        m_selectionId = selectionId;
        m_mergeKey = mergeKey;
        m_lastEditTimestamp = Stopwatch.GetTimestamp();
    }

    public override string name => m_name;

    public override bool canUndo => ResolveScene() is { isLoaded: true, isDestroyed: false };

    public override bool canRedo => canUndo;

    internal static void Execute(
        EditorActionContext context,
        string name,
        GameScene scene,
        Action mutation)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(mutation);
        byte[] before = SerializationManager.Serialize(scene);
        Guid? selectionId = (context.interactions.selection.selectedTarget as EngineObject)?.identity.persistentId;
        mutation();
        byte[] after = SerializationManager.Serialize(scene);
        context.history.RecordApplied(new SceneSnapshotOperation(
            name,
            scene,
            before,
            after,
            context.interactions,
            selectionId,
            mergeKey: null));
    }

    internal static void Execute(
        EditorInteractions interactions,
        string name,
        GameScene scene,
        Action mutation,
        object? mergeKey = null)
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
            selectionId,
            mergeKey));
    }

    protected override EditorHistoryResult Undo() => Restore(m_before);

    protected override EditorHistoryResult Redo() => Restore(m_after);

    protected override bool TryMerge(EditorHistoryOperation newer)
    {
        if (m_mergeKey is null ||
            newer is not SceneSnapshotOperation candidate ||
            m_sceneId != candidate.m_sceneId ||
            !Equals(m_mergeKey, candidate.m_mergeKey) ||
            Stopwatch.GetElapsedTime(m_lastEditTimestamp, candidate.m_lastEditTimestamp).TotalSeconds >
            C_MERGE_WINDOW_SECONDS)
        {
            return false;
        }
        m_after = candidate.m_after;
        m_lastEditTimestamp = candidate.m_lastEditTimestamp;
        return true;
    }

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
        _ = m_interactions.For(m_interactions.focusedArea, target).Select();
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
