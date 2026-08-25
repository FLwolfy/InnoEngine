using System;
using System.IO;

using Inno.Assets;
using Inno.Assets.Core;
using Inno.Assets.File;
using Inno.Assets.Types;
using Inno.Core.Assemblies;
using Inno.Core.Identity;
using Inno.Core.Reflection;
using Inno.Core.Serialization;
using Inno.Editor.Panel.FileBrowser;
using Xunit;

namespace Inno.Editor.Interactions.Tests;

public sealed class AssetHistoryTests : IDisposable
{
    private readonly string m_projectRoot = Path.Combine(
        Path.GetTempPath(),
        "InnoAssetHistoryTests",
        Guid.NewGuid().ToString("N"));
    private readonly EditorInteractionRuntime m_runtime;

    public AssetHistoryTests()
    {
        string assetRoot = Path.Combine(m_projectRoot, "Assets");
        Directory.CreateDirectory(assetRoot);
        IdentityManager.Initialize();
        _ = typeof(AssetEditorModule);
        _ = typeof(TextAsset);
        AssemblyManager.Initialize(new AssemblyManagerOptions
        {
            cacheDirectory = Path.Combine(m_projectRoot, "Library", "Assemblies")
        });
        TypeCacheManager.Initialize();
        SerializationManager.Initialize();
        AssetManagerOptions options = AssetManagerOptions.Create(
            assetRoot,
            Path.Combine(m_projectRoot, "Library"));
        options = new AssetManagerOptions
        {
            assetRoot = options.assetRoot,
            libraryRoot = options.libraryRoot,
            enableFileSystemWatcher = false,
            fileWatcherFlushDelayMs = options.fileWatcherFlushDelayMs,
            sourcePolicy = options.sourcePolicy,
            cacheOptions = options.cacheOptions
        };
        AssetManager.Initialize(options);
        m_runtime = new EditorInteractionRuntime(m_projectRoot);
        m_runtime.Start();
    }

    public void Dispose()
    {
        m_runtime.Dispose();
        AssetManager.Shutdown();
        SerializationManager.Shutdown();
        TypeCacheManager.Shutdown();
        AssemblyManager.Shutdown();
        IdentityManager.Shutdown();
        if (Directory.Exists(m_projectRoot))
            Directory.Delete(m_projectRoot, recursive: true);
    }

    [Fact]
    public void CreateFolderUndoAndRedoUseTheAssetDatabase()
    {
        Assert.True(m_runtime.interactions
            .For("panel/asset.file-browser", string.Empty)
            .Execute("file-browser/create-folder"));
        Assert.True(AssetManager.TryGetFileSystemEntry("New Folder", out AssetFileEntry created));
        Assert.True(created.isDirectory);

        Assert.True(m_runtime.interactions.history.Undo().succeeded);
        Assert.False(AssetManager.TryGetFileSystemEntry("New Folder", out _));
        Assert.True(m_runtime.interactions.history.Redo().succeeded);
        Assert.True(AssetManager.TryGetFileSystemEntry("New Folder", out _));
    }

    [Fact]
    public void DeleteFolderUndoRestoresIdentityMetadataAndEmptyDescendants()
    {
        AssetManager.CreateDirectory("Folder");
        AssetManager.CreateDirectory("Folder/Empty");
        Assert.True(AssetManager.TryGetPersistentId("Folder", out Guid folderId));
        Assert.True(AssetManager.TryGetFileSystemEntry("Folder", out AssetFileEntry folder));

        Assert.True(m_runtime.interactions
            .For("panel/asset.file-browser", folder)
            .Execute("file-browser/delete"));
        Assert.False(AssetManager.TryGetFileSystemEntry("Folder", out _));

        Assert.True(m_runtime.interactions.history.Undo().succeeded);
        Assert.True(AssetManager.TryGetFileSystemEntry("Folder/Empty", out AssetFileEntry restoredEmpty));
        Assert.True(restoredEmpty.isDirectory);
        Assert.True(AssetManager.TryGetPersistentId("Folder", out Guid restoredId));
        Assert.Equal(folderId, restoredId);

        Assert.True(m_runtime.interactions.history.Redo().succeeded);
        Assert.False(AssetManager.TryGetFileSystemEntry("Folder", out _));
    }

