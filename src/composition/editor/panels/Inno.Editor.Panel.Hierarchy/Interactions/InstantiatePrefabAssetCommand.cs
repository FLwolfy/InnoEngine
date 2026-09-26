using System;

using Inno.Assets.Pipeline;
using Inno.Editor.Interactions;
using Inno.Editor.Scene;

namespace Inno.Editor.Panel.Hierarchy;

[EditorAction(HierarchyInteractionIds.C_INSTANTIATE_PREFAB_ASSET, priority: 200)]
[EditorMenu(HierarchyInteractionIds.C_FILE_BROWSER_AREA, "Instantiate", order: 40)]
internal sealed class InstantiatePrefabAssetCommand(IEditorSceneWorkspace workspace) : EditorAction<AssetFileEntry>
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
protected override EditorActionState Query(EditorActionContext<AssetFileEntry> context)
        => string.Equals(context.target.extension, ".iprefab", StringComparison.OrdinalIgnoreCase)
            ? workspace.canPersist && workspace.activeScene is { } scene && workspace.CanEdit(scene)
                ? EditorActionState.enabled
                : EditorActionState.disabled
            : EditorActionState.hidden;

    /// <summary>
    /// Executes the prepared operation and publishes only a completed result.
    /// </summary>
    /// <param name="context">
    /// The operation scope that provides state, services, and ownership boundaries.
    /// </param>
protected override void Execute(EditorActionContext<AssetFileEntry> context)
        => _ = context.interactions
            .For(HierarchyInteractionIds.C_FILE_BROWSER_AREA, context.target)
            .Execute(HierarchyInteractionIds.C_OPEN);
}
