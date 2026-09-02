using System;
using System.Collections.Generic;
using System.Threading;
using Inno.Core.Identity;
using Inno.Extensibility.Types;

namespace Inno.Scene;

/// <summary>
/// Owns the loaded scene set and lifecycle state for one isolated runtime session.
/// </summary>
public sealed class SceneWorld : IDisposable
{
    private static readonly AsyncLocal<Scope?> S_CURRENT_SCOPE = new();

    private readonly List<GameScene> m_loadedScenes = [];
    private readonly IdentityAllocator m_identities;
    private readonly SceneTypeCatalog m_types;
    private GameScene? m_activeScene;
    private GameScene[]? m_loadedSceneSnapshot;
    private bool m_disposed;

    /// <summary>
    /// Creates an empty scene world bound to one session identity allocator.
    /// </summary>
    /// <param name="identities">
    /// The allocator that owns identities for scenes and their elements.
    /// </param>
    /// <param name="types">
    /// The host type catalog used to derive scene component and system generations.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="identities"/> is null.
    /// </exception>
    public SceneWorld(IdentityAllocator identities, TypeCatalog types)
    {
        m_identities = identities ?? throw new ArgumentNullException(nameof(identities));
        ArgumentNullException.ThrowIfNull(types);
        m_types = new SceneTypeCatalog(types);
    }

    /// <summary>
    /// Gets the active world bound to the current asynchronous execution context.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the caller is outside a scene execution scope.
    /// </exception>
    internal static SceneWorld current
        => S_CURRENT_SCOPE.Value?.world
            ?? throw new InvalidOperationException(
                "No scene world is bound to the current runtime execution context.");

    internal SceneTypeCatalog typeCatalog => m_types;

    /// <summary>
    /// Gets the scene currently selected for unqualified scene operations.
    /// </summary>
    public GameScene? activeScene => m_activeScene;

    /// <summary>
    /// Gets whether this world contains an active scene.
    /// </summary>
    public bool hasActiveScene => m_activeScene is not null;

    /// <summary>
    /// Gets an immutable snapshot of loaded scenes in hierarchy order.
    /// </summary>
    public IReadOnlyList<GameScene> loadedScenes => Array.AsReadOnly(GetLoadedSceneSnapshot());

    /// <summary>
    /// Resolves a live scene object by its persistent identity within this world.
    /// </summary>
    /// <typeparam name="TObject">
    /// The required scene object contract.
    /// </typeparam>
    /// <param name="persistentId">
    /// The persistent identity to resolve.
    /// </param>
    /// <returns>
    /// The live compatible object, or <see langword="null"/> when this world does not own one.
    /// </returns>
    public TObject? Find<TObject>(Guid persistentId)
        where TObject : IdentityObject
        => m_identities.Get<TObject>(persistentId);

    /// <summary>
    /// Binds this world and its identity allocator to the current asynchronous execution context.
    /// </summary>
    /// <returns>
    /// A strict last-in-first-out execution scope owned by the caller.
    /// </returns>
    /// <exception cref="ObjectDisposedException">
    /// Thrown when this world has been disposed.
    /// </exception>
    public IDisposable EnterScope()
    {
        EnsureActive();
        var scope = new Scope(this, S_CURRENT_SCOPE.Value, m_identities.EnterScope());
        S_CURRENT_SCOPE.Value = scope;
        return scope;
    }

    /// <summary>
    /// Gets the hierarchy index of a loaded scene.
    /// </summary>
    /// <param name="scene">
    /// The loaded scene to locate.
    /// </param>
    /// <returns>
    /// The zero-based hierarchy index.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the scene is not loaded by this world.
    /// </exception>
    public int GetSceneIndex(GameScene scene)
    {
        EnsureActive();
        ArgumentNullException.ThrowIfNull(scene);
        int index = m_loadedScenes.IndexOf(scene);
        return index >= 0
            ? index
            : throw new InvalidOperationException("Only a loaded scene has a hierarchy index.");
    }

