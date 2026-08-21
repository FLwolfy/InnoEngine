using System;

using Inno.Editor.Interactions;

namespace Inno.Editor.Panel.Hierarchy;

internal sealed class SceneWorkspaceOperation : EditorHistoryOperation
{
    private readonly string m_name;
    private readonly EditorSceneWorkspace m_workspace;
    private readonly EditorSceneWorkspace.WorkspaceSnapshot m_before;
    private readonly EditorSceneWorkspace.WorkspaceSnapshot m_after;

    private SceneWorkspaceOperation(
        string name,
        EditorSceneWorkspace workspace,
        EditorSceneWorkspace.WorkspaceSnapshot before,
        EditorSceneWorkspace.WorkspaceSnapshot after)
    {
        m_name = name;
        m_workspace = workspace;
        m_before = before;
        m_after = after;
    }

    public override string name => m_name;

    internal static void Execute(
        EditorActionContext context,
        string name,
        EditorSceneWorkspace workspace,
        Action mutation)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(mutation);
        EditorSceneWorkspace.WorkspaceSnapshot before = workspace.CaptureSnapshot();
        mutation();
        EditorSceneWorkspace.WorkspaceSnapshot after = workspace.CaptureSnapshot();
        context.history.RecordApplied(new SceneWorkspaceOperation(name, workspace, before, after));
    }

    internal static void Execute(
        EditorInteractions interactions,
        string name,
        EditorSceneWorkspace workspace,
        Action mutation)
    {
        ArgumentNullException.ThrowIfNull(interactions);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(mutation);
        EditorSceneWorkspace.WorkspaceSnapshot before = workspace.CaptureSnapshot();
        mutation();
        EditorSceneWorkspace.WorkspaceSnapshot after = workspace.CaptureSnapshot();
        interactions.history.RecordApplied(new SceneWorkspaceOperation(name, workspace, before, after));
    }

    protected override EditorHistoryResult Undo() => m_workspace.RestoreSnapshot(m_before);

    protected override EditorHistoryResult Redo() => m_workspace.RestoreSnapshot(m_after);
}
