using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;

using Inno.Assets;
using Inno.Assets.Core;
using Inno.Assets.File;
using Inno.Assets.Types;
using Inno.Core.Assemblies;
using Inno.Core.Identity;
using Inno.Core.Logging;
using Inno.Core.Serialization;

namespace Inno.Demo.Assets;

internal static class Program
{
    private static int Main()
    {
        string executionRoot = Directory.GetCurrentDirectory();
        LogManager.Initialize();
        LogManager.RegisterSink(new ConsoleLogSink());
        LogManager.SetMinimumLevel(LogLevel.Debug);
        IdentityManager.Initialize();
        AssemblyManager.Initialize(new AssemblyManagerOptions
        {
            cacheDirectory = Path.Combine(executionRoot, "Library", "Assemblies")
        });
        SerializationManager.Initialize();

        string assetsRoot = Path.Combine(executionRoot, "Assets");
        string artifactsRoot = Path.Combine(executionRoot, "Artifacts");
        string relativePath = "Notes/readme.txt";
        string metaPath = Path.Combine(assetsRoot, relativePath + ".imeta");
        string artifactPath = Path.Combine(artifactsRoot, relativePath + ".abin");

        using var changed = new AutoResetEvent(false);
        void OnSourceChanged(IReadOnlyList<AssetChangedEvent> changes)
        {
            Log.Info("[AssetsDemo] Source changed batch: {0}", changes.Count);
            for (int i = 0; i < changes.Count; i++)
                Log.Info("  - {0}: {1}", changes[i].changeType, changes[i].relativePath);

            changed.Set();
        }

        try
        {
            AssetManager.Initialize(new AssetManagerOptions
            {
                assetRoot = assetsRoot,
                artifactRoot = artifactsRoot,
                enableFileSystemWatcher = true,
                fileWatcherFlushDelayMs = 30
            });
            AssetManager.SourceFileSystemChanged += OnSourceChanged;

            var created = new TextAsset("hello-from-asset-manager", "plain");
            bool createdOk = AssetManager.Save(relativePath, created);
            Log.Info("[AssetsDemo] Save(new) result={0}", createdOk);
            Log.Info("[AssetsDemo] Assets root: {0}", assetsRoot);
            Log.Info("[AssetsDemo] Artifacts root: {0}", artifactsRoot);
            Log.Info("[AssetsDemo] Meta path exists: {0} ({1})", File.Exists(metaPath), metaPath);
            Log.Info("[AssetsDemo] Artifact path exists: {0} ({1})", File.Exists(artifactPath), artifactPath);

            TextAsset loaded = AssetManager.Load<TextAsset>(relativePath);
            Guid assetId = loaded.identity.persistentId;
            Log.Info("[AssetsDemo] Load result=true");
            Log.Info("[AssetsDemo] Loaded content: {0}", loaded.content);
            Log.Info("[AssetsDemo] Identity: {0}", assetId);

            bool sourceIndexed = SpinWait.SpinUntil(
                () => AssetManager.TryGetFileSystemEntry(relativePath, out _),
                TimeSpan.FromSeconds(2));
            Log.Info("[AssetsDemo] Source indexed: {0}", sourceIndexed);

            Log.Info("[AssetsDemo] Indexed entries: {0}", AssetManager.GetFileSystemEntries().Count);

            SetTextAssetContent(loaded, "updated-by-save");
            bool saved = AssetManager.Save(loaded);
            TextAsset updated = AssetManager.Load<TextAsset>(assetId);
            Log.Info("[AssetsDemo] Save(existing)={0}, updated content={1}", saved, updated.content);

            Log.Info("[AssetsDemo] Load(id)=true, content={0}", updated.content);
        }
        catch (Exception ex)
        {
            Log.Error("[AssetsDemo] ERROR");
            Log.Error(ex);
            return 1;
        }
        finally
        {
            Log.Info("[AssetsDemo] Execution root: {0}", executionRoot);
            AssetManager.SourceFileSystemChanged -= OnSourceChanged;
            AssetManager.Shutdown();
            SerializationManager.Shutdown();
            AssemblyManager.Shutdown();
            IdentityManager.Shutdown();
            LogManager.Shutdown();
        }
        return 0;
    }

    private static void SetTextAssetContent(TextAsset asset, string content)
    {
        PropertyInfo prop = typeof(TextAsset).GetProperty(
            "content",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;
        prop.SetValue(asset, content);
    }
}
