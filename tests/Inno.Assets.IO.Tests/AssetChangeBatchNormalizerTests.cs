using System;
using System.IO;
using System.Linq;
using System.Reflection;

using Inno.Assets.IO;

using Xunit;

namespace Inno.Assets.IO.Tests;

public sealed class AssetChangeBatchNormalizerTests
{
    [Fact]
    public void Normalize_CreateChangedDeleted_MissingPath_FoldsToDeleted()
    {
        string root = CreateRoot();
        try
        {
            AssetChangedEvent[] normalized = NormalizeViaReflection(root, [
                new AssetChangedEvent("A/a.txt", WatcherChangeTypes.Created),
                new AssetChangedEvent("A/a.txt", WatcherChangeTypes.Changed),
                new AssetChangedEvent("A/a.txt", WatcherChangeTypes.Deleted)
            ]);

            Assert.Single(normalized);
            Assert.Equal("A/a.txt", normalized[0].relativePath);
            Assert.Equal(WatcherChangeTypes.Deleted, normalized[0].changeType);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public void Normalize_RenameAndChanged_PreservesOldPathAndRename()
    {
        string root = CreateRoot();
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "B"));
            File.WriteAllText(Path.Combine(root, "B", "new.txt"), "x");

            AssetChangedEvent[] normalized = NormalizeViaReflection(root, [
                new AssetChangedEvent("B/new.txt", WatcherChangeTypes.Renamed, "A/old.txt"),
                new AssetChangedEvent("B/new.txt", WatcherChangeTypes.Changed)
            ]);

            Assert.Single(normalized);
            Assert.Equal("B/new.txt", normalized[0].relativePath);
            Assert.Equal("A/old.txt", normalized[0].oldRelativePath);
            Assert.True(normalized[0].changeType.HasFlag(WatcherChangeTypes.Renamed));
            Assert.True(normalized[0].changeType.HasFlag(WatcherChangeTypes.Changed));
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public void Normalize_DifferentRawOrders_ConvergeToSameFinalSignals()
    {
        string root = CreateRoot();
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "Flow"));
            File.WriteAllText(Path.Combine(root, "Flow", "one.txt"), "x");

            AssetChangedEvent[] a = NormalizeViaReflection(root, [
                new AssetChangedEvent("Flow/two.txt", WatcherChangeTypes.Renamed, "Flow/one.txt"),
                new AssetChangedEvent("Flow/two.txt", WatcherChangeTypes.Changed),
                new AssetChangedEvent("Flow/one.txt", WatcherChangeTypes.Deleted)
            ]);

            AssetChangedEvent[] b = NormalizeViaReflection(root, [
                new AssetChangedEvent("Flow/one.txt", WatcherChangeTypes.Deleted),
                new AssetChangedEvent("Flow/two.txt", WatcherChangeTypes.Changed),
                new AssetChangedEvent("Flow/two.txt", WatcherChangeTypes.Renamed, "Flow/one.txt")
            ]);

            string[] signalA = a.Select(ToSignal).OrderBy(static x => x, StringComparer.Ordinal).ToArray();
            string[] signalB = b.Select(ToSignal).OrderBy(static x => x, StringComparer.Ordinal).ToArray();
            Assert.Equal(signalA, signalB);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    private static string ToSignal(AssetChangedEvent e)
        => $"{e.relativePath}|{e.oldRelativePath}|{(int)e.changeType}";

    private static AssetChangedEvent[] NormalizeViaReflection(string root, AssetChangedEvent[] rawBatch)
    {
        Assembly assembly = typeof(AssetFileSystem).Assembly;
        Type normalizerType = assembly.GetType("Inno.Assets.IO.AssetChangeBatchNormalizer", throwOnError: true)!;
        MethodInfo normalize = normalizerType.GetMethod(
            "Normalize",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!;

        object? result = normalize.Invoke(null, [root, rawBatch]);
        return Assert.IsType<AssetChangedEvent[]>(result);
    }

    private static string CreateRoot()
    {
        string root = Path.Combine(Path.GetTempPath(), "InnoAssetsNormalizerTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void DeleteRoot(string root)
    {
        if (Directory.Exists(root))
            Directory.Delete(root, recursive: true);
    }
}
