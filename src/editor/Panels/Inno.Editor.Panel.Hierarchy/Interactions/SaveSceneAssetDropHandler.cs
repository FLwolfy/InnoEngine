using Inno.Assets;
using Inno.Assets.File;

using Inno.Editor.Core;
using Inno.Editor.Interactions;
using Inno.Editor.Scene;
using Inno.Engine.Scene;

namespace Inno.Editor.Panel.Hierarchy;

[EditorDrop(HierarchyInteractionIds.C_FILE_BROWSER_AREA)]
internal sealed class SaveSceneAssetDropHandler
    : EditorDrop<GameScene, string>
{
    private readonly IEditorSceneWorkspace m_workspace;
    internal SaveSceneAssetDropHandler(IEditorSceneWorkspace workspace)
    {
        m_workspace = workspace;
    }

    protected override EditorDropStatus Query(
        EditorDropContext<GameScene, string> context)
        => context.source.isLoaded
            ? EditorDropStatus.Accept()
            : EditorDropStatus.rejected;

    protected override EditorDropResult Drop(
        EditorDropContext<GameScene, string> context)
    {
        string path = m_workspace.SaveToDirectory(context.source, context.target);
        if (!AssetManager.TryGetFileSystemEntry(path, out AssetFileEntry selection))
            return EditorDropResult.rejected;
        _ = context.interactions.For(context.area, selection).Select();
        return EditorDropResult.Accepted(selection);
    }
}
