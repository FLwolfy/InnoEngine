using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

using Inno.Assets;
using Inno.Assets.Core;
using Inno.Assets.Serialization;
using Inno.Core.Logging;
using Inno.Core.Reflection;
using Inno.Core.Serialization;
using Inno.Editor.Core;
using Inno.Editor.Interactions;
using Inno.Engine.Scene;
using Inno.Engine.Scene.Assets;
using Inno.Engine.Scene.Components;

namespace Inno.Editor.Scene;

/// <summary>
/// Tracks editor scene documents, their source paths, and serialized dirty state.
/// </summary>
[EditorModule(order: 200)]
public sealed class EditorSceneWorkspace : EditorModule
{
    private const double C_DIRTY_REFRESH_SECONDS = 0.1;
    private const string C_SCENE_EXTENSION = ".iscene";
    private const string C_PREFAB_EXTENSION = ".iprefab";

    private readonly Dictionary<Guid, SceneDocument> m_documents = [];
    private readonly ConcurrentQueue<AssetChange> m_sourceChanges = new();
    private readonly EditorSceneDiagnosticPublisher m_diagnostics = new();
    private readonly EditorInteractions? m_interactions;

    private bool m_isAttached;
    private string[]? m_pendingScenePaths;
    private string m_pendingActivePath = string.Empty;
    private long m_nextRestoreAttemptTimestamp;
    private long m_waitingTypeCatalogVersion = -1;

    /// <summary>
    /// Creates a scene workspace and optionally enables editor selection coordination.
    /// </summary>
    /// <param name="interactions">
    /// The active editor interaction entry point. The extension runtime supplies this dependency automatically;
    /// direct tooling callers may omit it when selection coordination is unnecessary.
    /// </param>
    public EditorSceneWorkspace(EditorInteractions? interactions = null)
    {
        m_interactions = interactions;
    }

    /// <inheritdoc />
    protected override string workspaceStateId => "scene-workspace";

    /// <summary>
    /// Gets all scenes currently available to editor features.
    /// </summary>
    public IReadOnlyList<GameScene> scenes => SceneManager.loadedScenes;

    /// <summary>
    /// Gets the active scene, or <see langword="null"/> when the workspace contains no scenes.
    /// </summary>
    public GameScene? activeScene => SceneManager.activeScene;

    /// <summary>
    /// Creates and loads a uniquely named unsaved scene alongside the currently loaded scenes.
    /// </summary>
    /// <returns>The newly created active scene.</returns>
    public GameScene CreateScene()
    {
        string name = CreateUniqueSceneName(SceneManager.loadedScenes);
        GameScene scene = SceneManager.LoadNewSceneAdditive(name);
        m_documents.Add(
            scene.identity.persistentId,
            new SceneDocument(scene, string.Empty, Guid.Empty, []));
        return scene;
    }

    /// <summary>
    /// Applies queued asset path changes to loaded scene documents and prefab instances.
    /// This method must be called from the editor main thread.
    /// </summary>
    public void Refresh()
    {
        SynchronizeReplacedScenes();
        while (m_sourceChanges.TryDequeue(out AssetChange change))
        {
            try
            {
                if (change.kind == AssetChangeKind.Moved)
                    ApplyRename(change.oldRelativePath, change.relativePath);
            }
            catch (Exception exception)
            {
                Log.Error("Editor asset rename synchronization failed: {0}", exception);
            }
        }

        var synchronizedScenes = new HashSet<Guid>();
        foreach (SceneDocument document in m_documents.Values)
        {
            if (document.scene.isDestroyed)
                continue;
            Guid sceneId = document.scene.identity.persistentId;
            synchronizedScenes.Add(sceneId);
            try
            {
                SynchronizeSource(document.scene, document);
                m_diagnostics.ResolveSynchronization(sceneId);
            }
            catch (Exception exception)
            {
                document.isDirty = true;
                if (m_diagnostics.PublishSynchronizationFailure(document.scene, exception))
                    Log.Error("Scene document synchronization failed: {0}", exception);
            }
        }
        m_diagnostics.RetainSynchronizationTargets(synchronizedScenes);
    }

