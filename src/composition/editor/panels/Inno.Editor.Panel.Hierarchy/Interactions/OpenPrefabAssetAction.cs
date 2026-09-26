using Inno.Editor.Interactions;
using Inno.Editor.Scene;
using Inno.Scene;

namespace Inno.Editor.Panel.Hierarchy;

[EditorAction(HierarchyInteractionIds.C_OPEN, priority: 200)]
internal sealed class OpenPrefabAssetAction(
    IEditorSceneWorkspace workspace,
    SceneEdits edits) : EditorAction<PrefabAsset, string>
{
    /// <summary>
    /// Evaluates whether the requested change can be applied to the current generation.
    /// </summary>
    /// <param name="context">
    /// The operation scope that provides state, services, and ownership boundaries.
    /// </param>
    /// <returns>
    /// The validated editor action state that represents the completed operation.
    /// </returns>
protected override EditorActionState Query(EditorActionContext<PrefabAsset, string> context)
        => workspace.canPersist && workspace.activeScene is not null
            ? EditorActionState.enabled
            : EditorActionState.disabled;

    /// <summary>
    /// Applies the editor action to the supplied interaction context.
    /// </summary>
    /// <param name="context">
    /// The operation scope that provides state, services, and ownership boundaries.
    /// </param>
protected override void Execute(EditorActionContext<PrefabAsset, string> context)
    {
        GameScene? scene = workspace.activeScene;
        if (scene is null)
            return;
        GameObject instance = edits.InstantiatePrefab(context.target, scene);
        _ = context.interactions.For(HierarchyInteractionIds.C_AREA, instance).Select();
    }
}
