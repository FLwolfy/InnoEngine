using System;
using System.IO;

using Inno.Assets;
using Inno.Assets.Pipeline;
using Inno.Core.Diagnostics;
using Inno.Extensibility.Modules;
using Inno.Core.Identity;
using Inno.Core.Logging;
using Inno.Extensibility.Types;
using Inno.Core.Serialization;
using Inno.Core.Settings;
using Inno.Editor.Core;
using Inno.Editor.Panel.FileBrowser;
using Inno.Plugins.Authoring;
using Xunit;

namespace Inno.Editor.Interactions.Tests;

public sealed class AssetHistoryTests : IDisposable
{
    private readonly IdentityAllocator m_identities = new();
    private readonly IDisposable m_identityScope;
    private readonly string m_projectRoot = Path.Combine(
        Path.GetTempPath(),
        "InnoAssetHistoryTests",
        Guid.NewGuid().ToString("N"));
    private readonly ModuleHost m_modules;
    private readonly TypeCatalog m_types;
    private readonly SerializationRegistry m_serialization;
    private readonly DiagnosticHub m_diagnostics = new();
    private readonly LogRouter m_logs = new();
    private readonly AssetPipeline m_assets;
    private readonly PluginEnvironment m_plugins;
    private readonly ProjectSettingsStore m_settings;
    private readonly EditorInteractionRuntime m_runtime;

    public AssetHistoryTests()
    {
        string assetRoot = Path.Combine(m_projectRoot, "Assets");
        Directory.CreateDirectory(assetRoot);
        m_identityScope = m_identities.EnterScope();
        _ = typeof(AssetEditorModule);
        _ = typeof(TextAsset);
        m_modules = new ModuleHost(new ModuleHostOptions
        {
            cacheDirectory = Path.Combine(m_projectRoot, "Library", "Assemblies")
        });
        m_types = new TypeCatalog(m_modules);
        m_serialization = new SerializationRegistry(m_types);
        m_settings = new ProjectSettingsStore(
            Path.Combine(m_projectRoot, "Settings.Project.inno"),
            m_types,
            m_serialization,
            new ProjectId("tests.editor"));
        string libraryRoot = Path.Combine(m_projectRoot, "Library");
        string pluginRoot = Path.Combine(m_projectRoot, "Plugins");
        var pluginSources = new PluginSourceService(
            m_serialization,
            pluginRoot,
            libraryRoot);
        PluginScanResult pluginScan = pluginSources.Scan();
        AssetPipelineOptions options = AssetPipelineOptions.Create(
            assetRoot,
            libraryRoot);
        options = options with
        {
            enableFileSystemWatcher = false
        };
        m_assets = new AssetPipeline(
            m_modules,
            m_types,
            m_serialization,
            m_identities,
            m_diagnostics,
            m_logs,
            options);
        m_plugins = new PluginEnvironment(
            m_assets,
            m_settings,
            m_serialization,
            pluginRoot,
            libraryRoot,
            pluginScan);
        m_runtime = new EditorInteractionRuntime(
            new EditorContext(m_projectRoot),
            m_types,
            m_logs,
            [m_types, m_serialization, m_assets, m_plugins]);
        m_runtime.Start();
    }

    public void Dispose()
    {
        m_runtime.Dispose();
        m_plugins.Dispose();
        m_assets.Dispose();
        m_settings.Dispose();
        m_serialization.Dispose();
        m_types.Dispose();
        m_modules.Dispose();
        m_logs.Dispose();
        m_identityScope.Dispose();
        if (Directory.Exists(m_projectRoot))
            Directory.Delete(m_projectRoot, recursive: true);
    }

    [Fact]
    public void CreateFolderUndoAndRedoUseTheAssetDatabase()
    {
        Assert.True(m_runtime.interactions
            .For("panel/asset.file-browser", string.Empty)
            .Execute("file-browser/create-folder"));
        Assert.True(m_assets.TryGetFileSystemEntry(AssetPath.Project("New Folder"), out AssetFileEntry created));
        Assert.True(created.isDirectory);

        Assert.True(m_runtime.interactions.history.Undo().succeeded);
        Assert.False(m_assets.TryGetFileSystemEntry(AssetPath.Project("New Folder"), out _));
        Assert.True(m_runtime.interactions.history.Redo().succeeded);
        Assert.True(m_assets.TryGetFileSystemEntry(AssetPath.Project("New Folder"), out _));
    }

