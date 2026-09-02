using System;

using Inno.Core.Input;
using Inno.Editor.Interactions;
using Inno.Editor.Scene;
using Inno.Scene;
using Inno.Editor.ImGui.ImGuiWidget;
using EditorWidget = Inno.Editor.ImGui.ImGuiWidget.ImGuiWidget;

namespace Inno.Editor.Panel.Hierarchy;

[EditorAction(HierarchyInteractionIds.C_RENAME, priority: 100)]
[EditorMenu(HierarchyInteractionIds.C_AREA, "Rename", order: 200)]
[EditorShortcut(HierarchyInteractionIds.C_AREA, KeyCode.F2)]
internal sealed class RenameHierarchyTargetCommand(SceneEdits edits) :
    EditorPresentationAction<EngineObject, InlineRenamePresentation>
{
    private EngineObject? m_target;
    private string m_buffer = string.Empty;
    private bool m_requestFocus;
    private string m_originalName = string.Empty;

    /// <summary>
    /// Evaluates whether the requested change can be applied to the current generation.
    /// </summary>
    /// <param name="context">
    /// The operation scope that provides state, services, and ownership boundaries.
    /// </param>
    /// <returns>
    /// The validated editor action state that represents the completed operation.
    /// </returns>
    protected override EditorActionState Query(EditorActionContext<EngineObject> context)
        => IsAvailable(context.target)
            ? EditorActionState.enabled
            : EditorActionState.hidden;

    /// <summary>
    /// Executes the prepared operation and publishes only a completed result.
    /// </summary>
    /// <param name="context">
    /// The operation scope that provides state, services, and ownership boundaries.
    /// </param>
    protected override void Execute(EditorActionContext<EngineObject> context)
    {
        if (!TryGetName(context.target, out string name))
            return;
        Activate(context);
        m_target = context.target;
        m_buffer = name;
        m_originalName = name;
        m_requestFocus = true;
    }

    /// <summary>
    /// Presents this action through the current editor interaction surface.
    /// </summary>
    /// <param name="context">
    /// The operation scope that provides state, services, and ownership boundaries.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the requested condition is satisfied; otherwise, <see langword="false"/>.
    /// </returns>
    protected override bool Present(EditorActionContext<EngineObject, InlineRenamePresentation> context)
    {
        if (m_target is null)
            return false;
        InlineRenamePresentation presentation = context.argument;
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
        if (result is InlineRenameResult.FocusLost or InlineRenameResult.Commit)
            CommitName();
        return true;
    }

    /// <summary>
    /// Commits the interaction after editing completes successfully.
    /// </summary>
    protected override void OnCompleted() => ClearState();

    /// <summary>
    /// Cancels the interaction without committing its pending value.
    /// </summary>
    protected override void OnCancelled() => ClearState();

    /// <summary>
    /// Cancels pending presentation state when its editor surface disappears.
    /// </summary>
    protected override void OnPresentationLost()
    {
        if (m_target is null || !IsAvailable(m_target))
        {
            Cancel();
            return;
        }
        CommitName();
    }

    private void CommitName()
    {
        if (m_target is null)
            return;
        string fallback = m_target is GameScene ? "Scene" : "GameObject";
        string name = string.IsNullOrWhiteSpace(m_buffer) ? fallback : m_buffer.Trim();
        if (!string.Equals(m_originalName, name, StringComparison.Ordinal))
        {
            switch (m_target)
            {
                case GameScene scene:
                    edits.RenameScene(scene, name);
                    break;
                case GameObject gameObject:
                    edits.RenameGameObject(gameObject, name);
                    break;
            }
        }
        Complete();
    }

    private void ClearState()
    {
        m_target = null;
        m_buffer = string.Empty;
        m_requestFocus = false;
        m_originalName = string.Empty;
    }

    private static bool IsAvailable(EngineObject target)
        => target switch
        {
            GameScene scene => scene.isLoaded && !scene.isDestroyed,
            GameObject gameObject => gameObject.isRuntimeValid,
            _ => false
        };

    private static bool TryGetName(EngineObject target, out string name)
    {
        name = target switch
        {
            GameScene scene when IsAvailable(scene) => scene.name,
            GameObject gameObject when IsAvailable(gameObject) => gameObject.name,
            _ => string.Empty
        };
        return name.Length > 0 || IsAvailable(target);
    }
}
