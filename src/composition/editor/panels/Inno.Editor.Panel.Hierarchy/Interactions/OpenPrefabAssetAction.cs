using Inno.Editor.Interactions;
using Inno.Editor.Scene;
using Inno.Scene;

namespace Inno.Editor.Panel.Hierarchy;

[EditorAction(HierarchyInteractionIds.C_OPEN, priority: 200)]
internal sealed class OpenPrefabAssetAction(
    IEditorSceneWorkspace workspace,
    SceneEdits edits) : EditorAction<PrefabAsset, string>
{
    protected override EditorActionState Query(EditorActionContext<PrefabAsset, string> context)
        => workspace.canPersist && workspace.activeScene is not null
            ? EditorActionState.enabled
            : EditorActionState.disabled;

    protected override void Execute(EditorActionContext<PrefabAsset, string> context)
    {
        GameScene? scene = workspace.activeScene;
        if (scene is null)
            return;
        GameObject instance = edits.InstantiatePrefab(context.target, scene);
        _ = context.interactions.For(HierarchyInteractionIds.C_AREA, instance).Select();
    }
}
