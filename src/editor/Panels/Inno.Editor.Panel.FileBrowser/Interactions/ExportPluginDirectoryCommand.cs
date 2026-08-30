using System;
using System.IO;
using Inno.Assets;
using Inno.Assets.Core;
using Inno.Assets.File;
using Inno.Assets.Plugins;
using Inno.Core.Logging;
using Inno.Editor.Interactions;

namespace Inno.Editor.Panel.FileBrowser;

[EditorAction(FileBrowserInteractionIds.C_EXPORT_PLUGIN_DIRECTORY, FileBrowserInteractionIds.C_AREA)]
[EditorMenu(FileBrowserInteractionIds.C_AREA, "Export Plugin Folder", order: 51)]
internal sealed class ExportPluginDirectoryCommand : EditorAction<AssetFileEntry>
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
        string projectRoot = Path.GetDirectoryName(AssetManager.assetRoot)
            ?? throw new InvalidOperationException("The project Assets root has no parent directory.");
        string output = Path.Combine(projectRoot, "Plugins", definition.pluginId);
        string hash = PluginExportService.ExportDirectory(definition, output);
        _ = PluginManager.Refresh();
        Log.Info("Exported editable Plugin '{0}' to '{1}' ({2}).", definition.pluginId, output, hash);
    }
}