    /// <summary>
    /// Gets whether a scene contains unsaved serialized changes.
    /// </summary>
    /// <param name="scene">Scene to inspect.</param>
    /// <returns><see langword="true"/> when the scene has no source path or differs from its saved baseline.</returns>
    public bool IsDirty(GameScene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        SceneDocument document = GetOrCreateDocument(scene);
        try
        {
            SynchronizeSource(scene, document);
        }
        catch
        {
            document.isDirty = true;
        }
        if (string.IsNullOrEmpty(document.sourcePath))
            return true;
        if (!string.Equals(scene.name, GetAssetName(document.sourcePath), StringComparison.Ordinal))
        {
            document.isDirty = true;
            return true;
        }

        long now = Stopwatch.GetTimestamp();
        if (now < document.nextRefreshTimestamp)
            return document.isDirty;

        document.nextRefreshTimestamp = now + (long)(Stopwatch.Frequency * C_DIRTY_REFRESH_SECONDS);
        try
        {
            byte[] currentHash = ComputeSceneHash(scene);
            document.isDirty = !currentHash.AsSpan().SequenceEqual(document.savedHash);
        }
        catch
        {
            document.isDirty = true;
        }
        return document.isDirty;
    }

    /// <summary>
    /// Saves a scene to its existing path or creates a scene asset in the requested directory.
    /// </summary>
    /// <param name="scene">Scene to save.</param>
    /// <param name="currentDirectory">Fallback asset directory for a new scene.</param>
    /// <returns>The saved source-relative path.</returns>
    public string SaveScene(GameScene scene, string currentDirectory)
    {
        ArgumentNullException.ThrowIfNull(scene);
        SceneDocument document = GetOrCreateDocument(scene);
        string relativePath;
        if (string.IsNullOrEmpty(document.sourcePath))
        {
            relativePath = CreateUniquePath(currentDirectory, scene.name, C_SCENE_EXTENSION);
        }
        else
        {
            relativePath = RenameSceneSourceIfNeeded(scene, document);
        }
        SaveSceneAtPath(scene, relativePath);
        return relativePath;
    }

    /// <summary>
    /// Saves a scene as an asset in the requested directory and makes that path its document path.
    /// </summary>
    /// <param name="scene">Scene to save.</param>
    /// <param name="currentDirectory">Target asset directory.</param>
    /// <returns>The saved source-relative path.</returns>
    public string SaveSceneToDirectory(GameScene scene, string currentDirectory)
    {
        ArgumentNullException.ThrowIfNull(scene);
        SceneDocument document = GetOrCreateDocument(scene);
        string currentPath = document.sourcePath;
        string currentParent = NormalizePath(Path.GetDirectoryName(currentPath));
        string targetDirectory = NormalizePath(currentDirectory);
        string relativePath;
        if (!string.IsNullOrEmpty(currentPath) &&
            string.Equals(currentParent, targetDirectory, StringComparison.Ordinal))
        {
            relativePath = RenameSceneSourceIfNeeded(scene, document);
        }
        else
        {
            relativePath = CreateUniquePath(targetDirectory, scene.name, C_SCENE_EXTENSION);
        }
        SaveSceneAtPath(scene, relativePath);
        return relativePath;
    }

    /// <summary>
    /// Captures a game object subtree as a prefab in the requested directory.
    /// </summary>
    /// <param name="gameObject">Prefab root.</param>
    /// <param name="currentDirectory">Target asset directory.</param>
    /// <returns>The saved source-relative path.</returns>
    public string SavePrefab(GameObject gameObject, string currentDirectory)
    {
        ArgumentNullException.ThrowIfNull(gameObject);
        string relativePath = CreateUniquePath(currentDirectory, gameObject.name, C_PREFAB_EXTENSION);
        if (!AssetManager.Save(relativePath, PrefabAsset.Capture(gameObject)))
            throw new InvalidOperationException($"No asset importer could save prefab '{relativePath}'.");
        return relativePath;
    }

