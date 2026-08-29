using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

using Inno.Assets.Core;
using Inno.Assets.File;

using Xunit;

namespace Inno.Assets.File.Tests;

public sealed class AssetFileSystemChangeTests
{
    [Fact]
    public void Watcher_CoalescesCreateChangeAndDeleteThroughPublicCoordinator()
    {
        string root = CreateRoot();
        try
        {
            using var fileSystem = new AssetFileSystem(root, autoStart: true, flushDelayMs: 20);
            string path = Path.Combine(root, "Flow", "item.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            System.IO.File.WriteAllText(path, "one");
            System.IO.File.WriteAllText(path, "two");
            IReadOnlyList<AssetChangedEvent> observed = fileSystem.WaitForIdle();

            Assert.True(fileSystem.TryGetEntry(AssetPath.Project("Flow/item.txt"), out AssetFileEntry created));
            Assert.False(created.isDirectory);

            System.IO.File.Delete(path);
            observed = observed.Concat(fileSystem.WaitForIdle()).ToArray();

            Assert.False(fileSystem.TryGetEntry(AssetPath.Project("Flow/item.txt"), out _));
            Assert.Contains(observed, static change =>
                change.relativePath == "Flow/item.txt" &&
                change.changeType.HasFlag(WatcherChangeTypes.Deleted));
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public void Watcher_RenamePreservesOldAndNewRelativePaths()
    {
        string root = CreateRoot();
        try
        {
            string oldPath = Path.Combine(root, "old.txt");
            string newPath = Path.Combine(root, "new.txt");
            System.IO.File.WriteAllText(oldPath, "value");
            using var fileSystem = new AssetFileSystem(root, autoStart: true, flushDelayMs: 20);
            System.IO.File.Move(oldPath, newPath);
            IReadOnlyList<AssetChangedEvent> observed = fileSystem.WaitForIdle();

            AssetChangedEvent renamed = Assert.Single(observed.Where(static change =>
                change.changeType.HasFlag(WatcherChangeTypes.Renamed)));
            Assert.Equal("old.txt", renamed.oldRelativePath);
            Assert.Equal("new.txt", renamed.relativePath);
            Assert.False(fileSystem.Exists(AssetPath.Project("old.txt")));
            Assert.True(fileSystem.Exists(AssetPath.Project("new.txt")));
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public void Watcher_DoesNotPublishGeneratedMetadataChanges()
    {
        string root = CreateRoot();
        try
        {
            using var fileSystem = new AssetFileSystem(root, autoStart: true, flushDelayMs: 20);
            System.IO.File.WriteAllText(Path.Combine(root, "asset.txt.imeta"), "cache");
            Thread.Sleep(100);
            IReadOnlyList<AssetChangedEvent> changes = fileSystem.WaitForIdle();

            Assert.Empty(changes);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    private static string CreateRoot()
    {
        string root = Path.Combine(Path.GetTempPath(), "InnoAssetsFileChangeTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void DeleteRoot(string root)
    {
        if (Directory.Exists(root))
            Directory.Delete(root, recursive: true);
    }
}