    /// <summary>
    /// Moves a loaded scene to a hierarchy index without changing the active scene.
    /// </summary>
    /// <param name="scene">
    /// The loaded scene to move.
    /// </param>
    /// <param name="sceneIndex">
    /// The requested zero-based hierarchy index.
    /// </param>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the scene is not loaded by this world.
    /// </exception>
    public void SetSceneIndex(GameScene scene, int sceneIndex)
    {
        EnsureActive();
        ArgumentNullException.ThrowIfNull(scene);
        int currentIndex = m_loadedScenes.IndexOf(scene);
        if (currentIndex < 0)
            throw new InvalidOperationException("Only a loaded scene can be reordered.");
        int targetIndex = Math.Clamp(sceneIndex, 0, m_loadedScenes.Count - 1);
        if (currentIndex == targetIndex)
            return;
        m_loadedScenes.RemoveAt(currentIndex);
        m_loadedScenes.Insert(targetIndex, scene);
        InvalidateSnapshot();
    }

    /// <summary>
    /// Replaces the loaded set with one scene and makes it active.
    /// </summary>
    /// <param name="scene">
    /// The scene to load.
    /// </param>
    public void LoadScene(GameScene scene)
    {
        EnsureActive();
        ArgumentNullException.ThrowIfNull(scene);
        if (ReferenceEquals(m_activeScene, scene) && m_loadedScenes.Count == 1)
            return;
        UnloadAllScenes();
        m_loadedScenes.Add(scene);
        InvalidateSnapshot();
        m_activeScene = scene;
        scene.Load();
    }

    /// <summary>
    /// Loads a scene alongside the existing scene set.
    /// </summary>
    /// <param name="scene">
    /// The scene to load additively.
    /// </param>
    /// <param name="makeActive">
    /// Whether the loaded scene becomes active.
    /// </param>
    public void LoadSceneAdditive(GameScene scene, bool makeActive = true)
    {
        EnsureActive();
        ArgumentNullException.ThrowIfNull(scene);
        if (!m_loadedScenes.Contains(scene))
        {
            m_loadedScenes.Add(scene);
            InvalidateSnapshot();
            scene.Load();
        }
        if (makeActive || m_activeScene is null)
            m_activeScene = scene;
    }

    /// <summary>
    /// Creates and loads a new active scene.
    /// </summary>
    /// <param name="name">
    /// The initial scene display name.
    /// </param>
    /// <returns>
    /// The newly created and loaded scene.
    /// </returns>
    public GameScene LoadNewScene(string name = "Untitled Scene")
    {
        EnsureActive();
        var scene = new GameScene(m_types, name, persistentId: null);
        LoadScene(scene);
        return scene;
    }

    /// <summary>
    /// Creates and additively loads a new scene.
    /// </summary>
    /// <param name="name">
    /// The initial scene display name.
    /// </param>
    /// <param name="makeActive">
    /// Whether the new scene becomes active.
    /// </param>
    /// <returns>
    /// The newly created and loaded scene.
    /// </returns>
    public GameScene LoadNewSceneAdditive(string name = "Untitled Scene", bool makeActive = true)
    {
        EnsureActive();
        var scene = new GameScene(m_types, name, persistentId: null);
        LoadSceneAdditive(scene, makeActive);
        return scene;
    }

    /// <summary>
    /// Makes one loaded scene active without changing the loaded set.
    /// </summary>
    /// <param name="scene">
    /// The loaded scene to activate.
    /// </param>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the scene is not loaded by this world.
    /// </exception>
    public void SetActiveScene(GameScene scene)
    {
        EnsureActive();
        ArgumentNullException.ThrowIfNull(scene);
        if (!m_loadedScenes.Contains(scene))
            throw new InvalidOperationException("Only a loaded scene can become active.");
        m_activeScene = scene;
    }

    /// <summary>
    /// Moves a live object subtree between two scenes loaded by this world.
    /// </summary>
    /// <param name="gameObject">
    /// The live root object to move.
    /// </param>
    /// <param name="destination">
    /// The loaded destination scene.
    /// </param>
    /// <exception cref="InvalidOperationException">
    /// Thrown when either scene is not loaded or the object is not live.
    /// </exception>
    public void MoveGameObjectToScene(GameObject gameObject, GameScene destination)
    {
        EnsureActive();
        ArgumentNullException.ThrowIfNull(gameObject);
        ArgumentNullException.ThrowIfNull(destination);
        if (!gameObject.isRuntimeValid)
            throw new InvalidOperationException("Only a live GameObject can move between scenes.");
        GameScene source = gameObject.scene;
        if (!m_loadedScenes.Contains(source) || !m_loadedScenes.Contains(destination))
            throw new InvalidOperationException("Both source and destination scenes must be loaded by this world.");
        if (!ReferenceEquals(source, destination))
            source.TransferObjectTo(gameObject, destination);
    }

