using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

using Inno.Assets.Core;
using Inno.Assets.File;
using Inno.Assets.Importers;
using Inno.Assets.Loader;
using Inno.Core.Identity;
using Inno.Core.Logging;
using Inno.Core.Reflection;

namespace Inno.Assets;

/// <summary>
/// Global static entry point for asset importing, caching, loading and saving.
/// </summary>
public static class AssetManager
{
    private static readonly Lock SYNC = new();

    private static AssetFileSystem s_fileSystem = null!;
    private static AssetLoader s_loader = null!;

    #region State

    /// <summary>
    /// Absolute source asset root directory.
    /// </summary>
    public static string assetRoot { get; private set; } = string.Empty;

    /// <summary>
    /// Absolute imported artifact root directory.
    /// </summary>
    public static string artifactRoot { get; private set; } = string.Empty;

    /// <summary>
    /// True when manager has been initialized.
    /// </summary>
    public static bool isInitialized { get; private set; }

    /// <summary>
    /// Raised when source files changed in asset root.
    /// </summary>
    public static event Action<IReadOnlyList<AssetChangedEvent>>? SourceFileSystemChanged;

    #endregion

    #region Lifecycle

    /// <summary>
    /// Initializes asset manager.
    /// </summary>
    public static void Initialize(AssetManagerOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.assetRoot))
            throw new ArgumentException("Asset root is required.", nameof(options));
        if (string.IsNullOrWhiteSpace(options.artifactRoot))
            throw new ArgumentException("Artifact root is required.", nameof(options));

        lock (SYNC)
        {
            ShutdownInternal();

            assetRoot = Path.GetFullPath(options.assetRoot);
            artifactRoot = Path.GetFullPath(options.artifactRoot);
            Directory.CreateDirectory(assetRoot);
            Directory.CreateDirectory(artifactRoot);

            s_loader = new AssetLoader(assetRoot, artifactRoot);
            s_fileSystem = new AssetFileSystem(assetRoot, autoStart: false, options.fileWatcherFlushDelayMs);
            s_fileSystem.ChangedBatch += OnFileSystemChangedBatch;

            isInitialized = true;
        }

        if (options.autoRegisterBuiltInImporters)
            RegisterBuiltInImporters();

        if (options.autoRegisterImportersFromTypeCache)
            RegisterImportersFromTypeCache();

        lock (SYNC)
        {
            s_loader.ReconcileStorageState();
            s_fileSystem.Refresh();
            if (options.enableFileSystemWatcher)
                s_fileSystem.Start();
        }
    }

    /// <summary>
    /// Clears caches and unregisters importers.
    /// </summary>
    public static void Shutdown()
    {
        lock (SYNC)
        {
            ShutdownInternal();
        }
    }

    #endregion

    #region Importers

    /// <summary>
    /// Registers default built-in importers.
    /// </summary>
    public static void RegisterBuiltInImporters()
    {
        EnsureInitialized();

        s_loader.RegisterImporter(new BinaryAssetImporter());
        s_loader.RegisterImporter(new TextAssetImporter());
        s_loader.RegisterImporter(new ShaderAssetImporter());
        s_loader.RegisterImporter(new PngTextureAssetImporter());
    }

    /// <summary>
    /// Discovers and registers importer types from <c>TypeCache</c>.
    /// </summary>
    /// <remarks>
    /// Requires <c>TypeCacheManager</c> to be initialized before calling.
    /// </remarks>
    public static void RegisterImportersFromTypeCache()
    {
        EnsureInitialized();

        if (!TypeCacheManager.isInitialized)
        {
            Log.Error("Cannot register asset importers from TypeCache because TypeCacheManager is not initialized. Call TypeCacheManager.Initialize() before RegisterImportersFromTypeCache(), or initialize AssetManager with autoRegisterImportersFromTypeCache enabled.");
            return;
        }

        foreach (Type type in TypeCache.GetTypesImplementing<IAssetImporter>())
        {
            if (type.IsAbstract || type.IsInterface)
                continue;

            if (Activator.CreateInstance(type) is IAssetImporter importer)
                RegisterImporter(importer);
        }
    }

    /// <summary>
    /// Registers an importer by type using parameterless constructor.
    /// </summary>
    public static void RegisterImporter<TImporter>() where TImporter : IAssetImporter, new()
    {
        EnsureInitialized();
        s_loader.RegisterImporter<TImporter>();
    }

    /// <summary>
    /// Registers an importer instance.
    /// </summary>
    public static void RegisterImporter(IAssetImporter importer)
    {
        EnsureInitialized();
        s_loader.RegisterImporter(importer);
    }

    #endregion

    #region Importing

    /// <summary>
    /// Imports the assets from disk for generating metadata and artifacts.
    /// </summary>
    /// <remarks>
    /// This process will automatically load the imported assets into the memory.
    /// </remarks>
    public static bool Import(string relativePath)
    {
        EnsureInitialized();
        return s_loader.Load<AssetObject>(relativePath, AssetLoadMode.DiskRaw) != null;
    }

    /// <summary>
    /// Imports one source asset from disk as the requested asset type.
    /// </summary>
    public static bool Import<TAsset>(string relativePath)
        where TAsset : AssetObject
    {
        EnsureInitialized();
        return s_loader.Load<TAsset>(relativePath, AssetLoadMode.DiskRaw) != null;
    }

    /// <summary>
    /// Re-scans the full source asset tree, repairs stale generated files and syncs loaded assets.
    /// </summary>
    public static void Rescan()
    {
        EnsureInitialized();
        lock (SYNC)
        {
            s_loader.ReconcileStorageState();
            s_fileSystem.Refresh();
        }
    }

    #endregion

    #region Loading

    /// <summary>
    /// Loads an asset from the allowed load sources.
    /// </summary>
    public static TAsset? Load<TAsset>(string relativePath)
        where TAsset : AssetObject
    {
        EnsureInitialized();
        var assetLoadMode = AssetLoadMode.MemoryCache | AssetLoadMode.DiskCache;
        return s_loader.Load<TAsset>(relativePath, assetLoadMode);
    }

    /// <summary>
    /// Loads an asset reference from the allowed load sources.
    /// </summary>
    public static AssetRef<TAsset> LoadRef<TAsset>(string relativePath)
        where TAsset : AssetObject
    {
        EnsureInitialized();
        var assetLoadMode = AssetLoadMode.MemoryCache | AssetLoadMode.DiskCache;
        return s_loader.LoadRef<TAsset>(relativePath, assetLoadMode);
    }

    /// <summary>
    /// Resolves a handle to currently loaded asset instance.
    /// </summary>
    public static TAsset? Resolve<TAsset>(AssetRef<TAsset> assetRef)
        where TAsset : AssetObject
    {
        EnsureInitialized();
        return s_loader.Resolve(assetRef);
    }

    /// <summary>
    /// Gets an asset reference for a path without loading the asset.
    /// </summary>
    public static AssetRef<TAsset> GetRef<TAsset>(string relativePath) where TAsset : AssetObject
    {
        EnsureInitialized();
        return s_loader.GetRef<TAsset>(relativePath);
    }

    /// <summary>
    /// Gets an asset reference for an identity.
    /// </summary>
    public static AssetRef<TAsset> GetRef<TAsset>(Identity identity) where TAsset : AssetObject
    {
        EnsureInitialized();
        return s_loader.GetRef<TAsset>(identity);
    }

    /// <summary>
    /// Returns currently loaded relative paths.
    /// </summary>
    public static IReadOnlyList<string> GetLoadedPaths()
    {
        EnsureInitialized();
        return s_loader.GetLoadedPaths();
    }

    #endregion

    #region Saving

    /// <summary>
    /// Saves asset back to its current source path.
    /// </summary>
    public static bool Save(AssetObject asset)
    {
        EnsureInitialized();
        return s_loader.Save(asset);
    }

    /// <summary>
    /// Saves asset back to source path.
    /// </summary>
    public static bool Save(string relativePath, AssetObject asset)
    {
        EnsureInitialized();
        return s_loader.Save(relativePath, asset);
    }

    #endregion

    #region Unloading

    /// <summary>
    /// Unloads one asset by path.
    /// </summary>
    public static bool Unload(string relativePath)
    {
        if (!isInitialized)
            return false;

        return s_loader.Unload(relativePath);
    }

    /// <summary>
    /// Unloads one asset by handle.
    /// </summary>
    public static bool Unload<TAsset>(AssetRef<TAsset> assetRef) where TAsset : AssetObject
    {
        if (!isInitialized)
            return false;

        return s_loader.Unload(assetRef);
    }

    /// <summary>
    /// Unloads all loaded assets.
    /// </summary>
    public static void UnloadAll()
    {
        if (isInitialized)
            s_loader.UnloadAll();
    }

    #endregion

    #region File System

    /// <summary>
    /// Returns current filesystem tree graph for source assets.
    /// </summary>
    public static string GetFileSystemTreeGraph()
    {
        EnsureInitialized();
        lock (SYNC)
        {
            return s_fileSystem.BuildTreeGraph();
        }
    }

    /// <summary>
    /// Returns a snapshot of indexed source files/directories.
    /// </summary>
    public static IReadOnlyList<AssetFileEntry> GetFileSystemEntries(bool includeDirectories = true)
    {
        EnsureInitialized();
        lock (SYNC)
        {
            return s_fileSystem.GetEntries(includeDirectories);
        }
    }

    /// <summary>
    /// Returns a snapshot of immediate children under one directory path.
    /// </summary>
    public static IReadOnlyList<AssetFileEntry> GetFileSystemChildren(string parentRelativePath)
    {
        EnsureInitialized();
        lock (SYNC)
        {
            return s_fileSystem.GetChildren(parentRelativePath);
        }
    }

    /// <summary>
    /// Tries to get one indexed source file-system entry by relative path.
    /// </summary>
    public static bool TryGetFileSystemEntry(string relativePath, out AssetFileEntry entry)
    {
        EnsureInitialized();
        lock (SYNC)
        {
            return s_fileSystem.TryGetEntry(relativePath, out entry);
        }
    }

    /// <summary>
    /// Waits until the filesystem async tasks to synchronize.
    /// </summary>
    public static void WaitForIdle()
    {
        EnsureInitialized();

        if (!s_fileSystem.isWatching)
            return;

        s_fileSystem.WaitForIdle();
    }

    #endregion

    #region File System Events

    private static void OnFileSystemChangedBatch(IReadOnlyList<AssetChangedEvent> changes)
    {
        AssetChangedEvent[] sourceChanges;
        lock (SYNC)
        {
            sourceChanges = changes
                .Where(static x => !s_loader.IsInternalGeneratedPath(x.relativePath))
                .ToArray();

            if (sourceChanges.Length == 0)
                return;

            AssetChangedEvent[] renamed = sourceChanges
                .Where(static x => x.changeType.HasFlag(WatcherChangeTypes.Renamed))
                .ToArray();
            AssetChangedEvent[] deleted = sourceChanges
                .Where(static x => !x.changeType.HasFlag(WatcherChangeTypes.Renamed) && x.changeType.HasFlag(WatcherChangeTypes.Deleted))
                .ToArray();
            AssetChangedEvent[] createdOrChanged = sourceChanges
                .Where(static x => !x.changeType.HasFlag(WatcherChangeTypes.Renamed) && !x.changeType.HasFlag(WatcherChangeTypes.Deleted))
                .ToArray();

            for (int i = 0; i < renamed.Length; i++)
                s_loader.HandleRenamedSourcePath(renamed[i].oldRelativePath, renamed[i].relativePath);

            for (int i = 0; i < deleted.Length; i++)
                s_loader.HandleDeletedSourcePath(deleted[i].relativePath);

            for (int i = 0; i < createdOrChanged.Length; i++)
                s_loader.HandleCreatedOrChangedSourcePath(createdOrChanged[i].relativePath);
        }

        SourceFileSystemChanged?.Invoke(sourceChanges);
    }

    #endregion

    #region Internals

    private static void EnsureInitialized()
    {
        if (!isInitialized)
        {
            Log.Error("Asset Manager not initialized");
            throw new InvalidOperationException("AssetManager is not initialized.");
        }
    }

    private static void ShutdownInternal()
    {
        if (isInitialized)
        {
            s_fileSystem.ChangedBatch -= OnFileSystemChangedBatch;
            s_fileSystem.Dispose();
            s_loader.Clear();
        }

        assetRoot = string.Empty;
        artifactRoot = string.Empty;
        isInitialized = false;
    }

    #endregion
}
