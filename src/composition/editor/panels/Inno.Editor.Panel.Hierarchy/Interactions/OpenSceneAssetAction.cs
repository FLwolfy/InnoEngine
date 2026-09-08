using Inno.Editor.Interactions;
using Inno.Editor.Scene;
using Inno.Scene;

namespace Inno.Editor.Panel.Hierarchy;

[EditorAction(HierarchyInteractionIds.C_OPEN, priority: 200)]
internal sealed class OpenSceneAssetAction(IEditorSceneWorkspace workspace) : EditorAction<SceneAsset, string>
{
    /// <summary>
    /// Executes the prepared operation and publishes only a completed result.
    /// </summary>
    /// <param name="context">
    /// The operation scope that provides state, services, and ownership boundaries.
    /// </param>
    protected override void Execute(EditorActionContext<SceneAsset, string> context)
    {
        GameScene scene = workspace.Open(context.argument);
        _ = context.interactions.For(HierarchyInteractionIds.C_AREA, scene).Select();
    }
}