    /// <summary>
    /// Unloads the active scene when one exists.
    /// </summary>
    public void UnloadActiveScene()
    {
        EnsureActive();
        if (m_activeScene is GameScene scene)
            UnloadScene(scene);
    }

    /// <summary>
    /// Unloads one scene and selects another loaded scene when necessary.
    /// </summary>
    /// <param name="scene">
    /// The scene to unload.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the scene was loaded by this world; otherwise, <see langword="false"/>.
    /// </returns>
    public bool UnloadScene(GameScene scene)
    {
        EnsureActive();
        ArgumentNullException.ThrowIfNull(scene);
        if (!m_loadedScenes.Remove(scene))
            return false;
        InvalidateSnapshot();
        try
        {
            scene.Unload();
        }
        finally
        {
            if (ReferenceEquals(m_activeScene, scene))
                m_activeScene = m_loadedScenes.Count > 0 ? m_loadedScenes[^1] : null;
        }
        return true;
    }

    /// <summary>
    /// Unloads every scene while preserving the first lifecycle failure.
    /// </summary>
    public void UnloadAllScenes()
    {
        EnsureActive();
        Exception? firstException = null;
        for (int index = m_loadedScenes.Count - 1; index >= 0; index--)
        {
            try
            {
                m_loadedScenes[index].Unload();
            }
            catch (Exception exception)
            {
                firstException ??= exception;
            }
        }
        m_loadedScenes.Clear();
        InvalidateSnapshot();
        m_activeScene = null;
        if (firstException is not null)
            throw new InvalidOperationException("One or more scenes failed while unloading.", firstException);
    }

    /// <summary>
    /// Advances fixed-step lifecycle callbacks for every loaded scene.
    /// </summary>
    /// <param name="fixedDeltaTime">
    /// The fixed simulation interval in seconds.
    /// </param>
    public void FixedUpdate(float fixedDeltaTime)
    {
        EnsureActive();
        GameScene[] scenes = GetLoadedSceneSnapshot();
        for (int index = 0; index < scenes.Length; index++)
            scenes[index].FixedUpdate(fixedDeltaTime);
    }

    /// <summary>
    /// Advances variable-step lifecycle callbacks for every loaded scene.
    /// </summary>
    /// <param name="deltaTime">
    /// The elapsed frame time in seconds.
    /// </param>
    public void Update(float deltaTime)
    {
        EnsureActive();
        GameScene[] scenes = GetLoadedSceneSnapshot();
        for (int index = 0; index < scenes.Length; index++)
            scenes[index].Update(deltaTime);
    }

    /// <summary>
    /// Advances late lifecycle callbacks for every loaded scene.
    /// </summary>
    /// <param name="deltaTime">
    /// The elapsed frame time in seconds.
    /// </param>
    public void LateUpdate(float deltaTime)
    {
        EnsureActive();
        GameScene[] scenes = GetLoadedSceneSnapshot();
        for (int index = 0; index < scenes.Length; index++)
            scenes[index].LateUpdate(deltaTime);
    }

    /// <summary>
    /// Unloads every scene and permanently releases this world.
    /// </summary>
    public void Dispose()
    {
        if (m_disposed)
            return;
        using IDisposable scope = EnterScope();
        try
        {
            UnloadAllScenes();
        }
        finally
        {
            m_types.Dispose();
            m_disposed = true;
        }
    }

    private GameScene[] GetLoadedSceneSnapshot()
        => m_loadedSceneSnapshot ??= [.. m_loadedScenes];

    private void InvalidateSnapshot()
        => m_loadedSceneSnapshot = null;

    private void EnsureActive()
        => ObjectDisposedException.ThrowIf(m_disposed, this);

    private sealed class Scope(
        SceneWorld world,
        Scope? parent,
        IDisposable identityScope) : IDisposable
    {
        private bool m_disposed;

        internal SceneWorld world { get; } = world;

        /// <summary>
        /// Releases the resources owned by this instance.
        /// </summary>
        public void Dispose()
        {
            if (m_disposed)
                return;
            if (!ReferenceEquals(S_CURRENT_SCOPE.Value, this))
                throw new InvalidOperationException("Scene execution scopes must be disposed in reverse order.");
            m_disposed = true;
            S_CURRENT_SCOPE.Value = parent;
            identityScope.Dispose();
        }
    }
}
