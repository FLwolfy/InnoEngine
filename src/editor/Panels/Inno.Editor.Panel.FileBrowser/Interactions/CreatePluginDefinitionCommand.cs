using System;
using System.Linq;
using Inno.Assets;
using Inno.Assets.Core;
using Inno.Assets.File;
using Inno.Assets.Plugins;
using Inno.Editor.Interactions;

namespace Inno.Editor.Panel.FileBrowser;

[EditorAction(FileBrowserInteractionIds.C_CREATE_PLUGIN_DEFINITION, FileBrowserInteractionIds.C_AREA)]
[EditorMenu(FileBrowserInteractionIds.C_AREA, "Create/Plugin Definition", order: 200)]
internal sealed class CreatePluginDefinitionCommand : EditorAction<string>
{
    protected override EditorActionState Query(EditorActionContext<string> context)
        => AssetManager.isInitialized && IsWritableDirectory(context.target)
            ? EditorActionState.enabled
            : EditorActionState.disabled;

    protected override void Execute(EditorActionContext<string> context)
    {
        string parent = AssetPath.Parse(context.target).localPath;
        string candidate = Combine(parent, "New Plugin.iplugin");
        int suffix = 1;
        while (AssetManager.TryGetFileSystemEntry(candidate, out _))
            candidate = Combine(parent, $"New Plugin {suffix++}.iplugin");

        var definition = new PluginDefinitionAsset
        {
            pluginId = "new.plugin",
            displayName = "New Plugin"
        };
        if (!AssetManager.Save(candidate, definition))
            throw new InvalidOperationException("No importer accepted the Plugin definition Asset.");
        byte[] archive = AssetSourceArchive.Capture(candidate, out bool isDirectory);
        var data = new AssetHistoryData(
            AssetHistoryOperationKind.CreateAsset,
            candidate,
            string.Empty,
            isDirectory,
            archive);
        try
        {
            context.history.RecordApplied(
                "Create Plugin Definition",
                new EditorHistoryChange(
                    AssetHistoryKinds.SourceOperation,
                    EditorHistoryPayload.FromBytes(data.Encode())));
        }
        catch
        {
            AssetManager.Delete(candidate);
            throw;
        }
        if (AssetManager.TryGetFileSystemEntry(candidate, out AssetFileEntry created))
            _ = context.interactions.For(FileBrowserInteractionIds.C_AREA, created).Select();
    }

    private static bool IsWritableDirectory(string relativePath)
    {
        AssetPath path = AssetPath.Parse(relativePath);
        AssetSourceMount? mount = AssetManager.sourceMounts.FirstOrDefault(candidate => candidate.id == path.source);
        if (mount is null || mount.isReadOnly)
            return false;
        return string.IsNullOrEmpty(path.localPath) ||
               AssetManager.TryGetFileSystemEntry(path.ToString(), out AssetFileEntry entry) && entry.isDirectory;
    }

    private static string Combine(string parent, string name)
        => string.IsNullOrEmpty(parent) ? name : $"{parent}/{name}";
}
