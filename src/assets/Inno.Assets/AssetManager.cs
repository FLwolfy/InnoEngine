using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Inno.Assets.Core;
using Inno.Assets.File;
using Inno.Assets.Loader;
using Inno.Assets.Serialization;
using Inno.Core.Identity;
using Inno.Core.Logging;
using Inno.Core.Reflection;
using Inno.Core.Serialization;

namespace Inno.Assets;

/// <summary>
/// Provides the single application-level entry point for importing, loading, saving and
/// collecting assets.
/// </summary>
public static class AssetManager
{
    private const string C_META_POSTFIX = ".imeta";
    private const string C_ARTIFACT_POSTFIX = ".abin";
    private static readonly Lock S_LIFECYCLE_LOCK = new();

    private static AssetLoader? s_loader;
    private static AssetFileSystem? s_fileSystem;

    /// <summary>Gets whether asset services are initialized.</summary>
    public static bool isInitialized { get; private set; }

    /// <summary>Gets the absolute source asset root.</summary>
    public static string assetRoot { get; private set; } = string.Empty;

    /// <summary>Gets the absolute generated artifact root.</summary>
    public static string artifactRoot { get; private set; } = string.Empty;

    /// <summary>Occurs after normalized source file changes have been applied.</summary>
    public static event Action<IReadOnlyList<AssetChangedEvent>>? SourceFileSystemChanged;

    /// <summary>Occurs after a canonical loaded asset has been updated in place.</summary>
    public static event Action<AssetObject>? AssetReloaded;

    /// <summary>Initializes global asset services.</summary>
    /// <param name="options">The asset source, artifact and watcher configuration.</param>
    /// <exception cref="InvalidOperationException">
    /// Thrown when identity, type cache or serialization services are not initialized.
    /// </exception>
    public static void Initialize(AssetManagerOptions options)
    {
        if (!IdentityManager.isInitialized)
            throw new InvalidOperationException("AssetManager requires IdentityManager to be initialized first.");
        if (!TypeCacheManager.isInitialized)
            throw new InvalidOperationException("AssetManager requires TypeCacheManager to be initialized first.");
        if (!SerializationManager.isInitialized)
            throw new InvalidOperationException("AssetManager requires SerializationManager to be initialized first.");
        if (string.IsNullOrWhiteSpace(options.assetRoot))
            throw new ArgumentException("Asset root is required.", nameof(options));
        if (string.IsNullOrWhiteSpace(options.artifactRoot))
            throw new ArgumentException("Artifact root is required.", nameof(options));

        lock (S_LIFECYCLE_LOCK)
        {
            ShutdownLocked();
            assetRoot = Path.GetFullPath(options.assetRoot);
            artifactRoot = Path.GetFullPath(options.artifactRoot);
            AssetLoader loader = new(assetRoot, artifactRoot);
            AssetFileSystem fileSystem = new(
                assetRoot,
                autoStart: false,
                options.fileWatcherFlushDelayMs);
            loader.AssetReloaded += OnAssetReloaded;
            fileSystem.ChangedBatch += OnSourceChanges;
            s_loader = loader;
            s_fileSystem = fileSystem;
            isInitialized = true;
            AssetSerializationServices.SetReferenceResolver(ResolveSerializedReference);
            try
            {
                loader.Rescan();
                fileSystem.Refresh();
                if (options.enableFileSystemWatcher)
                    fileSystem.Start();
            }
            catch
            {
                ShutdownLocked();
                throw;
            }
        }
    }

    /// <summary>Shuts down global asset services and releases all runtime resources.</summary>
    public static void Shutdown()
    {
        lock (S_LIFECYCLE_LOCK)
            ShutdownLocked();
    }