    [Fact]
    public void DeleteFolderUndoRestoresIdentityMetadataAndEmptyDescendants()
    {
        m_assets.CreateDirectory(AssetPath.Project("Folder"));
        m_assets.CreateDirectory(AssetPath.Project("Folder/Empty"));
        Assert.True(m_assets.TryGetPersistentId(AssetPath.Project("Folder"), out Guid folderId));
        Assert.True(m_assets.TryGetFileSystemEntry(AssetPath.Project("Folder"), out AssetFileEntry folder));

        Assert.True(m_runtime.interactions
            .For("panel/asset.file-browser", folder)
            .Execute("file-browser/delete"));
        Assert.False(m_assets.TryGetFileSystemEntry(AssetPath.Project("Folder"), out _));

        Assert.True(m_runtime.interactions.history.Undo().succeeded);
        Assert.True(m_assets.TryGetFileSystemEntry(AssetPath.Project("Folder/Empty"), out AssetFileEntry restoredEmpty));
        Assert.True(restoredEmpty.isDirectory);
        Assert.True(m_assets.TryGetPersistentId(AssetPath.Project("Folder"), out Guid restoredId));
        Assert.Equal(folderId, restoredId);

        Assert.True(m_runtime.interactions.history.Redo().succeeded);
        Assert.False(m_assets.TryGetFileSystemEntry(AssetPath.Project("Folder"), out _));
    }

    [Fact]
    public void AssetDeleteHistorySurvivesATypeCatalogRefresh()
    {
        m_assets.CreateDirectory(AssetPath.Project("ReloadSafe"));
        Assert.True(m_assets.TryGetFileSystemEntry(AssetPath.Project("ReloadSafe"), out AssetFileEntry folder));
        Assert.True(m_runtime.interactions
            .For("panel/asset.file-browser", folder)
            .Execute("file-browser/delete"));

        m_types.Rebuild();
        _ = m_runtime.panelCount;

        Assert.True(m_runtime.interactions.history.Undo().succeeded);
        Assert.True(m_assets.TryGetFileSystemEntry(AssetPath.Project("ReloadSafe"), out _));
    }

    [Fact]
    public void FileBrowserDropMovesFilesAndFoldersWithUndoRedo()
    {
        m_assets.CreateDirectory(AssetPath.Project("From"));
        m_assets.CreateDirectory(AssetPath.Project("From/Folder"));
        m_assets.CreateDirectory(AssetPath.Project("From/Folder/Child"));
        m_assets.CreateDirectory(AssetPath.Project("To"));
        Assert.True(m_assets.Save(AssetPath.Project("From/Value.txt"), new TextAsset("value")));
        Assert.True(m_assets.TryGetInfo(AssetPath.Project("From/Value.txt"), out AssetInfo? file));
        Assert.NotNull(file);

        Guid fileToken = m_runtime.interactions
            .For("panel/asset.file-browser", file)
            .BeginDrag(new EditorDragData(file!, "Value.txt"));
        EditorInteraction destination = m_runtime.interactions.For("panel/asset.file-browser", "To");
        Assert.True(destination.QueryDrop(fileToken, EditorDropPlacement.Into).canDrop);
        Assert.True(destination.Drop(fileToken, EditorDropPlacement.Into).accepted);
        Assert.True(m_assets.TryGetFileSystemEntry(AssetPath.Project("To/Value.txt"), out _));
        Assert.False(m_assets.TryGetFileSystemEntry(AssetPath.Project("From/Value.txt"), out _));

        Assert.True(m_runtime.interactions.history.Undo().succeeded);
        Assert.True(m_assets.TryGetFileSystemEntry(AssetPath.Project("From/Value.txt"), out _));
        Assert.True(m_runtime.interactions.history.Redo().succeeded);
        Assert.True(m_assets.TryGetFileSystemEntry(AssetPath.Project("To/Value.txt"), out _));

        Assert.True(m_assets.TryGetFileSystemEntry(AssetPath.Project("From/Folder"), out AssetFileEntry folder));
        Guid folderToken = m_runtime.interactions
            .For("panel/asset.file-browser", folder)
            .BeginDrag(new EditorDragData(folder, "Folder"));
        Assert.True(destination.QueryDrop(folderToken, EditorDropPlacement.Into).canDrop);
        Assert.True(destination.Drop(folderToken, EditorDropPlacement.Into).accepted);
        Assert.True(m_assets.TryGetFileSystemEntry(AssetPath.Project("To/Folder/Child"), out _));

        Assert.True(m_runtime.interactions.history.Undo().succeeded);
        Assert.True(m_assets.TryGetFileSystemEntry(AssetPath.Project("From/Folder/Child"), out _));
    }

    [Fact]
    public void FileBrowserDropRejectsAFolderOwnDescendantAndNameCollisions()
    {
        m_assets.CreateDirectory(AssetPath.Project("Source"));
        m_assets.CreateDirectory(AssetPath.Project("Source/Child"));
        m_assets.CreateDirectory(AssetPath.Project("Target"));
        m_assets.CreateDirectory(AssetPath.Project("Target/Source"));
        Assert.True(m_assets.TryGetFileSystemEntry(AssetPath.Project("Source"), out AssetFileEntry source));

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
