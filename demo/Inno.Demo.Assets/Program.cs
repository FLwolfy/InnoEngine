using System;
using System.IO;
using System.Reflection;

using Inno.Assets;
using Inno.Assets.Core;
using Inno.Assets.Types;
using Inno.Core.Assemblies;
using Inno.Core.Identity;
using Inno.Core.Logging;
using Inno.Core.Reflection;
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
        TypeCacheManager.Initialize();
        SerializationManager.Initialize();

        string assetsRoot = Path.Combine(executionRoot, "Assets");
        string libraryRoot = Path.Combine(executionRoot, "Library");
        string relativePath = "Notes/readme.txt";
        string metaPath = Path.Combine(assetsRoot, relativePath + ".imeta");

        void OnSourceChanged(AssetChangeSet changes)
        {
            Log.Info("[AssetsDemo] Committed asset revision {0}: {1} changes", changes.revision, changes.changes.Count);
            for (int i = 0; i < changes.changes.Count; i++)
                Log.Info("  - {0}: {1}", changes.changes[i].kind, changes.changes[i].relativePath);
        }

        try
        {
            AssetManager.Initialize(AssetManagerOptions.Create(assetsRoot, libraryRoot) with
            {
                fileWatcherFlushDelayMs = 30
            });
            AssetManager.Changed += OnSourceChanged;

            var created = new TextAsset("hello-from-asset-manager", "plain");
            bool createdOk = AssetManager.Save(relativePath, created);
            Log.Info("[AssetsDemo] Save(new) result={0}", createdOk);
            Log.Info("[AssetsDemo] Assets root: {0}", assetsRoot);
            Log.Info("[AssetsDemo] Library root: {0}", libraryRoot);
            Log.Info("[AssetsDemo] Artifacts root: {0}", AssetManager.artifactRoot);
            Log.Info("[AssetsDemo] Meta path exists: {0} ({1})", File.Exists(metaPath), metaPath);
            bool hasArtifact = AssetManager.TryGetArtifact(
                created.identity.persistentId,
                "runtime",
                out AssetArtifactInfo? artifact);
            Log.Info(
                "[AssetsDemo] Runtime artifact exists: {0} ({1})",
                hasArtifact,
                artifact?.absolutePath ?? string.Empty);

            TextAsset loaded = AssetManager.Load<TextAsset>(relativePath);
            Guid assetId = loaded.identity.persistentId;
            Log.Info("[AssetsDemo] Load result=true");
            Log.Info("[AssetsDemo] Loaded content: {0}", loaded.content);
            Log.Info("[AssetsDemo] Identity: {0}", assetId);

            bool sourceIndexed = AssetManager.TryGetFileSystemEntry(relativePath, out _);
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
            AssetManager.Changed -= OnSourceChanged;
            AssetManager.Shutdown();
            SerializationManager.Shutdown();
            TypeCacheManager.Shutdown();
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