    /// <summary>Loads a canonical asset by source-relative path.</summary>
    /// <typeparam name="TAsset">The required asset type.</typeparam>
    /// <param name="relativePath">The source-relative path.</param>
    /// <returns>The canonical asset instance.</returns>
    /// <exception cref="InvalidOperationException">Thrown when no compatible asset can be loaded.</exception>
    public static TAsset Load<TAsset>(string relativePath) where TAsset : AssetObject
    {
        AssetObject? asset = GetLoader().Load(relativePath, typeof(TAsset));
        return asset as TAsset ?? throw new InvalidOperationException(
            $"Asset '{relativePath}' cannot be loaded as '{typeof(TAsset).FullName}'.");
    }

    /// <summary>Loads a canonical asset by persistent identity.</summary>
    /// <typeparam name="TAsset">The required asset type.</typeparam>
    /// <param name="persistentId">The persistent asset identity.</param>
    /// <returns>The canonical asset instance.</returns>
    /// <exception cref="InvalidOperationException">Thrown when no compatible asset can be loaded.</exception>
    public static TAsset Load<TAsset>(Guid persistentId) where TAsset : AssetObject
    {
        AssetObject? asset = GetLoader().Load(persistentId, typeof(TAsset));
        return asset as TAsset ?? throw new InvalidOperationException(
            $"Asset '{persistentId}' cannot be loaded as '{typeof(TAsset).FullName}'.");
    }

    /// <summary>Tries to load a canonical asset by source-relative path.</summary>
    /// <typeparam name="TAsset">The required asset type.</typeparam>
    /// <param name="relativePath">The source-relative path.</param>
    /// <param name="asset">The canonical asset when successful.</param>
    /// <returns><see langword="true"/> when a compatible asset was loaded.</returns>
    public static bool TryLoad<TAsset>(string relativePath, out TAsset? asset) where TAsset : AssetObject
    {
        bool success = GetLoader().TryLoad(relativePath, typeof(TAsset), out AssetObject? value);
        asset = value as TAsset;
        return success && asset is not null;
    }

    /// <summary>Tries to load a canonical asset by persistent identity.</summary>
    /// <typeparam name="TAsset">The required asset type.</typeparam>
    /// <param name="persistentId">The persistent asset identity.</param>
    /// <param name="asset">The canonical asset when successful.</param>
    /// <returns><see langword="true"/> when a compatible asset was loaded.</returns>
    public static bool TryLoad<TAsset>(Guid persistentId, out TAsset? asset) where TAsset : AssetObject
    {
        bool success = GetLoader().TryLoad(persistentId, typeof(TAsset), out AssetObject? value);
        asset = value as TAsset;
        return success && asset is not null;
    }

    /// <summary>Asynchronously loads a canonical asset by source-relative path.</summary>
    /// <typeparam name="TAsset">The required asset type.</typeparam>
    /// <param name="relativePath">The source-relative path.</param>
    /// <param name="cancellationToken">Cancellation for the current caller's wait.</param>
    /// <returns>The canonical asset instance.</returns>
    public static async ValueTask<TAsset> LoadAsync<TAsset>(
        string relativePath,
        CancellationToken cancellationToken = default)
        where TAsset : AssetObject
    {
        AssetObject? asset = await GetLoader()
            .LoadAsync(relativePath, typeof(TAsset), cancellationToken)
            .ConfigureAwait(false);
        return asset as TAsset ?? throw new InvalidOperationException(
            $"Asset '{relativePath}' cannot be loaded as '{typeof(TAsset).FullName}'.");
    }

    /// <summary>Asynchronously loads a canonical asset by persistent identity.</summary>
    /// <typeparam name="TAsset">The required asset type.</typeparam>
    /// <param name="persistentId">The persistent asset identity.</param>
    /// <param name="cancellationToken">Cancellation for the current caller's wait.</param>
    /// <returns>The canonical asset instance.</returns>
    public static async ValueTask<TAsset> LoadAsync<TAsset>(
        Guid persistentId,
        CancellationToken cancellationToken = default)
        where TAsset : AssetObject
    {
        AssetObject? asset = await GetLoader()
            .LoadAsync(persistentId, typeof(TAsset), cancellationToken)
            .ConfigureAwait(false);
        return asset as TAsset ?? throw new InvalidOperationException(
            $"Asset '{persistentId}' cannot be loaded as '{typeof(TAsset).FullName}'.");
    }

