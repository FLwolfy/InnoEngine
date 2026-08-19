using Inno.Editor.Assets.AssetEditors;
using Inno.Editor.Core.Commands;
using Inno.Editor.Core.Menus;
using Inno.Editor.Scene.Hierarchy;
using Inno.Editor.Scene.Workspace;
using Inno.Core.Input;
using Inno.Engine.Scene;

namespace Inno.Editor.Scene.Commands;

[EditorAction(EditorActionIds.Delete, priority: 100)]
[EditorMenu(typeof(SceneSurface.HierarchyScene), "Delete", order: 400, separatorBefore: true)]
[EditorShortcut(typeof(HierarchyPanel), KeyCode.Delete)]
internal sealed class DeleteSceneCommand(EditorSceneWorkspace workspace) : EditorAction<GameScene>
{
    protected override EditorActionState Query(EditorActionContext<GameScene> context)
        => context.target is { isLoaded: true, isDestroyed: false } && workspace.scenes.Count > 1
            ? EditorActionState.enabled
            : EditorActionState.disabled;

    protected override void Execute(EditorActionContext<GameScene> context)
    {
        if (!workspace.CloseScene(context.target))
            return;
        if (context.editor.selection.TryGet(out GameScene? selected) && ReferenceEquals(selected, context.target))
            context.editor.selection.Clear();
    }
}
