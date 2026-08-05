using System;
using System.IO;
using System.Linq;
using System.Threading;

using Inno.Assets.File;

using Xunit;

namespace Inno.Assets.File.Tests;

public sealed class AssetFileSystemTests
{
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
    public void Watcher_EmitsChangedBatch_WhenFileChanged()
    {
        string root = CreateRoot();
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "Config"));
            string file = Path.Combine(root, "Config", "watch.txt");
            System.IO.File.WriteAllText(file, "a");

            using var fs = new AssetFileSystem(root, autoStart: true, flushDelayMs: 20);
            using var changed = new AutoResetEvent(false);
            AssetChangedEvent[]? batch = null;

            fs.ChangedBatch += changes =>
            {
                batch = changes.ToArray();
                changed.Set();
            };

            System.IO.File.WriteAllText(file, "b");
            bool signaled = changed.WaitOne(TimeSpan.FromSeconds(2));

            Assert.True(signaled);
            Assert.NotNull(batch);
            Assert.Contains(batch!, static x => x.relativePath == "Config/watch.txt");
            Assert.True(fs.Exists("Config/watch.txt"));
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