    /// <summary>Imports one source asset.</summary>
    /// <param name="relativePath">The source-relative path.</param>
    /// <returns><see langword="true"/> when an importer handled the source.</returns>
    public static bool Import(string relativePath)
    {
        bool imported = GetLoader().Import(relativePath);
        if (imported)
            GetFileSystem().Refresh();
        return imported;
    }

    /// <summary>Saves an asset to its current source path.</summary>
    /// <param name="asset">The asset to save.</param>
    /// <returns><see langword="true"/> when an importer exported the asset.</returns>
    public static bool Save(AssetObject asset)
    {
        bool saved = GetLoader().Save(asset);
        if (saved)
            GetFileSystem().Refresh();
        return saved;
    }

    /// <summary>Saves an unsaved asset to its initial source-relative path.</summary>
    /// <param name="relativePath">The initial source-relative path.</param>
    /// <param name="asset">The asset to save.</param>
    /// <returns><see langword="true"/> when an importer exported the asset.</returns>
    public static bool Save(string relativePath, AssetObject asset)
    {
        bool saved = GetLoader().Save(relativePath, asset);
        if (saved)
            GetFileSystem().Refresh();
        return saved;
    }

    /// <summary>Reconciles source files, generated files and the persistent catalog.</summary>
    public static void Rescan()
    {
        GetLoader().Rescan();
        GetFileSystem().Refresh();
    }

    /// <summary>Collects assets that have no external managed references.</summary>
    /// <returns>The number of released canonical assets.</returns>
    public static int UnloadUnusedAssets() => GetLoader().UnloadUnusedAssets();

    /// <summary>Tries to resolve an asset type without loading the asset.</summary>
    /// <param name="relativePath">The source-relative path.</param>
    /// <param name="assetType">The resolved concrete asset type.</param>
    /// <returns><see langword="true"/> when the type can be resolved.</returns>
    public static bool TryGetAssetType(string relativePath, out Type? assetType)
        => GetLoader().TryGetAssetType(relativePath, out assetType);

    /// <summary>Tries to resolve a persistent identity without loading the asset.</summary>
    /// <param name="relativePath">The source-relative path.</param>
    /// <param name="persistentId">The resolved persistent identity.</param>
    /// <returns><see langword="true"/> when catalog metadata exists.</returns>
    public static bool TryGetPersistentId(string relativePath, out Guid persistentId)
        => GetLoader().TryGetPersistentId(relativePath, out persistentId);

    /// <summary>Gets source paths for all canonical loaded assets.</summary>
    /// <returns>A stable source-relative path snapshot.</returns>
    public static IReadOnlyList<string> GetLoadedPaths() => GetLoader().GetLoadedPaths();

    /// <summary>Gets direct or transitive runtime dependencies of an asset.</summary>
    /// <param name="asset">The asset to query.</param>
    /// <param name="recursive">Whether transitive dependencies should be included.</param>
    /// <returns>The dependency descriptors.</returns>
    public static IReadOnlyList<AssetDependency> GetDependencies(
        AssetObject asset,
        bool recursive = false)
        => GetLoader().GetDependencies(asset, recursive);

    /// <summary>Gets an engine-known reference diagnostic snapshot.</summary>
    /// <param name="asset">The asset to inspect.</param>
    /// <returns>The reference diagnostic snapshot.</returns>
    public static AssetReferenceInfo GetReferenceInfo(AssetObject asset)
        => GetLoader().GetReferenceInfo(asset);

    /// <summary>Gets indexed source entries.</summary>
    /// <param name="includeDirectories">Whether directories should be included.</param>
    /// <returns>The stable source entry snapshot.</returns>
    public static IReadOnlyList<AssetFileEntry> GetFileSystemEntries(bool includeDirectories = true)
        => FilterGenerated(GetFileSystem().GetEntries(includeDirectories));