    /// <summary>
    /// Opens a scene asset additively as the active editor scene.
    /// </summary>
    /// <param name="relativePath">Scene asset source-relative path.</param>
    /// <returns>The existing loaded instance or the newly loaded scene.</returns>
    public GameScene OpenScene(string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        string normalizedPath = NormalizePath(relativePath);
        SceneDocument? existing = m_documents.Values.FirstOrDefault(document =>
            document.scene.isLoaded &&
            !document.scene.isDestroyed &&
            string.Equals(document.sourcePath, normalizedPath, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            SceneManager.SetActiveScene(existing.scene);
            return existing.scene;
        }

        SceneAsset asset = AssetManager.Load<SceneAsset>(normalizedPath);
        GameScene scene = asset.Instantiate();
        scene.name = GetAssetName(normalizedPath);
        byte[] savedHash = ComputeSceneHash(scene);
        SceneManager.LoadSceneAdditive(scene);
        m_documents.Add(
            scene.identity.persistentId,
            new SceneDocument(scene, normalizedPath, asset.identity.persistentId, savedHash));
        return scene;
    }

    /// <summary>
    /// Closes a loaded scene and removes its editor document state without deleting its source asset.
    /// </summary>
    /// <param name="scene">Loaded scene to close.</param>
    /// <returns><see langword="true"/> when the scene was loaded and closed.</returns>
    public bool CloseScene(GameScene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        if (scene.isDestroyed)
            return false;
        Guid sceneId = scene.identity.persistentId;
        bool closed = SceneManager.UnloadScene(scene);
        if (closed)
        {
            m_documents.Remove(sceneId);
            m_diagnostics.ResolveSynchronization(sceneId);
        }
        return closed;
    }

    /// <summary>
    /// Tries to get the current source-relative asset path of a saved scene.
    /// </summary>
    /// <param name="scene">Scene whose document path is requested.</param>
    /// <param name="relativePath">The saved source path when available.</param>
    /// <returns><see langword="true"/> when the scene is backed by a scene asset.</returns>
    public bool TryGetSourcePath(GameScene scene, out string relativePath)
    {
        ArgumentNullException.ThrowIfNull(scene);
        SceneDocument document = GetOrCreateDocument(scene);
        try
        {
            SynchronizeSource(scene, document);
        }
        catch
        {
            document.isDirty = true;
        }
        relativePath = document.sourcePath;
        return !string.IsNullOrEmpty(relativePath);
    }

    /// <summary>
    /// Removes all tracked document state.
    /// </summary>
    public void Clear()
    {
        m_documents.Clear();
        m_diagnostics.RetainSynchronizationTargets(new HashSet<Guid>());
        while (m_sourceChanges.TryDequeue(out _))
        {
        }
    }

    /// <inheritdoc />
    protected override void CaptureWorkspaceState(EditorWorkspaceStateWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        if (m_pendingScenePaths is not null)
        {
            writer.Set("openScenes", m_pendingScenePaths);
            writer.Set("activeScene", m_pendingActivePath);
            return;
        }
        string[] scenePaths = SceneManager.loadedScenes
            .Select(scene => TryGetSourcePath(scene, out string path) ? path : string.Empty)
            .Where(static path => !string.IsNullOrEmpty(path))
            .ToArray();
        writer.Set("openScenes", scenePaths);
        if (SceneManager.activeScene is GameScene active && TryGetSourcePath(active, out string activePath))
            writer.Set("activeScene", activePath);
    }

    /// <inheritdoc />
    protected override void RestoreWorkspaceState(EditorWorkspaceStateReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        string[] paths = reader.Get("openScenes", Array.Empty<string>());
        m_pendingActivePath = reader.Get("activeScene", string.Empty);
        if (paths.Length == 0)
        {
            m_pendingScenePaths = null;
            m_diagnostics.ResolveRestore();
            return;
        }
        m_pendingScenePaths = paths
            .Select(NormalizePath)
            .Where(static path => !string.IsNullOrEmpty(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        m_nextRestoreAttemptTimestamp = 0;
        m_waitingTypeCatalogVersion = -1;
    }

    /// <summary>
    /// Attaches the workspace to Asset Database changes and ensures an editable scene exists.
    /// </summary>
    /// <param name="context">The shared editor context for the active runtime.</param>
    protected override void OnStart(EditorContext context)
    {
        if (m_isAttached)
            return;
        AssetManager.Changed += OnAssetDatabaseChanged;
        m_isAttached = true;
    }

    /// <summary>
    /// Refreshes source synchronization for loaded editor documents.
    /// </summary>
    /// <param name="context">The shared editor context containing current frame state.</param>
    protected override void OnUpdate(EditorContext context)
    {
        TryRestorePendingScenes();
        Refresh();
    }

    /// <summary>
    /// Detaches the workspace from Asset Database changes and releases any scene it created for the editor.
    /// </summary>
    /// <param name="context">The shared editor context for the runtime being stopped.</param>
    protected override void OnStop(EditorContext context)
    {
        if (!m_isAttached)
            return;
        AssetManager.Changed -= OnAssetDatabaseChanged;
        SceneManager.UnloadAllScenes();
        m_isAttached = false;
        Clear();
    }

    /// <inheritdoc />
    protected override void OnDispose()
        => m_diagnostics.Dispose();

    private void SaveSceneAtPath(GameScene scene, string relativePath)
    {
        scene.name = GetAssetName(relativePath);
        bool exists = AssetManager.TryLoad(relativePath, out SceneAsset? sceneAsset);
        sceneAsset ??= new SceneAsset();
        sceneAsset.CaptureFrom(scene);
        bool saved = exists
            ? AssetManager.Save(sceneAsset)
            : AssetManager.Save(relativePath, sceneAsset);
        if (!saved)
            throw new InvalidOperationException($"No asset importer could save scene '{relativePath}'.");

        byte[] savedHash = ComputeSceneHash(scene);
        SceneDocument document = GetOrCreateDocument(scene);
        document.sourcePath = relativePath;
        document.sourceAssetId = sceneAsset.identity.persistentId;
        document.savedHash = savedHash;
        document.isDirty = false;
        document.nextRefreshTimestamp = Stopwatch.GetTimestamp() +
                                        (long)(Stopwatch.Frequency * C_DIRTY_REFRESH_SECONDS);
    }

    private SceneDocument GetOrCreateDocument(GameScene scene)
    {
        Guid sceneId = scene.identity.persistentId;
        if (m_documents.TryGetValue(sceneId, out SceneDocument? document))
        {
            document.scene = scene;
            return document;
        }
        document = new SceneDocument(scene, string.Empty, Guid.Empty, []);
        m_documents.Add(sceneId, document);
        return document;
    }

    private static void SynchronizeSource(GameScene scene, SceneDocument document)
    {
        if (document.sourceAssetId == Guid.Empty)
            return;
        if (!AssetManager.TryLoad(document.sourceAssetId, out SceneAsset? asset) ||
            asset is null ||
            asset.isMissing)
        {
            document.sourcePath = string.Empty;
            document.sourceAssetId = Guid.Empty;
            document.isDirty = true;
            return;
        }

        string sourcePath = NormalizePath(asset.sourcePath);
        string sourceName = GetAssetName(sourcePath);
        bool pathChanged = !string.Equals(document.sourcePath, sourcePath, StringComparison.Ordinal);
        if (!pathChanged)
            return;

        bool wasDirty = document.isDirty ||
                        !ComputeSceneHash(scene).AsSpan().SequenceEqual(document.savedHash);
        document.sourcePath = sourcePath;
        scene.name = sourceName;
        if (!wasDirty)
            document.savedHash = ComputeSceneHash(scene);
        document.isDirty = wasDirty;
        document.nextRefreshTimestamp = Stopwatch.GetTimestamp() +
                                        (long)(Stopwatch.Frequency * C_DIRTY_REFRESH_SECONDS);
    }

    private void ApplyRename(string oldRelativePath, string newRelativePath)
    {
        string oldPath = NormalizePath(oldRelativePath);
        string newPath = NormalizePath(newRelativePath);
        string extension = Path.GetExtension(newPath);
        if (string.Equals(extension, C_SCENE_EXTENSION, StringComparison.OrdinalIgnoreCase))
        {
            ApplySceneRename(oldPath, newPath);
            return;
        }
        if (string.Equals(extension, C_PREFAB_EXTENSION, StringComparison.OrdinalIgnoreCase))
            ApplyPrefabRename(oldPath, newPath);
    }

    private void ApplySceneRename(string oldPath, string newPath)
    {
        foreach (SceneDocument document in m_documents.Values)
        {
            if (!string.Equals(document.sourcePath, oldPath, StringComparison.OrdinalIgnoreCase) ||
                document.scene.isDestroyed)
            {
                continue;
            }

            bool wasDirty = document.isDirty ||
                            !ComputeSceneHash(document.scene).AsSpan().SequenceEqual(document.savedHash);
            document.sourcePath = newPath;
            document.scene.name = GetAssetName(newPath);
            if (AssetManager.TryGetPersistentId(newPath, out Guid sourceAssetId))
                document.sourceAssetId = sourceAssetId;
            if (!wasDirty)
                document.savedHash = ComputeSceneHash(document.scene);
            document.isDirty = wasDirty;
        }
    }

    private static string RenameSceneSourceIfNeeded(GameScene scene, SceneDocument document)
    {
        string currentPath = NormalizePath(document.sourcePath);
        string directory = NormalizePath(Path.GetDirectoryName(currentPath));
        string targetPath = Combine(directory, SanitizeFileName(scene.name) + C_SCENE_EXTENSION);
        if (string.Equals(currentPath, targetPath, StringComparison.Ordinal))
            return currentPath;
        if (AssetManager.TryGetFileSystemEntry(targetPath, out _))
        {
            throw new IOException(
                $"Scene asset '{targetPath}' already exists. Choose a different scene name before saving.");
        }

        AssetManager.Move(currentPath, targetPath);
        document.sourcePath = targetPath;
        return targetPath;
    }

    private static void ApplyPrefabRename(string oldPath, string newPath)
    {
        if (!AssetManager.TryGetPersistentId(newPath, out Guid sourceAssetId))
            return;
        string oldName = GetAssetName(oldPath);
        string newName = GetAssetName(newPath);
        IReadOnlyList<GameScene> scenes = SceneManager.loadedScenes;
        for (int sceneIndex = 0; sceneIndex < scenes.Count; sceneIndex++)
        {
            IReadOnlyList<GameObject> objects = scenes[sceneIndex].GetObjects();
            for (int objectIndex = 0; objectIndex < objects.Count; objectIndex++)
            {
                GameObject gameObject = objects[objectIndex];
                PrefabInstanceInfo? prefab = gameObject.prefabInstance;
                if (prefab?.isRoot == true &&
                    prefab.sourceAssetId == sourceAssetId &&
                    string.Equals(gameObject.name, oldName, StringComparison.Ordinal))
                {
                    gameObject.name = newName;
                }
            }
        }
    }

    private void OnAssetDatabaseChanged(AssetChangeSet changeSet)
    {
        m_waitingTypeCatalogVersion = -1;
        for (int i = 0; i < changeSet.changes.Count; i++)
            m_sourceChanges.Enqueue(changeSet.changes[i]);
    }

    private void TryRestorePendingScenes()
    {
        if (m_pendingScenePaths is null)
        {
            m_diagnostics.ResolveRestore();
            return;
        }
        long typeCatalogVersion = TypeCacheManager.current.version;
        if (m_waitingTypeCatalogVersion == typeCatalogVersion)
            return;
        long now = Stopwatch.GetTimestamp();
        if (now < m_nextRestoreAttemptTimestamp)
            return;
        m_nextRestoreAttemptTimestamp = now + Stopwatch.Frequency / 4;

        var candidates = new List<(GameScene Scene, string Path, Guid AssetId, byte[] Hash)>();
        bool waitingForSourceIndex = false;
        try
        {
            for (int i = 0; i < m_pendingScenePaths.Length; i++)
            {
                string path = m_pendingScenePaths[i];
                if (!AssetManager.TryGetFileSystemEntry(path, out _))
                {
                    string absolutePath = Path.Combine(
                        AssetManager.assetRoot,
                        path.Replace('/', Path.DirectorySeparatorChar));
                    if (File.Exists(absolutePath))
                    {
                        waitingForSourceIndex = true;
                        break;
                    }
                    Log.Warn("Editor scene workspace skipped missing scene '{0}'.", path);
                    continue;
                }
                SceneAsset asset = AssetManager.Load<SceneAsset>(path);
                GameScene scene = asset.Instantiate();
                scene.name = GetAssetName(path);
                candidates.Add((
                    scene,
                    path,
                    asset.identity.persistentId,
                    ComputeSceneHash(scene)));
            }
        }
        catch (SceneTypeResolutionException exception)
        {
            DisposeRestoreCandidates(candidates);
            m_waitingTypeCatalogVersion = typeCatalogVersion;
            string message =
                $"{exception.elementKind} stable type id '{exception.stableTypeId}' " +
                "is not present in the active type catalog.";
            if (m_diagnostics.PublishRestoreFailure("SCENE-TYPE", message))
            {
                Log.Error(
                    "Editor scene workspace cannot restore saved scenes because {0}",
                    message);
            }
            return;
        }
        catch (Exception exception)
        {
            DisposeRestoreCandidates(candidates);
            m_waitingTypeCatalogVersion = typeCatalogVersion;
            if (m_diagnostics.PublishRestoreFailure("SCENE-RESTORE", exception.Message))
                Log.Error("Editor scene workspace could not restore saved scenes: {0}", exception);
            return;
        }
        if (waitingForSourceIndex)
        {
            DisposeRestoreCandidates(candidates);
            return;
        }

        SceneManager.UnloadAllScenes();
        m_documents.Clear();
        for (int i = 0; i < candidates.Count; i++)
        {
            (GameScene scene, string path, Guid assetId, byte[] hash) = candidates[i];
            SceneManager.LoadSceneAdditive(scene, makeActive: false);
            m_documents.Add(
                scene.identity.persistentId,
                new SceneDocument(scene, path, assetId, hash));
        }
        SceneDocument? activeDocument = m_documents.Values.FirstOrDefault(document =>
            string.Equals(document.sourcePath, m_pendingActivePath, StringComparison.OrdinalIgnoreCase));
        if (activeDocument is not null && activeDocument.scene.isLoaded)
            SceneManager.SetActiveScene(activeDocument.scene);
        if (m_interactions is not null)
            _ = m_interactions.For(m_interactions.focusedArea).Select();
        m_pendingScenePaths = null;
        m_waitingTypeCatalogVersion = -1;
        m_diagnostics.ResolveRestore();
    }

    private static void DisposeRestoreCandidates(
        IReadOnlyList<(GameScene Scene, string Path, Guid AssetId, byte[] Hash)> candidates)
    {
        for (int i = candidates.Count - 1; i >= 0; i--)
        {
            if (candidates[i].Scene.isDestroyed)
                continue;
            SceneManager.LoadSceneAdditive(candidates[i].Scene, makeActive: false);
            _ = SceneManager.UnloadScene(candidates[i].Scene);
        }
    }

    private void SynchronizeReplacedScenes()
    {
        IReadOnlyList<GameScene> loaded = SceneManager.loadedScenes;
        for (int i = 0; i < loaded.Count; i++)
        {
            GameScene scene = loaded[i];
            if (m_documents.TryGetValue(scene.identity.persistentId, out SceneDocument? document) &&
                !ReferenceEquals(document.scene, scene))
            {
                document.scene = scene;
            }
        }
    }

    internal SceneDocumentSnapshot CaptureDocumentSnapshot(GameScene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        SceneDocument document = GetOrCreateDocument(scene);
        return new SceneDocumentSnapshot(
            scene.identity.persistentId,
            SerializationManager.Serialize(scene),
            document.sourcePath,
            document.sourceAssetId,
            document.savedHash.ToArray(),
            document.isDirty,
            SceneManager.GetSceneIndex(scene));
    }

    internal GameScene RestoreDocumentSnapshot(SceneDocumentSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        GameScene scene = SerializationManager.Deserialize<GameScene>(snapshot.payload);
        SceneManager.LoadSceneAdditive(scene, makeActive: false);
        SceneManager.SetSceneIndex(scene, snapshot.sceneIndex);
        m_documents[scene.identity.persistentId] = new SceneDocument(
            scene,
            snapshot.sourcePath,
            snapshot.sourceAssetId,
            snapshot.savedHash.ToArray())
        {
            isDirty = snapshot.isDirty
        };
        return scene;
    }

    internal bool CloseDocumentForHistory(GameScene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        Guid sceneId = scene.identity.persistentId;
        bool closed = SceneManager.UnloadScene(scene);
        if (closed)
        {
            m_documents.Remove(sceneId);
            m_diagnostics.ResolveSynchronization(sceneId);
        }
        return closed;
    }

    internal void RestoreEditorState(Guid? activeSceneId, Guid? selectedId)
    {
        if (activeSceneId is Guid sceneId && FindEngineObject(sceneId) is GameScene { isLoaded: true } active)
            SceneManager.SetActiveScene(active);
        else if (SceneManager.loadedScenes.Count > 0 && SceneManager.activeScene is null)
            SceneManager.SetActiveScene(SceneManager.loadedScenes[0]);

        if (m_interactions is null)
            return;
        object? target = selectedId is Guid id ? FindEngineObject(id) : null;
        target ??= SceneManager.activeScene;
        _ = m_interactions.For(m_interactions.focusedArea, target).Select();
    }

    private static EngineObject? FindEngineObject(Guid id)
    {
        foreach (GameScene scene in SceneManager.loadedScenes)
        {
            if (scene.identity.persistentId == id)
                return scene;
            foreach (GameObject gameObject in scene.GetObjects())
            {
                if (gameObject.identity.persistentId == id)
                    return gameObject;
                GameComponent? component = gameObject.GetComponents()
                    .FirstOrDefault(value => value.identity.persistentId == id);
                if (component is not null)
                    return component;
            }
            GameSystem? system = scene.GetSystems()
                .FirstOrDefault(value => value.identity.persistentId == id);
            if (system is not null)
                return system;
        }
        return null;
    }

    private static byte[] ComputeSceneHash(GameScene scene)
    {
        var dependencies = new AssetDependencyCollection();
        SerializationContext context = SerializationContext.empty.With(dependencies);
        byte[] payload = SerializationManager.Serialize(scene, context);
        return SHA256.HashData(payload);
    }

    private static string CreateUniquePath(string directory, string name, string extension)
    {
        directory = NormalizePath(directory);
        string fileName = SanitizeFileName(name);
        string candidate = Combine(directory, fileName + extension);
        for (int suffix = 1; AssetManager.TryGetFileSystemEntry(candidate, out _); suffix++)
            candidate = Combine(directory, $"{fileName} {suffix}{extension}");
        return candidate;
    }

    private static string CreateUniqueSceneName(IReadOnlyList<GameScene> scenes)
    {
        const string c_baseName = "Untitled Scene";
        var names = scenes.Select(static scene => scene.name).ToHashSet(StringComparer.Ordinal);
        if (!names.Contains(c_baseName))
            return c_baseName;
        for (int suffix = 1; ; suffix++)
        {
            string candidate = $"{c_baseName} {suffix}";
            if (!names.Contains(candidate))
                return candidate;
        }
    }

    private static string GetAssetName(string relativePath)
    {
        string name = Path.GetFileNameWithoutExtension(relativePath);
        return string.IsNullOrWhiteSpace(name) ? "Untitled" : name;
    }

    private static string SanitizeFileName(string value)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        string sanitized = new((value ?? string.Empty)
            .Trim()
            .Select(character => invalid.Contains(character) ? '_' : character)
            .ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? "Untitled" : sanitized;
    }

    private static string Combine(string directory, string fileName)
        => string.IsNullOrEmpty(directory) ? fileName : $"{directory}/{fileName}";

    private static string NormalizePath(string? path)
        => string.IsNullOrWhiteSpace(path)
            ? string.Empty
            : path.Replace('\\', '/').Trim('/');

    private sealed class SceneDocument(
        GameScene scene,
        string sourcePath,
        Guid sourceAssetId,
        byte[] savedHash)
    {
        public GameScene scene = scene;
        public string sourcePath = sourcePath;
        public Guid sourceAssetId = sourceAssetId;
        public byte[] savedHash = savedHash;
        public bool isDirty;
        public long nextRefreshTimestamp;
    }

    internal sealed record SceneDocumentSnapshot(
        Guid sceneId,
        byte[] payload,
        string sourcePath,
        Guid sourceAssetId,
        byte[] savedHash,
        bool isDirty,
        int sceneIndex);
}
