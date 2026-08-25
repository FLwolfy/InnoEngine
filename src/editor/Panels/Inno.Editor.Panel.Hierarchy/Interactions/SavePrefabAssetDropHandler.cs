using Inno.Assets;
using Inno.Assets.File;

using Inno.Editor.Core;
using Inno.Editor.Interactions;
using Inno.Editor.Scene;
using Inno.Engine.Scene;

namespace Inno.Editor.Panel.Hierarchy;

[EditorDrop(HierarchyInteractionIds.C_FILE_BROWSER_AREA)]
internal sealed class SavePrefabAssetDropHandler
    : EditorDrop<GameObject, string>
{
    private readonly IEditorSceneWorkspace m_workspace;
    internal SavePrefabAssetDropHandler(IEditorSceneWorkspace workspace)
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
