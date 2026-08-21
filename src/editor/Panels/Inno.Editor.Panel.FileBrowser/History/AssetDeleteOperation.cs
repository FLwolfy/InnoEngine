using System;
using System.IO;

using Inno.Assets;
using Inno.Editor.Interactions;

namespace Inno.Editor.Panel.FileBrowser;

internal sealed class AssetDeleteOperation : EditorHistoryOperation
{
    private readonly string m_relativePath;
    private readonly string m_archiveRoot;
    private readonly bool m_isDirectory;

    private AssetDeleteOperation(string relativePath, string archiveRoot, bool isDirectory)
    {
        m_relativePath = relativePath;
        m_archiveRoot = archiveRoot;
        m_isDirectory = isDirectory;
    }

    public override string name => "Delete Asset";

    public override bool canUndo => Directory.Exists(m_archiveRoot) && !SourceExists();

    public override bool canRedo => SourceExists();

    internal static bool Execute(EditorActionContext context, AssetEditorModule assets, AssetEditorContext asset)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(assets);
        ArgumentNullException.ThrowIfNull(asset);
        string source = Path.Combine(AssetManager.assetRoot, asset.relativePath);
        bool isDirectory = Directory.Exists(source);
        string archive = Path.Combine(
            context.editor.projectDirectory,
            "Library",
            "Editor",
            "Undo",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(archive);
        try
        {
            CopySource(source, Path.Combine(archive, "source"), isDirectory);
            string meta = source + ".imeta";
            if (File.Exists(meta))
                File.Copy(meta, Path.Combine(archive, "source.imeta"));
            if (!assets.Delete(asset))
            {
                Directory.Delete(archive, recursive: true);
                return false;
            }
            context.history.RecordApplied(new AssetDeleteOperation(asset.relativePath, archive, isDirectory));
            return true;
        }
        catch
        {
            if (Directory.Exists(archive))
                Directory.Delete(archive, recursive: true);
            throw;
        }
    }

    protected override EditorHistoryResult Undo()
    {
        if (SourceExists())
            return EditorHistoryResult.Failure($"Asset '{m_relativePath}' already exists.");
        string target = Path.Combine(AssetManager.assetRoot, m_relativePath);
        string? parent = Path.GetDirectoryName(target);
        if (!string.IsNullOrEmpty(parent))
            Directory.CreateDirectory(parent);
        CopySource(Path.Combine(m_archiveRoot, "source"), target, m_isDirectory);
        string archivedMeta = Path.Combine(m_archiveRoot, "source.imeta");
        if (File.Exists(archivedMeta))
            File.Copy(archivedMeta, target + ".imeta");
        AssetManager.Rescan();
        AssetManager.WaitForIdle();
        return EditorHistoryResult.Success();
    }

    protected override EditorHistoryResult Redo()
    {
        if (!SourceExists())
            return EditorHistoryResult.Failure($"Asset '{m_relativePath}' no longer exists.");
        AssetManager.Delete(m_relativePath);
        return EditorHistoryResult.Success();
    }

    protected override void Dispose(bool disposing)
    {
        if (!disposing || !Directory.Exists(m_archiveRoot))
            return;
        try
        {
            Directory.Delete(m_archiveRoot, recursive: true);
        }
        catch
        {
            // The cache remains recoverable and will be removed with the project's Library directory.
        }
    }

    private bool SourceExists()
    {
        string source = Path.Combine(AssetManager.assetRoot, m_relativePath);
        return Directory.Exists(source) || File.Exists(source);
    }

    private static void CopySource(string source, string target, bool isDirectory)
    {
        if (!isDirectory)
        {
            File.Copy(source, target);
            return;
        }
        Directory.CreateDirectory(target);
        foreach (string directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(source, directory);
            Directory.CreateDirectory(Path.Combine(target, relative));
        }
        foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(source, file);
            string destination = Path.Combine(target, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination);
        }
    }
}
