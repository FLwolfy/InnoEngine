using Inno.Editor.Assets;
using Inno.Editor.Assets.AssetEditors;
using Inno.Editor.Core.Commands;
using Inno.Editor.Scene.Workspace;
using Inno.Engine.Scene;
using Inno.Engine.Scene.Assets;

namespace Inno.Editor.Scene.Commands;

[EditorAction(EditorActionIds.Open, typeof(AssetSurface.Browser), priority: 200)]
internal sealed class OpenSceneAssetAction(EditorSceneWorkspace workspace) : EditorAction<SceneAsset>
{
    protected override void Execute(EditorActionContext<SceneAsset> context)
    {
        if (context.argument is not AssetEditorContext assetContext)
            return;
        GameScene scene = workspace.OpenScene(assetContext.relativePath);
        context.editor.selection.Select(scene);
    }
}
