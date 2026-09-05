using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

using Inno.Assets;
using Inno.Assets.Pipeline;
using Inno.Extensibility.Modules;
using Inno.Core.Identity;
using Inno.Core.Logging;
using Inno.Extensibility.Types;
using Inno.Core.Serialization;
using Inno.Editor.Core;
using Inno.Editor.Interactions;
using Inno.Runtime;
using Inno.Scene;
using Inno.Scene.Components;

namespace Inno.Editor.Scene;

/// <summary>
/// Tracks editor scene documents, their source paths, and serialized dirty state.
/// </summary>
[EditorModule("scene-workspace", order: 200)]
internal sealed class EditorSceneWorkspace :
    EditorModule,
    IEditorGameScenePresentation,
    IEditorSceneWorkspace,
    IEditorScenePlayMode,
    IEditorReloadParticipant
{
    private const double C_DIRTY_REFRESH_SECONDS = 0.1;
    private const string C_SCENE_EXTENSION = ".iscene";
    private const string C_PREFAB_EXTENSION = ".iprefab";

    private readonly Dictionary<Guid, SceneDocument> m_documents = [];
    private readonly ConcurrentQueue<AssetChange> m_sourceChanges = new();
    private readonly AssetPipeline m_assets;
    private readonly EditorSceneDiagnosticPublisher m_diagnostics = new();
    private readonly EditorReloadCoordinator m_reloads;
    private readonly Logger m_log;
    private readonly RuntimeSession m_runtimeSession;
    private readonly SceneStateDiagnosticTracker m_sceneStateDiagnostics;
    private readonly SerializationRegistry m_serialization;
    private readonly IEditorSelectionCoordinator? m_selection;
    private readonly TypeCatalog m_types;

    private bool m_isAttached;
    private bool m_isPreparingPlayMode;
    private PlayModeLease? m_playModeSession;
    private IDisposable? m_reloadIntegration;
    private IDisposable? m_reloadRegistration;
    private string[]? m_pendingScenePaths;
    private string m_pendingActivePath = string.Empty;
    private long m_nextRestoreAttemptTimestamp;
    private long m_waitingTypeCatalogVersion = -1;

    /// <summary>
    /// Creates a scene workspace and optionally enables editor selection coordination.
    /// </summary>
    /// <param name="runtimeSession">
    /// The Edit session whose runtime-owned coroutines are retired during script generation changes.
    /// </param>
    /// <param name="assets">
    /// The authoring asset pipeline that owns scene and prefab documents.
    /// </param>
    /// <param name="types">
    /// The host-owned type catalog that resolves scene element generations.
    /// </param>
    /// <param name="serialization">
    /// The serialization registry used for scene snapshots and history state.
    /// </param>
    /// <param name="reloads">
    /// The host-owned coordinator for atomic editor generation transitions.
    /// </param>
    /// <param name="logs">
    /// The application log router used for scene workspace diagnostics.
    /// </param>
    /// <param name="selection">
    /// The active editor selection coordinator, or <see langword="null"/> when selection is not hosted.
    /// </param>
    internal EditorSceneWorkspace(
        RuntimeSession runtimeSession,
        AssetPipeline assets,
        TypeCatalog types,
        SerializationRegistry serialization,
        EditorReloadCoordinator reloads,
        LogRouter logs,
        IEditorSelectionCoordinator? selection = null)
    {
        m_runtimeSession = runtimeSession ?? throw new ArgumentNullException(nameof(runtimeSession));
        m_assets = assets ?? throw new ArgumentNullException(nameof(assets));
        m_types = types ?? throw new ArgumentNullException(nameof(types));
        m_serialization = serialization ?? throw new ArgumentNullException(nameof(serialization));
        m_reloads = reloads ?? throw new ArgumentNullException(nameof(reloads));
        ArgumentNullException.ThrowIfNull(logs);
        m_log = logs.CreateLogger<EditorSceneWorkspace>();
        m_selection = selection;
        m_sceneStateDiagnostics = new SceneStateDiagnosticTracker(runtimeSession.scenes, types);
    }

    internal SceneWorld world => m_playModeSession?.runtimeWorld ?? m_runtimeSession.scenes;

    internal IDisposable EnterPresentationScope()
        => (m_playModeSession?.runtimeSession ?? m_runtimeSession).EnterExecutionScope();

    internal SerializationRegistry serialization => m_serialization;

    internal IAssetReferenceResolver assets => m_assets;

    internal TypeCatalog types => m_types;

    internal TSceneObject? Find<TSceneObject>(Guid persistentId)
        where TSceneObject : IdentityObject
        => world.Find<TSceneObject>(persistentId);

    /// <summary>
    /// Gets all scenes currently available to editor features.
    /// </summary>
    public IReadOnlyList<GameScene> scenes => world.loadedScenes;

    /// <summary>
    /// Gets the active scene, or <see langword="null"/> when the workspace contains no scenes.
    /// </summary>
    public GameScene? activeScene => world.activeScene;

    /// <summary>
    /// Gets whether the loaded scenes represent editable documents that may be persisted.
    /// </summary>
    public bool canPersist => !m_isPreparingPlayMode && m_playModeSession is null;

    /// <summary>
    /// Captures the scene set that represents the game for the current Editor frame.
    /// </summary>
    /// <returns>
    /// A coherent snapshot of the Edit world before Play commits, the isolated Play world while it is
    /// active, or the Edit world again after the Play lease has been released.
    /// </returns>
    public EditorScenePresentationSnapshot Capture()
    {
        SceneWorld presentedWorld = m_playModeSession?.runtimeWorld ?? m_runtimeSession.scenes;
        GameScene[] scenes = presentedWorld.loadedScenes
            .Where(static scene => !scene.isDestroyed)
            .ToArray();
        GameScene? activeScene = presentedWorld.activeScene;
        if (activeScene is { isDestroyed: true } ||
            activeScene is not null && !scenes.Contains(activeScene))
        {
            activeScene = null;
        }
        return new EditorScenePresentationSnapshot(scenes, activeScene);
    }

    /// <summary>
    /// Makes one loaded scene the active editor document without changing scene order.
    /// </summary>
    /// <param name="scene">
    /// The loaded scene to activate.
    /// </param>
    public void SetActiveScene(GameScene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        using IDisposable scope = EnterPresentationScope();
        world.SetActiveScene(scene);
    }

    internal void DisposeUnattached()
    {
        m_playModeSession?.Dispose();
        ((IDisposable)this).Dispose();
    }

    /// <summary>
    /// Creates and loads a uniquely named unsaved scene alongside the currently loaded scenes.
    /// </summary>
    /// <returns>
    /// The newly created active scene.
    /// </returns>
    internal GameScene CreateScene()
    {
        using IDisposable scope = EnterPresentationScope();
        string name = CreateUniqueSceneName(world.loadedScenes);
        GameScene scene = world.LoadNewSceneAdditive(name);
        if (canPersist)
        {
            m_documents.Add(
                scene.identity.persistentId,
                new SceneDocument(scene, string.Empty, Guid.Empty, []));
        }
        return scene;
    }

    /// <summary>
    /// Applies queued asset path changes to loaded scene documents and prefab instances.
    /// This method must be called from the editor main thread.
    /// </summary>
    internal void Refresh()
    {
        SynchronizeReplacedScenes();
        ApplyPendingSourceChanges();

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
                    m_log.Write(LogLevel.Error, "Scene document synchronization failed: {0}", [exception]);
            }
        }
        m_diagnostics.RetainSynchronizationTargets(synchronizedScenes);
        m_sceneStateDiagnostics.Reconcile();
    }

    /// <summary>
    /// Gets whether a scene contains unsaved serialized changes.
    /// </summary>
    /// <param name="scene">
    /// Scene to inspect.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the scene has no source path or differs from its saved baseline.
    /// </returns>
    public bool IsDirty(GameScene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        if (m_playModeSession is not null)
        {
            EnsurePresentedScene(scene);
            return false;
        }
        ApplyPendingSourceChanges();
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
            document.isDirty = HasSerializedChanges(scene, document);
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
    /// <param name="scene">
    /// Scene to save.
    /// </param>
    /// <param name="currentDirectory">
    /// Fallback asset directory for a new scene.
    /// </param>
    /// <returns>
    /// The saved source-relative path.
    /// </returns>
    public string Save(GameScene scene, string currentDirectory)
    {
        ArgumentNullException.ThrowIfNull(scene);
        EnsureCanPersist();
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
    /// <param name="scene">
    /// Scene to save.
    /// </param>
    /// <param name="currentDirectory">
    /// Target asset directory.
    /// </param>
    /// <returns>
    /// The saved source-relative path.
    /// </returns>
    public string SaveToDirectory(GameScene scene, string currentDirectory)
    {
        ArgumentNullException.ThrowIfNull(scene);
        EnsureCanPersist();
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
    /// <param name="gameObject">
    /// Prefab root.
    /// </param>
    /// <param name="currentDirectory">
    /// Target asset directory.
    /// </param>
    /// <returns>
    /// The saved source-relative path.
    /// </returns>
    public string SavePrefab(GameObject gameObject, string currentDirectory)
    {
        ArgumentNullException.ThrowIfNull(gameObject);
        EnsureCanPersist();
        string relativePath = CreateUniquePath(currentDirectory, gameObject.name, C_PREFAB_EXTENSION);
        if (!m_assets.Save(
                AssetPath.Project(relativePath),
                PrefabAsset.Capture(gameObject, m_serialization, m_assets)))
            throw new InvalidOperationException($"No asset importer could save prefab '{relativePath}'.");
        return relativePath;
    }

    /// <summary>
    /// Opens a scene asset additively as the active editor scene.
    /// </summary>
    /// <param name="relativePath">
    /// Scene asset source-relative path.
    /// </param>
    /// <returns>
    /// The existing loaded instance or the newly loaded scene.
    /// </returns>
    public GameScene Open(string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        EnsureCanPersist();
        string normalizedPath = NormalizePath(relativePath);
        SceneDocument? existing = m_documents.Values.FirstOrDefault(document =>
            document.scene.isLoaded &&
            !document.scene.isDestroyed &&
            string.Equals(document.sourcePath, normalizedPath, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            m_runtimeSession.scenes.SetActiveScene(existing.scene);
            return existing.scene;
        }

        SceneAsset asset = m_assets.Load<SceneAsset>(AssetPath.Parse(normalizedPath));
        GameScene scene = asset.Instantiate(m_serialization, m_assets);
        scene.name = GetAssetName(normalizedPath);
        byte[] savedHash = ComputeSceneHash(scene);
        m_runtimeSession.scenes.LoadSceneAdditive(scene);
        m_documents.Add(
            scene.identity.persistentId,
            new SceneDocument(scene, normalizedPath, asset.identity.persistentId, savedHash));
        return scene;
    }

    /// <summary>
    /// Closes a loaded scene and removes its editor document state without deleting its source asset.
    /// </summary>
    /// <param name="scene">
    /// Loaded scene to close.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the scene was loaded and closed.
    /// </returns>
    internal bool CloseScene(GameScene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        if (scene.isDestroyed)
            return false;
        using IDisposable scope = EnterPresentationScope();
        Guid sceneId = scene.identity.persistentId;
        bool closed = world.UnloadScene(scene);
        if (closed && canPersist)
        {
            m_documents.Remove(sceneId);
            m_diagnostics.ResolveSynchronization(sceneId);
        }
        return closed;
    }

    /// <summary>
    /// Tries to get the current source-relative asset path of a saved scene.
    /// </summary>
    /// <param name="scene">
    /// Scene whose document path is requested.
    /// </param>
    /// <param name="relativePath">
    /// The saved source path when available.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the scene is backed by a scene asset.
    /// </returns>
    public bool TryGetSourcePath(GameScene scene, out string relativePath)
    {
        ArgumentNullException.ThrowIfNull(scene);
        if (m_playModeSession is PlayModeLease playModeSession)
        {
            EnsurePresentedScene(scene);
            relativePath = playModeSession.TryGetSnapshot(
                scene.identity.persistentId,
                out SceneDocumentSnapshot snapshot)
                ? snapshot.sourcePath
                : string.Empty;
            return !string.IsNullOrEmpty(relativePath);
        }
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
    internal void Clear()
    {
        m_documents.Clear();
        m_diagnostics.RetainSynchronizationTargets(new HashSet<Guid>());
        while (m_sourceChanges.TryDequeue(out _))
        {
        }
    }

    /// <summary>
    /// Captures an immutable snapshot of the current observable state.
    /// </summary>
    /// <param name="state">
    /// The lifecycle or domain state applied by this operation.
    /// </param>
    protected override void Capture(EditorState state)
    {
        if (m_playModeSession is PlayModeLease playModeSession)
        {
            playModeSession.Capture(state);
            return;
        }
        if (m_pendingScenePaths is not null)
        {
            state.Set("openScenes", m_pendingScenePaths);
            state.Set("activeScene", m_pendingActivePath);
            return;
        }
        string[] scenePaths = m_runtimeSession.scenes.loadedScenes
            .Select(scene => TryGetSourcePath(scene, out string path) ? path : string.Empty)
            .Where(static path => !string.IsNullOrEmpty(path))
            .ToArray();
        state.Set("openScenes", scenePaths);
        if (m_runtimeSession.scenes.activeScene is GameScene active && TryGetSourcePath(active, out string activePath))
            state.Set("activeScene", activePath);
    }

    /// <summary>
    /// Restores the supplied snapshot while preserving current invariants.
    /// </summary>
    /// <param name="state">
    /// The lifecycle or domain state applied by this operation.
    /// </param>
    protected override void Restore(EditorState state)
    {
        string[] paths = state.Get("openScenes", Array.Empty<string>());
        m_pendingActivePath = state.Get("activeScene", string.Empty);
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
    /// <param name="context">
    /// The shared editor context for the active runtime.
    /// </param>
    protected override void OnStart(EditorContext context)
    {
        if (m_isAttached)
            return;
        m_assets.Changed += OnAssetDatabaseChanged;
        m_reloadIntegration = SceneReloadIntegration.Acquire(
            m_runtimeSession,
            m_serialization,
            m_reloads);
        m_reloadRegistration = m_reloads.Register(this);
        m_isAttached = true;
        m_sceneStateDiagnostics.Reconcile(force: true);
    }

    /// <summary>
    /// Refreshes source synchronization for loaded editor documents.
    /// </summary>
    /// <param name="context">
    /// The shared editor context containing current frame state.
    /// </param>
    protected override void OnUpdate(EditorContext context)
    {
        if (m_playModeSession is not null)
        {
            m_sceneStateDiagnostics.Reconcile();
            return;
        }
        TryRestorePendingScenes();
        Refresh();
    }

    /// <summary>
    /// Detaches the workspace from Asset Database changes and releases any scene it created for the editor.
    /// </summary>
    /// <param name="context">
    /// The shared editor context for the runtime being stopped.
    /// </param>
    protected override void OnStop(EditorContext context)
    {
        if (!m_isAttached)
            return;
        try
        {
            m_playModeSession?.Dispose();
        }
        finally
        {
            m_assets.Changed -= OnAssetDatabaseChanged;
            m_reloadRegistration?.Dispose();
            m_reloadRegistration = null;
            m_runtimeSession.scenes.UnloadAllScenes();
            m_sceneStateDiagnostics.Reconcile(force: true);
            m_reloadIntegration?.Dispose();
            m_reloadIntegration = null;
            m_isAttached = false;
            Clear();
        }
    }

    /// <summary>
    /// Releases resources retained by this feature after it has stopped.
    /// </summary>
    protected override void OnDispose()
    {
        m_reloadRegistration?.Dispose();
        m_reloadRegistration = null;
        m_reloadIntegration?.Dispose();
        m_reloadIntegration = null;
        m_diagnostics.Dispose();
    }

    IEditorReloadTransaction IEditorReloadParticipant.Capture(AssemblyReloadContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (m_playModeSession is not null)
        {
            return new WorkspaceReloadTransaction(
                this,
                Array.Empty<ReloadDocumentState>(),
                preserveDocumentBaselines: true);
        }
        ApplyPendingSourceChanges();
        var documents = new List<ReloadDocumentState>(m_documents.Count);
        foreach ((Guid sceneId, SceneDocument document) in m_documents)
        {
            GameScene scene = document.scene;
            if (scene.isDestroyed)
                continue;
            SynchronizeSource(scene, document);
            bool wasDirty = string.IsNullOrEmpty(document.sourcePath) ||
                            !string.Equals(
                                scene.name,
                                GetAssetName(document.sourcePath),
                                StringComparison.Ordinal) ||
                            HasSerializedChanges(scene, document);
            documents.Add(new ReloadDocumentState(
                sceneId,
                document.savedHash.ToArray(),
                document.isDirty,
                document.nextRefreshTimestamp,
                wasDirty));
        }
        return new WorkspaceReloadTransaction(this, documents, preserveDocumentBaselines: false);
    }

    void IEditorReloadParticipant.RefreshDiagnostics()
        => m_sceneStateDiagnostics.Reconcile(force: true);

    private void SaveSceneAtPath(GameScene scene, string relativePath)
    {
        scene.name = GetAssetName(relativePath);
        bool exists = m_assets.TryLoad(AssetPath.Parse(relativePath), out SceneAsset? sceneAsset);
        sceneAsset ??= new SceneAsset();
        sceneAsset.CaptureFrom(scene, m_serialization, m_assets);
        bool saved = exists
            ? m_assets.Save(sceneAsset)
            : m_assets.Save(AssetPath.Parse(relativePath), sceneAsset);
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

    private void SynchronizeSource(GameScene scene, SceneDocument document)
    {
        if (document.sourceAssetId == Guid.Empty)
            return;
        if (!m_assets.TryLoad(document.sourceAssetId, out SceneAsset? asset) ||
            asset is null ||
            asset.isMissing)
        {
            document.sourcePath = string.Empty;
            document.sourceAssetId = Guid.Empty;
            document.isDirty = true;
            return;
        }

        string sourcePath = NormalizePath(asset.assetPath.ToString());
        string sourceName = GetAssetName(sourcePath);
        bool pathChanged = !string.Equals(document.sourcePath, sourcePath, StringComparison.Ordinal);
        if (!pathChanged)
            return;

        bool wasDirty = HasSerializedChanges(scene, document);
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
        if (m_assets.TryGetFileSystemEntry(AssetPath.Parse(newPath), out Inno.Assets.Pipeline.AssetFileEntry entry) &&
            entry.isDirectory)
        {
            ApplyDirectoryRename(oldPath, newPath);
            return;
        }
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

            RelocateSceneDocument(document, newPath);
        }
    }

    private void ApplyDirectoryRename(string oldPath, string newPath)
    {
        string oldPrefix = oldPath + "/";
        foreach (SceneDocument document in m_documents.Values)
        {
            if (document.scene.isDestroyed ||
                !document.sourcePath.StartsWith(oldPrefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string suffix = document.sourcePath[oldPrefix.Length..];
            RelocateSceneDocument(document, Combine(newPath, suffix));
        }
    }

    private void RelocateSceneDocument(SceneDocument document, string newPath)
    {
        bool wasDirty = HasSerializedChanges(document.scene, document);
        document.sourcePath = newPath;
        document.scene.name = GetAssetName(newPath);
        if (m_assets.TryGetPersistentId(AssetPath.Parse(newPath), out Guid sourceAssetId))
            document.sourceAssetId = sourceAssetId;
        if (!wasDirty)
            document.savedHash = ComputeSceneHash(document.scene);
        document.isDirty = wasDirty;
        document.nextRefreshTimestamp = Stopwatch.GetTimestamp() +
                                        (long)(Stopwatch.Frequency * C_DIRTY_REFRESH_SECONDS);
    }

    private string RenameSceneSourceIfNeeded(GameScene scene, SceneDocument document)
    {
        string currentPath = NormalizePath(document.sourcePath);
        string directory = NormalizePath(Path.GetDirectoryName(currentPath));
        string targetPath = Combine(directory, SanitizeFileName(scene.name) + C_SCENE_EXTENSION);
        if (string.Equals(currentPath, targetPath, StringComparison.Ordinal))
            return currentPath;
        if (m_assets.TryGetFileSystemEntry(AssetPath.Parse(targetPath), out _))
        {
            throw new IOException(
                $"Scene asset '{targetPath}' already exists. Choose a different scene name before saving.");
        }

        m_assets.Move(AssetPath.Parse(currentPath), AssetPath.Parse(targetPath));
        document.sourcePath = targetPath;
        return targetPath;
    }

    private void ApplyPrefabRename(string oldPath, string newPath)
    {
        if (!m_assets.TryGetPersistentId(AssetPath.Parse(newPath), out Guid sourceAssetId))
            return;
        string oldName = GetAssetName(oldPath);
        string newName = GetAssetName(newPath);
        IReadOnlyList<GameScene> scenes = m_runtimeSession.scenes.loadedScenes;
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

    private void ApplyPendingSourceChanges()
    {
        while (m_sourceChanges.TryDequeue(out AssetChange change))
        {
            try
            {
                if (change.kind == AssetChangeKind.Moved)
                    ApplyRename(
                        change.previousAssetPath?.ToString() ?? string.Empty,
                        change.assetPath.ToString());
            }
            catch (Exception exception)
            {
                m_log.Write(LogLevel.Error, "Editor asset rename synchronization failed: {0}", [exception]);
            }
        }
    }

    private void TryRestorePendingScenes()
    {
        if (m_pendingScenePaths is null)
        {
            m_diagnostics.ResolveRestore();
            return;
        }
        long typeCatalogVersion = m_types.current.version;
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
                if (!m_assets.TryGetFileSystemEntry(AssetPath.Parse(path), out _))
                {
                    string absolutePath = Path.Combine(
                        m_assets.assetRoot,
                        path.Replace('/', Path.DirectorySeparatorChar));
                    if (File.Exists(absolutePath))
                    {
                        waitingForSourceIndex = true;
                        break;
                    }
                    m_log.Write(LogLevel.Warn, "Editor scene workspace skipped missing scene '{0}'.", [path]);
                    continue;
                }
                SceneAsset asset = m_assets.Load<SceneAsset>(AssetPath.Parse(path));
                GameScene scene = asset.Instantiate(m_serialization, m_assets);
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
                m_log.Write(
                    LogLevel.Error,
                    "Editor scene workspace cannot restore saved scenes because {0}",
                    [message]);
            }
            return;
        }
        catch (Exception exception)
        {
            DisposeRestoreCandidates(candidates);
            m_waitingTypeCatalogVersion = typeCatalogVersion;
            if (m_diagnostics.PublishRestoreFailure("SCENE-RESTORE", exception.Message))
                m_log.Write(
                    LogLevel.Error,
                    "Editor scene workspace could not restore saved scenes: {0}",
                    [exception]);
            return;
        }
        if (waitingForSourceIndex)
        {
            DisposeRestoreCandidates(candidates);
            return;
        }

        m_runtimeSession.scenes.UnloadAllScenes();
        m_documents.Clear();
        for (int i = 0; i < candidates.Count; i++)
        {
            (GameScene scene, string path, Guid assetId, byte[] hash) = candidates[i];
            m_runtimeSession.scenes.LoadSceneAdditive(scene, makeActive: false);
            m_documents.Add(
                scene.identity.persistentId,
                new SceneDocument(scene, path, assetId, hash));
        }
        SceneDocument? activeDocument = m_documents.Values.FirstOrDefault(document =>
            string.Equals(document.sourcePath, m_pendingActivePath, StringComparison.OrdinalIgnoreCase));
        if (activeDocument is not null && activeDocument.scene.isLoaded)
            m_runtimeSession.scenes.SetActiveScene(activeDocument.scene);
        m_selection?.SetSelection(null);
        m_pendingScenePaths = null;
        m_waitingTypeCatalogVersion = -1;
        m_diagnostics.ResolveRestore();
    }

    private void DisposeRestoreCandidates(
        IReadOnlyList<(GameScene Scene, string Path, Guid AssetId, byte[] Hash)> candidates)
    {
        for (int i = candidates.Count - 1; i >= 0; i--)
        {
            if (candidates[i].Scene.isDestroyed)
                continue;
            m_runtimeSession.scenes.LoadSceneAdditive(candidates[i].Scene, makeActive: false);
            _ = m_runtimeSession.scenes.UnloadScene(candidates[i].Scene);
        }
    }

    private void SynchronizeReplacedScenes()
    {
        IReadOnlyList<GameScene> loaded = m_runtimeSession.scenes.loadedScenes;
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
        using IDisposable scope = EnterPresentationScope();
        EnsurePresentedScene(scene);
        SceneDocument? document = null;
        SceneDocumentSnapshot? playBaseline = null;
        if (m_playModeSession is PlayModeLease playModeSession)
        {
            if (playModeSession.TryGetSnapshot(
                    scene.identity.persistentId,
                    out SceneDocumentSnapshot snapshot))
            {
                playBaseline = snapshot;
            }
        }
        else
            document = GetOrCreateDocument(scene);
        SerializationContext serializationContext = SerializationContext.empty
            .With<IAssetReferenceResolver>(m_assets);
        return new SceneDocumentSnapshot(
            scene.identity.persistentId,
            m_serialization.Serialize(scene, serializationContext),
            document?.sourcePath ?? playBaseline?.sourcePath ?? string.Empty,
            document?.sourceAssetId ?? playBaseline?.sourceAssetId ?? Guid.Empty,
            document?.savedHash.ToArray() ?? playBaseline?.savedHash.ToArray() ?? [],
            document?.isDirty ?? false,
            world.GetSceneIndex(scene),
            document?.nextRefreshTimestamp ?? 0);
    }

    internal GameScene RestoreDocumentSnapshot(SceneDocumentSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        using IDisposable scope = EnterPresentationScope();
        SerializationContext serializationContext = SerializationContext.empty
            .With<IAssetReferenceResolver>(m_assets);
        GameScene scene = m_serialization.Deserialize<GameScene>(snapshot.payload, serializationContext);
        world.LoadSceneAdditive(scene, makeActive: false);
        world.SetSceneIndex(scene, snapshot.sceneIndex);
        if (canPersist)
        {
            m_documents[scene.identity.persistentId] = new SceneDocument(
                scene,
                snapshot.sourcePath,
                snapshot.sourceAssetId,
                snapshot.savedHash.ToArray())
            {
                isDirty = snapshot.isDirty,
                nextRefreshTimestamp = snapshot.nextRefreshTimestamp
            };
        }
        return scene;
    }

    IDisposable IEditorScenePlayMode.BeginPlayMode(RuntimeSession runtimeSession)
    {
        ArgumentNullException.ThrowIfNull(runtimeSession);
        if (runtimeSession.options.kind != RuntimeSessionKind.Play)
        {
            throw new ArgumentException(
                "An editor Play Mode scene session requires a Play runtime session.",
                nameof(runtimeSession));
        }
        SceneWorld runtimeWorld = runtimeSession.scenes;
        if (m_isPreparingPlayMode || m_playModeSession is not null)
            throw new InvalidOperationException("An editor Play Mode scene session is already active.");
        if (runtimeWorld.loadedScenes.Count != 0)
            throw new InvalidOperationException("A Play Mode scene snapshot requires an empty runtime world.");

        m_isPreparingPlayMode = true;
        try
        {
            Refresh();
            SceneDocumentSnapshot[] snapshots = m_runtimeSession.scenes.loadedScenes
                .Select(CaptureDocumentSnapshot)
                .OrderBy(static snapshot => snapshot.sceneIndex)
                .ToArray();
            Guid? activeSceneId = m_runtimeSession.scenes.activeScene?.identity.persistentId;
            Guid? selectedSceneObjectId = GetSelectedSceneObjectId();
            var session = new PlayModeLease(
                this,
                runtimeSession,
                snapshots,
                activeSceneId,
                selectedSceneObjectId);
            MaterializeRuntimeSceneSet(session);
            m_playModeSession = session;
            if (selectedSceneObjectId is not null)
                RestoreSelection(selectedSceneObjectId);
            return session;
        }
        finally
        {
            m_isPreparingPlayMode = false;
        }
    }

    internal bool CloseDocumentForHistory(GameScene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        using IDisposable scope = EnterPresentationScope();
        Guid sceneId = scene.identity.persistentId;
        bool closed = world.UnloadScene(scene);
        if (closed && canPersist)
        {
            m_documents.Remove(sceneId);
            m_diagnostics.ResolveSynchronization(sceneId);
        }
        return closed;
    }

    internal void RestoreActiveScene(Guid? activeSceneId)
    {
        using IDisposable scope = EnterPresentationScope();
        if (activeSceneId is Guid sceneId && FindEngineObject(sceneId) is GameScene { isLoaded: true } active)
            world.SetActiveScene(active);
        else if (world.loadedScenes.Count > 0 && world.activeScene is null)
            world.SetActiveScene(world.loadedScenes[0]);
    }

    internal void RestoreSelection(Guid? selectedId, bool selectActiveSceneWhenMissing = true)
    {
        if (m_selection is null)
            return;
        object? target = selectedId is Guid id ? FindEngineObject(id) : null;
        if (target is null && selectActiveSceneWhenMissing)
            target = world.activeScene;
        m_selection.SetSelection(target);
    }

    private EngineObject? FindEngineObject(Guid id)
        => world.Find<EngineObject>(id);

    private void ReleasePlayModeLease(PlayModeLease session)
    {
        if (!ReferenceEquals(m_playModeSession, session))
            return;
        object? runtimeSelection = m_selection?.selectedTarget;
        Guid? runtimeSelectionId = runtimeSelection is EngineObject { isDestroyed: false } selected
            ? selected.identity.persistentId
            : null;
        m_playModeSession = null;
        Guid? editSelectionId = runtimeSelectionId is Guid currentId &&
                                m_runtimeSession.scenes.Find<EngineObject>(currentId) is not null
            ? currentId
            : session.editSelectionId;
        if (runtimeSelection is EngineObject || runtimeSelection is null && session.editSelectionId is not null)
            RestoreSelection(editSelectionId, selectActiveSceneWhenMissing: false);
    }

    private void MaterializeRuntimeSceneSet(PlayModeLease session)
    {
        RuntimeSession runtimeSession = session.runtimeSession;
        SceneWorld runtimeWorld = runtimeSession.scenes;
        using IDisposable runtimeScope = runtimeSession.EnterExecutionScope();
        try
        {
            IReadOnlyList<SceneDocumentSnapshot> snapshots = session.snapshots;
            SerializationContext serializationContext = SerializationContext.empty
                .With<IAssetReferenceResolver>(m_assets);
            for (int i = 0; i < snapshots.Count; i++)
            {
                SceneDocumentSnapshot snapshot = snapshots[i];
                GameScene runtimeScene = m_serialization.Deserialize<GameScene>(
                    snapshot.payload,
                    serializationContext);
                runtimeWorld.LoadSceneAdditive(runtimeScene, makeActive: false);
                runtimeWorld.SetSceneIndex(runtimeScene, snapshot.sceneIndex);
            }
            if (session.activeSceneId is Guid activeSceneId)
            {
                GameScene? activeScene = runtimeWorld.loadedScenes.FirstOrDefault(
                    scene => scene.identity.persistentId == activeSceneId);
                if (activeScene is not null)
                    runtimeWorld.SetActiveScene(activeScene);
            }
        }
        catch (Exception exception)
        {
            try
            {
                runtimeWorld.UnloadAllScenes();
            }
            catch (Exception cleanupFailure)
            {
                throw new AggregateException(
                    "The Play Mode scene set could not be constructed and its partial state could not be released.",
                    exception,
                    cleanupFailure);
            }
            throw new InvalidOperationException(
                "The Play Mode start snapshot could not be materialized in its isolated runtime world.",
                exception);
        }
    }

    private void EnsureCanPersist()
    {
        if (!canPersist)
        {
            throw new InvalidOperationException(
                "Scene and prefab persistence is unavailable while Play Mode runtime copies are active.");
        }
    }

    private void EnsurePresentedScene(GameScene scene)
    {
        IReadOnlyList<GameScene> presentedScenes = world.loadedScenes;
        for (int i = 0; i < presentedScenes.Count; i++)
        {
            if (ReferenceEquals(presentedScenes[i], scene))
                return;
        }
        throw new InvalidOperationException(
            "The scene is not owned by the Editor's active Edit or Play presentation session.");
    }

    private Guid? GetSelectedSceneObjectId()
        => m_selection?.selectedTarget is EngineObject { isDestroyed: false } selected
            ? selected.identity.persistentId
            : null;

    private byte[] ComputeSceneHash(GameScene scene)
    {
        var dependencies = new AssetDependencyCollection(includeLastKnownPaths: false);
        SerializationContext context = SerializationContext.empty
            .With(dependencies)
            .With<IAssetReferenceResolver>(m_assets);
        byte[] payload = m_serialization.Serialize(scene, context);
        return SHA256.HashData(payload);
    }

    private bool HasSerializedChanges(GameScene scene, SceneDocument document)
        => !ComputeSceneHash(scene).AsSpan().SequenceEqual(document.savedHash);

    private string CreateUniquePath(string directory, string name, string extension)
    {
        directory = NormalizePath(directory);
        string fileName = SanitizeFileName(name);
        string candidate = Combine(directory, fileName + extension);
        for (int suffix = 1; m_assets.TryGetFileSystemEntry(AssetPath.Parse(candidate), out _); suffix++)
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

    private sealed class WorkspaceReloadTransaction(
        EditorSceneWorkspace workspace,
        IReadOnlyList<ReloadDocumentState> documents,
        bool preserveDocumentBaselines) : IEditorReloadTransaction
    {
        /// <summary>
        /// Prepares candidate state without changing the active generation.
        /// </summary>
        public void PrepareForActivation()
        {
        }

        /// <summary>
        /// Applies the prepared state at the caller-controlled commit point.
        /// </summary>
        public void Apply()
        {
            workspace.SynchronizeReplacedScenes();
            if (preserveDocumentBaselines)
                return;
            foreach (ReloadDocumentState state in documents)
            {
                if (!workspace.m_documents.TryGetValue(state.sceneId, out SceneDocument? document))
                    continue;
                if (state.wasDirty)
                {
                    document.isDirty = true;
                    document.nextRefreshTimestamp = 0;
                    continue;
                }

                document.savedHash = workspace.ComputeSceneHash(document.scene);
                document.isDirty = false;
                document.nextRefreshTimestamp = Stopwatch.GetTimestamp() +
                                                (long)(Stopwatch.Frequency * C_DIRTY_REFRESH_SECONDS);
            }
        }

        /// <summary>
        /// Completes the committed operation and releases temporary state.
        /// </summary>
        public void Complete()
        {
        }

        /// <summary>
        /// Restores the state that existed before candidate activation began.
        /// </summary>
        public void RollbackStructure()
            => RestoreBaseline();

        /// <summary>
        /// Restores the state that existed before candidate activation began.
        /// </summary>
        public void RestorePreviousState()
            => RestoreBaseline();

        private void RestoreBaseline()
        {
            workspace.SynchronizeReplacedScenes();
            foreach (ReloadDocumentState state in documents)
            {
                if (!workspace.m_documents.TryGetValue(state.sceneId, out SceneDocument? document))
                    continue;
                document.savedHash = state.savedHash.ToArray();
                document.isDirty = state.isDirty;
                document.nextRefreshTimestamp = state.nextRefreshTimestamp;
            }
        }
    }

    private readonly record struct ReloadDocumentState(
        Guid sceneId,
        byte[] savedHash,
        bool isDirty,
        long nextRefreshTimestamp,
        bool wasDirty);

    private sealed class SceneDocument(
        GameScene scene,
        string sourcePath,
        Guid sourceAssetId,
        byte[] savedHash)
    {
        /// <summary>
        /// The scene value used as part of this type's public representation.
        /// </summary>
        public GameScene scene = scene;
        /// <summary>
        /// The source path value used as part of this type's public representation.
        /// </summary>
        public string sourcePath = sourcePath;
        /// <summary>
        /// The source asset id value used as part of this type's public representation.
        /// </summary>
        public Guid sourceAssetId = sourceAssetId;
        /// <summary>
        /// The saved hash value used as part of this type's public representation.
        /// </summary>
        public byte[] savedHash = savedHash;
        /// <summary>
        /// The is dirty value used as part of this type's public representation.
        /// </summary>
        public bool isDirty;
        /// <summary>
        /// The next refresh timestamp value used as part of this type's public representation.
        /// </summary>
        public long nextRefreshTimestamp;
    }

    private sealed class PlayModeLease(
        EditorSceneWorkspace workspace,
        RuntimeSession playSession,
        SceneDocumentSnapshot[] sceneSnapshots,
        Guid? activeScene,
        Guid? selectedSceneObject) : IDisposable
    {
        private readonly Dictionary<Guid, SceneDocumentSnapshot> m_snapshotBySceneId =
            sceneSnapshots.ToDictionary(static snapshot => snapshot.sceneId);

        internal IReadOnlyList<SceneDocumentSnapshot> snapshots { get; } = sceneSnapshots;

        internal Guid? activeSceneId { get; } = activeScene;

        internal Guid? editSelectionId { get; } = selectedSceneObject;

        internal RuntimeSession runtimeSession { get; } = playSession;

        internal SceneWorld runtimeWorld => runtimeSession.scenes;

        private bool m_disposed;

        /// <summary>
        /// Releases the resources owned by this instance.
        /// </summary>
        public void Dispose()
        {
            if (m_disposed)
                return;
            m_disposed = true;
            workspace.ReleasePlayModeLease(this);
        }

        internal void Capture(EditorState state)
        {
            state.Set(
                "openScenes",
                sceneSnapshots
                    .Select(static snapshot => snapshot.sourcePath)
                    .Where(static path => !string.IsNullOrEmpty(path))
                    .ToArray());
            string activePath = activeSceneId is Guid activeId &&
                                m_snapshotBySceneId.TryGetValue(activeId, out SceneDocumentSnapshot? active)
                ? active.sourcePath
                : string.Empty;
            state.Set("activeScene", activePath);
        }

        internal bool TryGetSnapshot(Guid sceneId, out SceneDocumentSnapshot snapshot)
            => m_snapshotBySceneId.TryGetValue(sceneId, out snapshot!);
    }

    internal sealed record SceneDocumentSnapshot(
        Guid sceneId,
        byte[] payload,
        string sourcePath,
        Guid sourceAssetId,
        byte[] savedHash,
        bool isDirty,
        int sceneIndex,
        long nextRefreshTimestamp = 0);
}
