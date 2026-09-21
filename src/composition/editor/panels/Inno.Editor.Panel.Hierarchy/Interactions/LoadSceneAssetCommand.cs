using System;

using Inno.Assets.Pipeline;
using Inno.Editor.Interactions;
using Inno.Editor.Scene;

namespace Inno.Editor.Panel.Hierarchy;

[EditorAction(HierarchyInteractionIds.C_LOAD_SCENE_ASSET, priority: 200)]
[EditorMenu(HierarchyInteractionIds.C_FILE_BROWSER_AREA, "Load", order: 40)]
internal sealed class LoadSceneAssetCommand(IEditorSceneWorkspace workspace) : EditorAction<AssetFileEntry>
{
    protected override EditorActionState Query(EditorActionContext<AssetFileEntry> context)
        => string.Equals(context.target.extension, ".iscene", StringComparison.OrdinalIgnoreCase)
            ? workspace.canPersist
                ? EditorActionState.enabled
                : EditorActionState.disabled
            : EditorActionState.hidden;

    protected override void Execute(EditorActionContext<AssetFileEntry> context)
        => _ = context.interactions
            .For(HierarchyInteractionIds.C_FILE_BROWSER_AREA, context.target)
            .Execute(HierarchyInteractionIds.C_OPEN);
}
