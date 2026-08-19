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
using Inno.Core.Serialization;
using Inno.Editor.Core;
using Inno.Editor.Core.Commands;
using Inno.Engine.Scene;
using Inno.Engine.Scene.Assets;

namespace Inno.Editor.Scene.Workspace;

/// <summary>
/// Tracks editor scene documents, their source paths, and serialized dirty state.
/// </summary>
[EditorModule(order: 200)]
public sealed class EditorSceneWorkspace : EditorModule
{
    private const double C_DIRTY_REFRESH_SECONDS = 0.1;
    private const string C_SCENE_EXTENSION = ".innoscene";
    private const string C_PREFAB_EXTENSION = ".innoprefab";

    private readonly Dictionary<Guid, SceneDocument> m_documents = [];
    private readonly ConcurrentQueue<AssetChange> m_sourceChanges = new();

    private GameScene? m_ownedScene;
    private bool m_isAttached;

    /// <summary>Gets the active presentation-neutral hierarchy rename session.</summary>
    public EditorRenameSession? rename { get; private set; }

    /// <summary>Gets all scenes currently available to editor features.</summary>
    public IReadOnlyList<GameScene> scenes => SceneManager.loadedScenes;

    /// <summary>Gets the active scene.</summary>
    /// <exception cref="InvalidOperationException">Thrown when no active scene exists.</exception>
    public GameScene activeScene => SceneManager.activeScene
        ?? throw new InvalidOperationException("The editor does not have an active scene.");

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

        foreach (SceneDocument document in m_documents.Values)
        {
            if (document.scene.isDestroyed)
                continue;
            try
            {
                SynchronizeSource(document.scene, document);
            }
            catch (Exception exception)
            {
                document.isDirty = true;
                Log.Error("Scene document synchronization failed: {0}", exception);
            }
        }
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
        if (scene.isLoaded && SceneManager.loadedScenes.Count <= 1)
            return false;
        Guid sceneId = scene.identity.persistentId;
        bool closed = SceneManager.UnloadScene(scene);
        if (closed)
            m_documents.Remove(sceneId);
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
        while (m_sourceChanges.TryDequeue(out _))
        {
        }
    }

    /// <summary>Begins renaming a game object.</summary>
    public void BeginRename(GameObject gameObject)
    {
        ArgumentNullException.ThrowIfNull(gameObject);
        CancelRename();
        rename = new EditorRenameSession(
            gameObject,
            gameObject.name,
            _ => EditorValidationResult.valid,
            value => gameObject.name = string.IsNullOrWhiteSpace(value)
                ? "GameObject"
                : value.Trim());
    }

    /// <summary>Cancels the active hierarchy rename.</summary>
    public void CancelRename()
    {
        rename?.Cancel();
        rename = null;
    }

    /// <summary>Attaches the workspace to asset changes and ensures an editable scene exists.</summary>
    protected override void OnStart(EditorContext context)
    {
        if (m_isAttached)
            return;
        AssetManager.Changed += OnAssetDatabaseChanged;
        if (!SceneManager.hasActiveScene)
            m_ownedScene = SceneManager.LoadNewScene();
        m_isAttached = true;
    }

    /// <summary>Detaches the workspace and releases any scene it created for the editor.</summary>
    protected override void OnUpdate(EditorContext context)
    {
        Refresh();
        if (rename?.isCompleted == true)
            rename = null;
    }

    /// <inheritdoc />
    protected override void OnStop(EditorContext context)
    {
        if (!m_isAttached)
            return;
        AssetManager.Changed -= OnAssetDatabaseChanged;
        if (m_ownedScene is not null)
            SceneManager.UnloadAllScenes();
        m_ownedScene = null;
        m_isAttached = false;
        CancelRename();
        Clear();
    }

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
            return document;
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
        for (int i = 0; i < changeSet.changes.Count; i++)
            m_sourceChanges.Enqueue(changeSet.changes[i]);
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
}
