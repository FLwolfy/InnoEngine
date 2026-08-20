using Inno.Assets;
using Inno.Assets.File;

using Inno.Editor.Core;
using Inno.Editor.Interactions;
using Inno.Engine.Scene;

namespace Inno.Editor.Panel.Hierarchy;

[EditorDrop("panel/asset.file-browser")]
internal sealed class SavePrefabAssetDropHandler
    : EditorDrop<GameObject, string>
{
    private readonly EditorSceneWorkspace m_workspace;
    internal SavePrefabAssetDropHandler(EditorSceneWorkspace workspace)
    {
        m_workspace = workspace;
    }

    protected override EditorDropStatus Query(
        EditorDropContext<GameObject, string> context)
        => context.source.isRuntimeValid
            ? EditorDropStatus.Accept()
            : EditorDropStatus.rejected;

    protected override EditorDropResult Drop(
        EditorDropContext<GameObject, string> context)
    {
        string path = m_workspace.SavePrefab(context.source, context.target);
        if (!AssetManager.TryGetFileSystemEntry(path, out AssetFileEntry selection))
            return EditorDropResult.rejected;
        _ = context.interactions.For(context.area, selection).Select();
        return EditorDropResult.Accepted(selection);
    }
}
