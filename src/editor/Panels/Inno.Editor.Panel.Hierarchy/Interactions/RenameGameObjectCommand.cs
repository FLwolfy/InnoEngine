using System;

using Inno.Editor.Interactions;
using Inno.Editor.Scene;
using Inno.Core.Input;
using Inno.Engine.Scene;
using Inno.Editor.ImGui.ImGuiWidget;
using EditorWidget = Inno.Editor.ImGui.ImGuiWidget.ImGuiWidget;

namespace Inno.Editor.Panel.Hierarchy;

[EditorAction("hierarchy/rename-game-object", priority: 100)]
[EditorMenu("panel/scene.hierarchy", "Rename", order: 200)]
[EditorShortcut("panel/scene.hierarchy", KeyCode.F2)]
internal sealed class RenameGameObjectCommand(SceneEdits edits) : EditorAction<GameObject>
{
    private GameObject? m_gameObject;
    private string m_buffer = string.Empty;
    private bool m_requestFocus;
    private string m_originalName = string.Empty;

    protected override EditorActionState Query(EditorActionContext<GameObject> context)
        => context.target.isRuntimeValid
            ? EditorActionState.enabled
            : EditorActionState.hidden;

    protected override void Execute(EditorActionContext<GameObject> context)
    {
        m_gameObject = context.target;
        m_buffer = context.target.name;
        m_originalName = context.target.name;
        m_requestFocus = true;
        Activate(context);
    }

    protected override bool Present(EditorActionContext<GameObject> context)
    {
        if (m_gameObject is null ||
            !context.TryGetArgument(out InlineRenamePresentation? presentation) ||
            presentation is null)
        {
            return false;
        }

        InlineRenameResult result = EditorWidget.InlineRename(
            presentation.id,
            ref m_buffer,
            ref m_requestFocus,
            presentation.rowHeight,
            presentation.bufferSize,
            presentation.width);
        if (result == InlineRenameResult.Cancel)
        {
            Cancel();
            return true;
        }
        if (result == InlineRenameResult.FocusLost)
        {
            CommitName();
            return true;
        }
        if (result != InlineRenameResult.Commit)
            return true;
        CommitName();
        return true;
    }

    protected override void OnCompleted() => ClearState();

    protected override void OnCancelled() => ClearState();

    protected override void OnPresentationLost()
    {
        if (m_gameObject is null || !m_gameObject.isRuntimeValid)
        {
            Cancel();
            return;
        }
        CommitName();
    }

    private void CommitName()
    {
        if (m_gameObject is null)
            return;
        string name = string.IsNullOrWhiteSpace(m_buffer) ? "GameObject" : m_buffer.Trim();
        if (!string.Equals(m_originalName, name, System.StringComparison.Ordinal))
            edits.RenameGameObject(m_gameObject, name);
        Complete();
    }

    private void ClearState()
    {
        m_gameObject = null;
        m_buffer = string.Empty;
        m_requestFocus = false;
        m_originalName = string.Empty;
    }
}
