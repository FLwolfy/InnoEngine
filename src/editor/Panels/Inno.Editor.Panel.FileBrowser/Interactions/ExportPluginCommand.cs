using System;
using Inno.Assets;
using Inno.Assets.Core;
using Inno.Assets.File;
using Inno.Assets.Plugins;
using Inno.Editor.Interactions;

namespace Inno.Editor.Panel.FileBrowser;

[EditorAction(FileBrowserInteractionIds.C_EXPORT_PLUGIN, FileBrowserInteractionIds.C_AREA)]
[EditorMenu(FileBrowserInteractionIds.C_AREA, "Export Plugin ZIP", order: 50)]
internal sealed class ExportPluginCommand(PluginExportWindowModule export) : EditorAction<AssetFileEntry>
{
    protected override EditorActionState Query(EditorActionContext<AssetFileEntry> context)
    {
        if (context.target.isReadOnly || context.target.source != AssetSourceId.project)
            return EditorActionState.hidden;
        return AssetManager.TryGetAssetType(context.target.assetPath, out Type? assetType) &&
               assetType == typeof(PluginDefinitionAsset)
            ? EditorActionState.enabled
            : EditorActionState.hidden;
    }

    protected override void Execute(EditorActionContext<AssetFileEntry> context)
    {
        PluginDefinitionAsset definition = AssetManager.Load<PluginDefinitionAsset>(context.target.assetPath);
        export.Open(definition, PluginExportKind.Zip, context.editor.projectDirectory);
    }
}