    /// <summary>Gets immediate indexed children of a source directory.</summary>
    /// <param name="parentRelativePath">The source-relative parent path.</param>
    /// <returns>The immediate child entry snapshot.</returns>
    public static IReadOnlyList<AssetFileEntry> GetFileSystemChildren(string parentRelativePath)
        => FilterGenerated(GetFileSystem().GetChildren(parentRelativePath));

    /// <summary>Tries to resolve an indexed source entry.</summary>
    /// <param name="relativePath">The source-relative path.</param>
    /// <param name="entry">The resolved source entry.</param>
    /// <returns><see langword="true"/> when the entry exists and is not generated metadata.</returns>
    public static bool TryGetFileSystemEntry(string relativePath, out AssetFileEntry entry)
    {
        if (IsGeneratedPath(relativePath))
        {
            entry = null!;
            return false;
        }
        return GetFileSystem().TryGetEntry(relativePath, out entry);
    }

    /// <summary>Waits until queued source watcher changes have been processed.</summary>
    public static void WaitForIdle() => GetFileSystem().WaitForIdle();

    internal static AssetObject ResolveSerializedReference(
        Guid persistentId,
        Guid stableTypeId,
        string lastKnownPath,
        Type expectedType,
        string propertyPath)
    {
        try
        {
            return GetLoader().ResolveReference(
                persistentId,
                stableTypeId,
                lastKnownPath,
                expectedType);
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(
                $"Asset reference '{persistentId}' at '{propertyPath}' cannot be resolved as " +
                $"'{expectedType.FullName}'.",
                exception);
        }
    }

    private static void OnSourceChanges(IReadOnlyList<AssetChangedEvent> changes)
    {
        AssetLoader? loader = s_loader;
        if (loader is null)
            return;
        try
        {
            loader.ApplySourceChanges(changes);
        }
        catch (Exception exception)
        {
            Log.Error("Asset source refresh failed: {0}", exception);
            try
            {
                loader.Rescan();
            }
            catch (Exception recoveryException)
            {
                Log.Error("Asset source recovery rescan failed: {0}", recoveryException);
            }
        }
        InvokeObservers(SourceFileSystemChanged, changes);
    }

    private static void OnAssetReloaded(AssetObject asset)
        => InvokeObservers(AssetReloaded, asset);

    private static void InvokeObservers<T>(Action<T>? handlers, T value)
    {
        if (handlers is null)
            return;
        foreach (Delegate handler in handlers.GetInvocationList())
        {
            try
            {
                ((Action<T>)handler)(value);
            }
            catch
            {
                // Observer failures cannot roll back committed manager state.
            }
        }
    }

    private static AssetLoader GetLoader()
        => isInitialized && s_loader is not null
            ? s_loader
            : throw new InvalidOperationException("AssetManager is not initialized.");

    private static AssetFileSystem GetFileSystem()
        => isInitialized && s_fileSystem is not null
            ? s_fileSystem
            : throw new InvalidOperationException("AssetManager is not initialized.");

    private static IReadOnlyList<AssetFileEntry> FilterGenerated(IReadOnlyList<AssetFileEntry> entries)
        => entries.Where(static entry => entry.isDirectory || !IsGeneratedPath(entry.relativePath)).ToArray();

    private static bool IsGeneratedPath(string relativePath)
        => relativePath.EndsWith(C_META_POSTFIX, StringComparison.OrdinalIgnoreCase) ||
           relativePath.EndsWith(C_ARTIFACT_POSTFIX, StringComparison.OrdinalIgnoreCase);

    private static void ShutdownLocked()
    {
        AssetSerializationServices.SetReferenceResolver(null);
        if (s_fileSystem is not null)
        {
            s_fileSystem.ChangedBatch -= OnSourceChanges;
            s_fileSystem.Dispose();
        }
        if (s_loader is not null)
        {
            s_loader.AssetReloaded -= OnAssetReloaded;
            s_loader.Dispose();
        }
        s_fileSystem = null;
        s_loader = null;
        assetRoot = string.Empty;
        artifactRoot = string.Empty;
        isInitialized = false;
        SourceFileSystemChanged = null;
        AssetReloaded = null;
    }
}
