using System;

using Inno.Assets;
using Inno.Assets.Pipeline;
using Inno.Editor.Interactions;

namespace Inno.Editor.Panel.FileBrowser;

[EditorAction(FileBrowserInteractionIds.C_IMPORT_SAMPLE, FileBrowserInteractionIds.C_AREA)]
[EditorMenu(FileBrowserInteractionIds.C_AREA, "Import Sample", order: 120)]
internal sealed class ImportAssetSampleCommand(AssetEditorModule assets) : EditorAction<AssetFileEntry>
{
    /// <summary>
    /// Determines whether the selected asset sample can be imported into the Project source.
    /// </summary>
    /// <param name="context">
    /// The action context containing the selected indexed asset entry.
    /// </param>
    /// <returns>
    /// The visibility and availability state for importing the selected sample.
    /// </returns>
    protected override EditorActionState Query(EditorActionContext<AssetFileEntry> context)
    {
        if (!context.target.isSample)
            return EditorActionState.hidden;
        try
        {
            AssetPath target = AssetPath.Project(AssetSample.GetImportName(context.target.assetPath));
            return assets.pipeline.TryGetFileSystemEntry(target, out _)
                ? EditorActionState.disabled
                : EditorActionState.enabled;
        }
        catch (ArgumentException)
        {
            return EditorActionState.disabled;
        }
    }

    /// <summary>
    /// Imports the selected sample into the Project source and records the operation in editor history.
    /// </summary>
    /// <param name="context">
    /// The action context containing the selected sample and history transaction.
    /// </param>
    protected override void Execute(EditorActionContext<AssetFileEntry> context)
    {
        AssetPath imported = assets.pipeline.ImportSample(context.target.assetPath);
        byte[] archive = AssetSourceArchive.Capture(
            assets.pipeline,
            imported.localPath,
            out bool isDirectory);
        var data = new AssetHistoryData(
            AssetHistoryOperationKind.CreateAsset,
            imported.ToString(),
            string.Empty,
            isDirectory,
            archive);
        try
        {
            context.history.RecordApplied(
                "Import Sample",
                new EditorHistoryChange(
                    AssetHistoryKinds.SourceOperation,
                    EditorHistoryPayload.FromBytes(data.Encode())));
        }
        catch (Exception failure)
        {
            try
            {
                assets.pipeline.Delete(imported);
            }
            catch (Exception rollbackException)
            {
                throw new AggregateException(
                    "The sample import could not be recorded and its compensation also failed.",
                    failure,
                    rollbackException);
            }
            throw;
        }
        assets.SelectPath(imported.ToString());
    }
}