    [Fact]
    public void AssetDeleteHistorySurvivesATypeCatalogRefresh()
    {
        AssetManager.CreateDirectory("ReloadSafe");
        Assert.True(AssetManager.TryGetFileSystemEntry("ReloadSafe", out AssetFileEntry folder));
        Assert.True(m_runtime.interactions
            .For("panel/asset.file-browser", folder)
            .Execute("file-browser/delete"));

        TypeCacheManager.Rebuild();
        _ = m_runtime.panelCount;

        Assert.True(m_runtime.interactions.history.Undo().succeeded);
        Assert.True(AssetManager.TryGetFileSystemEntry("ReloadSafe", out _));
    }

    [Fact]
    public void FileBrowserDropMovesFilesAndFoldersWithUndoRedo()
    {
        AssetManager.CreateDirectory("From");
        AssetManager.CreateDirectory("From/Folder");
        AssetManager.CreateDirectory("From/Folder/Child");
        AssetManager.CreateDirectory("To");
        Assert.True(AssetManager.Save("From/Value.txt", new TextAsset("value")));
        Assert.True(AssetManager.TryGetInfo("From/Value.txt", out AssetInfo? file));
        Assert.NotNull(file);

        Guid fileToken = m_runtime.interactions
            .For("panel/asset.file-browser", file)
            .BeginDrag(new EditorDragData(file!, "Value.txt"));
        EditorInteraction destination = m_runtime.interactions.For("panel/asset.file-browser", "To");
        Assert.True(destination.QueryDrop(fileToken, EditorDropPlacement.Into).canDrop);
        Assert.True(destination.Drop(fileToken, EditorDropPlacement.Into).accepted);
        Assert.True(AssetManager.TryGetFileSystemEntry("To/Value.txt", out _));
        Assert.False(AssetManager.TryGetFileSystemEntry("From/Value.txt", out _));

        Assert.True(m_runtime.interactions.history.Undo().succeeded);
        Assert.True(AssetManager.TryGetFileSystemEntry("From/Value.txt", out _));
        Assert.True(m_runtime.interactions.history.Redo().succeeded);
        Assert.True(AssetManager.TryGetFileSystemEntry("To/Value.txt", out _));

        Assert.True(AssetManager.TryGetFileSystemEntry("From/Folder", out AssetFileEntry folder));
        Guid folderToken = m_runtime.interactions
            .For("panel/asset.file-browser", folder)
            .BeginDrag(new EditorDragData(folder, "Folder"));
        Assert.True(destination.QueryDrop(folderToken, EditorDropPlacement.Into).canDrop);
        Assert.True(destination.Drop(folderToken, EditorDropPlacement.Into).accepted);
        Assert.True(AssetManager.TryGetFileSystemEntry("To/Folder/Child", out _));

        Assert.True(m_runtime.interactions.history.Undo().succeeded);
        Assert.True(AssetManager.TryGetFileSystemEntry("From/Folder/Child", out _));
    }

    [Fact]
    public void FileBrowserDropRejectsAFolderOwnDescendantAndNameCollisions()
    {
        AssetManager.CreateDirectory("Source");
        AssetManager.CreateDirectory("Source/Child");
        AssetManager.CreateDirectory("Target");
        AssetManager.CreateDirectory("Target/Source");
        Assert.True(AssetManager.TryGetFileSystemEntry("Source", out AssetFileEntry source));

        Guid descendantToken = m_runtime.interactions
            .For("panel/asset.file-browser", source)
            .BeginDrag(new EditorDragData(source, "Source"));
        Assert.False(m_runtime.interactions
            .For("panel/asset.file-browser", "Source/Child")
            .QueryDrop(descendantToken, EditorDropPlacement.Into)
            .canDrop);

        Guid collisionToken = m_runtime.interactions
            .For("panel/asset.file-browser", source)
            .BeginDrag(new EditorDragData(source, "Source"));
        Assert.False(m_runtime.interactions
            .For("panel/asset.file-browser", "Target")
            .QueryDrop(collisionToken, EditorDropPlacement.Into)
            .canDrop);
    }
}
