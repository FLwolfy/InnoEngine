using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

using Inno.Assets.Core;
using Inno.Assets.File;

using Xunit;

namespace Inno.Assets.File.Tests;

public sealed class AssetFileSystemTests
{
    [Fact]
    public void Queries_RejectRootedAndTraversalPaths()
    {
        string root = CreateRoot();
        try
        {
            using var fileSystem = new AssetFileSystem(root, autoStart: false);

            Assert.Throws<ArgumentException>(() => fileSystem.Exists("../outside.txt"));
            Assert.Throws<ArgumentException>(() => fileSystem.GetChildren("A/../../outside"));
            Assert.Throws<ArgumentException>(() => fileSystem.TryGetEntry(Path.GetFullPath(root), out _));
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public void Refresh_IndexesDirectoriesAndFiles()
    {
        string root = CreateRoot();
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "Config"));
            System.IO.File.WriteAllText(Path.Combine(root, "Config", "a.json"), "{}");
            System.IO.File.WriteAllText(Path.Combine(root, "readme.txt"), "hi");

            using var fs = new AssetFileSystem(root, autoStart: false);
            var entries = fs.GetEntries(includeDirectories: true);

            Assert.Contains(entries, static x => x.relativePath == string.Empty && x.isDirectory);
            Assert.Contains(entries, static x => x.relativePath == "Config" && x.isDirectory);
            Assert.Contains(entries, static x => x.relativePath == "Config/a.json" && !x.isDirectory);
            Assert.Contains(entries, static x => x.relativePath == "readme.txt" && !x.isDirectory);

            var rootChildren = fs.GetChildren(string.Empty);
            Assert.Equal(2, rootChildren.Count);
            Assert.Equal("Config", rootChildren[0].relativePath);
            Assert.Equal("readme.txt", rootChildren[1].relativePath);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public void Watcher_PublishesChangesOnlyWhenOwnerPolls()
    {
        string root = CreateRoot();
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "Config"));
            string file = Path.Combine(root, "Config", "watch.txt");
            System.IO.File.WriteAllText(file, "a");

            using var fs = new AssetFileSystem(root, autoStart: true, flushDelayMs: 20);
            System.IO.File.WriteAllText(file, "b");
            IReadOnlyList<AssetChangedEvent> batch = fs.WaitForIdle();

            Assert.Contains(batch, static x => x.relativePath == "Config/watch.txt");
            Assert.True(fs.Exists("Config/watch.txt"));
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public void Entries_ExposeLastExtensionDisplayNameAndFilterDatabaseNoise()
    {
        string root = CreateRoot();
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "Scripts"));
            System.IO.File.WriteAllText(Path.Combine(root, "Scripts", "Tool.editor.cs"), "class Tool {} ");
            System.IO.File.WriteAllText(Path.Combine(root, "Scripts", "Tool.editor.cs.imeta"), "meta");
            System.IO.File.WriteAllText(Path.Combine(root, "Scripts", "Tool.pdb"), "symbols");
            System.IO.File.WriteAllText(Path.Combine(root, ".DS_Store"), "noise");

            using var fileSystem = new AssetFileSystem(root, autoStart: false);
            AssetFileEntry entry = Assert.Single(
                fileSystem.GetEntries(includeDirectories: false));

            Assert.Equal("Tool.editor.cs", entry.name);
            Assert.Equal("Tool.editor", entry.nameWithoutExtension);
            Assert.Equal(".cs", entry.extension);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public void MultipleSourcesKeepCanonicalPathsNamesAndReadOnlyStateIsolated()
    {
        string root = CreateRoot();
        string project = Path.Combine(root, "Assets");
        string plugin = Path.Combine(root, "PluginAssets");
        try
        {
            Directory.CreateDirectory(project);
            Directory.CreateDirectory(plugin);
            System.IO.File.WriteAllText(Path.Combine(project, "same.txt"), "project");
            System.IO.File.WriteAllText(Path.Combine(plugin, "same.txt"), "plugin");
            var pluginId = new AssetSourceId("tests.mount");
            using var fileSystem = new AssetFileSystem(
                [
                    new AssetSourceMount(AssetSourceId.project, project, isReadOnly: false),
                    new AssetSourceMount(pluginId, plugin, isReadOnly: true)
                ],
                autoStart: false);

            Assert.True(fileSystem.TryGetEntry("same.txt", out AssetFileEntry projectEntry));
            Assert.True(fileSystem.TryGetEntry("tests.mount::same.txt", out AssetFileEntry pluginEntry));
            Assert.Equal("same.txt", projectEntry.name);
            Assert.Equal("same.txt", pluginEntry.name);
            Assert.False(projectEntry.isReadOnly);
            Assert.True(pluginEntry.isReadOnly);
            Assert.Equal(AssetSourceId.project, projectEntry.source);
            Assert.Equal(pluginId, pluginEntry.source);
            Assert.Contains(fileSystem.GetChildren(string.Empty), entry => entry.source == AssetSourceId.project);
            Assert.Contains(fileSystem.GetChildren(string.Empty), entry => entry.relativePath == "tests.mount::");
            Assert.Contains(fileSystem.GetChildren("tests.mount::"), entry => entry.relativePath == "tests.mount::same.txt");
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public void Watcher_RenameCarriesOldAndNewPathsThroughOwnerPolling()
    {
        string root = CreateRoot();
        try
        {
            string oldPath = Path.Combine(root, "old.txt");
            string newPath = Path.Combine(root, "new.txt");
            System.IO.File.WriteAllText(oldPath, "value");
            using var fileSystem = new AssetFileSystem(root, autoStart: true, flushDelayMs: 20);

            System.IO.File.Move(oldPath, newPath);
            IReadOnlyList<AssetChangedEvent> changes = fileSystem.WaitForIdle();

            AssetChangedEvent moved = Assert.Single(changes, static change =>
                change.changeType.HasFlag(WatcherChangeTypes.Renamed));
            Assert.Equal("old.txt", moved.oldRelativePath);
            Assert.Equal("new.txt", moved.relativePath);
            Assert.False(fileSystem.Exists("old.txt"));
            Assert.True(fileSystem.Exists("new.txt"));
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    private static string CreateRoot()
    {
        string root = Path.Combine(Path.GetTempPath(), "InnoAssetsIOTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void DeleteRoot(string root)
    {
        if (Directory.Exists(root))
            Directory.Delete(root, recursive: true);
    }
}
