using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

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
            using var signal = new AutoResetEvent(false);
            var observed = new List<AssetChangedEvent>();
            fileSystem.ChangedBatch += changes =>
            {
                lock (observed)
                    observed.AddRange(changes);
                signal.Set();
            };

            string path = Path.Combine(root, "Flow", "item.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            System.IO.File.WriteAllText(path, "one");
            System.IO.File.WriteAllText(path, "two");
            Assert.True(signal.WaitOne(TimeSpan.FromSeconds(3)));
            fileSystem.WaitForIdle();

            Assert.True(fileSystem.TryGetEntry("Flow/item.txt", out AssetFileEntry created));
            Assert.False(created.isDirectory);

            System.IO.File.Delete(path);
            Assert.True(signal.WaitOne(TimeSpan.FromSeconds(3)));
            fileSystem.WaitForIdle();

            Assert.False(fileSystem.TryGetEntry("Flow/item.txt", out _));
            lock (observed)
            {
                Assert.Contains(observed, static change =>
                    change.relativePath == "Flow/item.txt" &&
                    change.changeType.HasFlag(WatcherChangeTypes.Deleted));
            }
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
            using var signal = new AutoResetEvent(false);
            var observed = new List<AssetChangedEvent>();
            fileSystem.ChangedBatch += changes =>
            {
                lock (observed)
                    observed.AddRange(changes);
                signal.Set();
            };

            System.IO.File.Move(oldPath, newPath);
            Assert.True(signal.WaitOne(TimeSpan.FromSeconds(3)));
            fileSystem.WaitForIdle();

            AssetChangedEvent renamed;
            lock (observed)
            {
                renamed = Assert.Single(observed.Where(static change =>
                    change.changeType.HasFlag(WatcherChangeTypes.Renamed)));
            }
            Assert.Equal("old.txt", renamed.oldRelativePath);
            Assert.Equal("new.txt", renamed.relativePath);
            Assert.False(fileSystem.Exists("old.txt"));
            Assert.True(fileSystem.Exists("new.txt"));
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
            int eventCount = 0;
            fileSystem.ChangedBatch += changes => eventCount += changes.Count;

            System.IO.File.WriteAllText(Path.Combine(root, "asset.txt.imeta"), "cache");
            Thread.Sleep(100);
            fileSystem.WaitForIdle();

            Assert.Equal(0, eventCount);
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
