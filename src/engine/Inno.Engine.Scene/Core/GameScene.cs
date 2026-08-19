using System;
using System.Collections.Generic;
using System.Linq;

using Inno.Assets.Core;
using Inno.Core.Serialization;
using Inno.Engine.Scene.Components;

namespace Inno.Engine.Scene;

/// <summary>
/// Owns a runtime scene, including its objects, components, hierarchy, and systems.
/// </summary>
[RequiresSerializationConverter]
public sealed class GameScene : EngineObject, ISerializable
{
    private readonly SceneStore m_store = new();
    private readonly HierarchyService m_hierarchy;
    private readonly ActivationService m_activation = new();
    private readonly SceneSystemScheduler m_systems;
    private string m_name;
    private bool m_isLoaded;
    private bool m_isUnloading;
    private AssetObject? m_sourceAsset;

    /// <summary>
    /// Creates an empty scene.
    /// </summary>
    /// <param name="name">Initial scene name.</param>
    public GameScene(string name = "Untitled Scene")
        : this(name, persistentId: null)
    {
    }

    internal GameScene(string name, Guid? persistentId)
    {
        m_name = name ?? string.Empty;
        m_hierarchy = new HierarchyService(this);
        m_systems = new SceneSystemScheduler(this);
        RegisterIdentity(persistentId);
    }

    /// <summary>
    /// Gets or sets the scene display name.
    /// </summary>
    public string name
    {
        get => m_name;
        set
        {
            EnsureNotDestroyed();
            m_name = value ?? string.Empty;
        }
    }

    /// <summary>
    /// Gets whether this scene is currently owned by <see cref="SceneManager"/>.
    /// </summary>
    public bool isLoaded => m_isLoaded;

    /// <summary>
    /// Creates a game object with a mandatory transform component.
    /// </summary>
    /// <param name="name">Initial game object name.</param>
    /// <returns>The created game object.</returns>
    public GameObject CreateObject(string name = "GameObject")
        => CreateObject(name, persistentId: null, transformPersistentId: null, invokeReset: true);

    /// <summary>
    /// Recursively destroys a game object and its complete child subtree.
    /// </summary>
    /// <param name="gameObject">Root object to destroy.</param>
    /// <returns><see langword="true"/> when the object belonged to this scene and was destroyed.</returns>
    public bool DestroyObject(GameObject gameObject)
    {
        ArgumentNullException.ThrowIfNull(gameObject);
        if (!Contains(gameObject))
            return false;
        Guid persistentId = gameObject.identity.persistentId;
        Exception? firstException = null;

        Transform[] children = gameObject.transform.children.ToArray();
        for (int i = 0; i < children.Length; i++)
        {
            try
            {
                DestroyObject(children[i].gameObject);
            }
            catch (Exception exception)
            {
                firstException ??= exception;
            }
        }

        m_hierarchy.Unregister(gameObject.transform);
        IReadOnlyList<SceneStoreRemovedComponent> removed = m_store.RemoveObject(gameObject);
        for (int i = 0; i < removed.Count; i++)
        {
            GameComponent component = removed[i].component;
            try
            {
                if (removed[i].wasCommitted && component is GameBehavior behavior)
                    m_systems.DestroyBehavior(behavior);
            }
            catch (Exception exception)
            {
                firstException ??= exception;
            }
            finally
            {
                if (!component.isDestroyed)
                    component.Detach();
            }
        }

        gameObject.DestroyDirect();
        if (firstException is not null)
            throw new InvalidOperationException($"A destruction callback failed for GameObject '{persistentId}'.", firstException);
        return true;
    }

    /// <summary>
    /// Gets all committed live game objects in scene storage order.
    /// </summary>
    /// <returns>A stable object snapshot.</returns>
    public IReadOnlyList<GameObject> GetObjects()
    {
        EnsureNotDestroyed();
        return m_store.GetObjects();
    }

    /// <summary>
    /// Finds the first game object with an ordinally matching name.
    /// </summary>
    /// <param name="name">Name to find.</param>
    /// <returns>The first match, or <see langword="null"/> when no object matches.</returns>
    public GameObject? FindObject(string name)
    {
        EnsureNotDestroyed();
        return m_store.GetObjects().FirstOrDefault(gameObject => string.Equals(gameObject.name, name, StringComparison.Ordinal));
    }

    /// <summary>
    /// Creates and registers a parameterless game system.
    /// </summary>
    /// <typeparam name="TSystem">Concrete system type.</typeparam>
    /// <returns>The registered system.</returns>
    public TSystem AddSystem<TSystem>() where TSystem : GameSystem, new()
    {
        EnsureNotDestroyed();
        return m_systems.Add<TSystem>();
    }

    /// <summary>
    /// Creates and registers a concrete game system by runtime type.
    /// </summary>
    public GameSystem AddSystem(Type systemType)
    {
        EnsureNotDestroyed();
        return m_systems.Add(systemType, persistentId: null, invokeReset: true);
    }

    /// <summary>
    /// Registers a game system instance.
    /// </summary>
    /// <param name="system">System to register.</param>
    public void AddSystem(GameSystem system)
    {
        EnsureNotDestroyed();
        m_systems.Add(system);
    }

    /// <summary>
    /// Removes a registered game system.
    /// </summary>
    /// <param name="system">System to remove.</param>
    /// <returns><see langword="true"/> when the system was registered.</returns>
    public bool RemoveSystem(GameSystem system)
    {
        EnsureNotDestroyed();
        ArgumentNullException.ThrowIfNull(system);
        return m_systems.Remove(system);
    }

    /// <summary>
    /// Explicitly restores a registered system to its default state.
    /// </summary>
    public void ResetSystem(GameSystem system)
    {
        EnsureNotDestroyed();
        ArgumentNullException.ThrowIfNull(system);
        m_systems.Reset(system);
    }

    /// <summary>
    /// Gets registered systems in display and serialization order.
    /// Explicit <see cref="GameSystem.order"/> values independently control lifecycle execution priority.
    /// </summary>
    public IReadOnlyList<GameSystem> GetSystems()
    {
        EnsureNotDestroyed();
        return m_systems.GetSystems();
    }

    /// <summary>
    /// Gets the display index of a registered system.
    /// </summary>
    /// <param name="system">Registered system to locate.</param>
    /// <returns>The zero-based display index.</returns>
    public int GetSystemIndex(GameSystem system)
    {
        EnsureNotDestroyed();
        ArgumentNullException.ThrowIfNull(system);
        return m_systems.GetIndex(system);
    }

    /// <summary>
    /// Moves a registered system to a requested display and serialization index.
    /// This operation does not change its explicit <see cref="GameSystem.order"/>.
    /// </summary>
    /// <param name="system">Registered system to move.</param>
    /// <param name="systemIndex">Requested zero-based display index.</param>
    public void SetSystemIndex(GameSystem system, int systemIndex)
    {
        EnsureNotDestroyed();
        ArgumentNullException.ThrowIfNull(system);
        m_systems.SetIndex(system, systemIndex);
    }

    internal GameSystem AddSystem(Type systemType, Guid? persistentId, bool invokeReset)
        => m_systems.Add(systemType, persistentId, invokeReset);

    internal bool Contains(GameObject gameObject) => !isDestroyed && m_store.Contains(gameObject);

    internal GameComponent AddComponent(
        GameObject owner,
        Type componentType,
        Guid? persistentId,
        bool invokeReset)
    {
        EnsureOwned(owner);
        ArgumentNullException.ThrowIfNull(componentType);
        if (!typeof(GameComponent).IsAssignableFrom(componentType))
            throw new ArgumentException($"Type '{componentType.FullName}' is not a scene component.", nameof(componentType));

        bool allowsMultiple = componentType.IsDefined(typeof(AllowMultipleComponentAttribute), inherit: true);
        GameComponent component = ComponentFactory.Create(componentType);
        component.Attach(owner);
        bool addedToStore = false;
        bool registeredHierarchy = false;
        try
        {
            component.RegisterIdentity(persistentId);
            m_store.AddComponent(owner, component, allowsMultiple);
            addedToStore = true;
            if (component is Transform transform)
            {
                owner.BindTransform(transform);
                m_hierarchy.Register(transform);
                registeredHierarchy = true;
                m_activation.RecomputeSubtree(owner);
            }

            if (invokeReset)
                component.DispatchReset();
            return component;
        }
        catch
        {
            if (registeredHierarchy && component is Transform transform)
                m_hierarchy.Unregister(transform);
            if (addedToStore)
                m_store.RemoveComponent(owner, component);
            if (!component.isDestroyed)
                component.Detach();
            throw;
        }
    }

    internal bool RemoveComponent(GameObject owner, GameComponent component)
    {
        EnsureOwned(owner);
        if (!ReferenceEquals(component.ownerOrNull, owner))
            return false;
        if (component is Transform)
            throw new InvalidOperationException("The mandatory Transform component cannot be removed.");
        SceneStoreRemovalKind removal = m_store.RemoveComponent(owner, component);
        if (removal == SceneStoreRemovalKind.None)
            return false;

        try
        {
            if (removal == SceneStoreRemovalKind.RemovedCommitted && component is GameBehavior behavior)
                m_systems.DestroyBehavior(behavior);
        }
        finally
        {
            if (!component.isDestroyed)
                component.Detach();
        }
        return true;
    }

    internal void ResetComponent(GameObject owner, GameComponent component)
    {
        EnsureOwned(owner);
        ArgumentNullException.ThrowIfNull(component);
        if (!ReferenceEquals(component.ownerOrNull, owner))
            throw new InvalidOperationException($"GameComponent '{component.GetType().FullName}' is not attached to GameObject '{owner.name}'.");
        component.DispatchReset();
    }

    internal IReadOnlyList<GameComponent> GetComponents(GameObject owner)
    {
        EnsureOwned(owner);
        return m_store.GetComponents(owner);
    }

    internal int GetComponentIndex(GameObject owner, GameComponent component)
    {
        EnsureOwned(owner);
        return m_store.GetComponentIndex(owner, component);
    }

    internal void SetComponentIndex(GameObject owner, GameComponent component, int componentIndex)
    {
        EnsureOwned(owner);
        m_store.SetComponentIndex(owner, component, componentIndex);
    }

    internal IReadOnlyList<TComponent> GetComponents<TComponent>(GameObject owner)
        where TComponent : GameComponent
    {
        EnsureOwned(owner);
        return m_store.GetComponents<TComponent>(owner);
    }

    internal IReadOnlyList<TComponent> GetComponents<TComponent>() where TComponent : GameComponent
        => m_store.GetComponents<TComponent>();

    internal IReadOnlyList<GameObject> Query(params Type[] componentTypes)
        => m_store.Query(componentTypes);

    internal IDisposable BeginExecutionPhase() => m_store.BeginExecutionPhase();

    internal SceneStructureSnapshot CaptureStructure() => m_store.CaptureStructure();

    internal GameObject? FindObject(Guid persistentId)
        => m_store.GetOwnedObjects().FirstOrDefault(
            gameObject => gameObject.identity.persistentId == persistentId);

    internal GameComponent? FindComponent(Guid persistentId)
        => m_store.GetOwnedObjects()
            .SelectMany(m_store.GetComponents)
            .FirstOrDefault(component => component.identity.persistentId == persistentId);

    internal void ReplaceComponentForReload(GameComponent previous, GameComponent replacement)
    {
        GameObject owner = previous.ownerOrNull
            ?? throw new InvalidOperationException("The component being replaced is detached.");
        if (!Contains(owner) || previous is Transform || replacement is Transform)
            throw new InvalidOperationException("Only attached non-Transform components can be hot reloaded.");
        bool attachedHere = replacement.ownerOrNull is null;
        if (attachedHere)
            replacement.Attach(owner);
        else if (!ReferenceEquals(replacement.ownerOrNull, owner))
            throw new InvalidOperationException("The replacement component belongs to another GameObject.");
        Guid persistentId = previous.ReleaseIdentityForReplacement();
        try
        {
            replacement.RegisterIdentity(persistentId);
            m_store.ReplaceComponent(previous, replacement);
        }
        catch
        {
            if (replacement.identity.runtimeId is not null)
                _ = replacement.ReleaseIdentityForReplacement();
            previous.RegisterIdentity(persistentId);
            if (attachedHere && !replacement.isDestroyed)
                replacement.Detach();
            throw;
        }
    }

    internal void ReplaceSystemForReload(GameSystem previous, GameSystem replacement)
        => m_systems.ReplaceForReload(previous, replacement);

    internal bool canDispatch => m_isLoaded && !m_isUnloading && !isDestroyed;

    internal void SetSourceAsset(AssetObject sourceAsset)
    {
        ArgumentNullException.ThrowIfNull(sourceAsset);
        m_sourceAsset = sourceAsset;
    }

    internal void ValidateRestoreTarget(Guid persistentId)
    {
        EnsureNotDestroyed();
        if (m_isLoaded)
            throw new InvalidOperationException($"Loaded scene '{m_name}' cannot be restored in place.");
        if (m_store.GetOwnedObjects().Count != 0 || m_systems.GetSystems().Count != 0)
            throw new InvalidOperationException($"Scene '{m_name}' must be empty before state can be restored.");
        if (identity.persistentId != persistentId)
        {
            throw new InvalidOperationException(
                $"Scene identity '{identity.persistentId}' does not match serialized identity '{persistentId}'.");
        }
    }

    internal void SetActive(GameObject gameObject, bool active)
    {
        EnsureOwned(gameObject);
        m_activation.SetActive(gameObject, active);
    }

    internal void RecomputeActiveSubtree(GameObject gameObject)
    {
        if (Contains(gameObject))
            m_activation.RecomputeSubtree(gameObject);
    }

    internal void SetParent(Transform transform, Transform? parent, bool worldPositionStays)
    {
        EnsureOwned(transform.gameObject);
        m_hierarchy.SetParent(transform, parent, worldPositionStays);
    }

    internal int GetSiblingIndex(Transform transform)
    {
        EnsureOwned(transform.gameObject);
        return m_hierarchy.GetSiblingIndex(transform);
    }

    internal void SetSiblingIndex(Transform transform, int siblingIndex)
    {
        EnsureOwned(transform.gameObject);
        m_hierarchy.SetSiblingIndex(transform, siblingIndex);
    }

    internal GameObject CreateObject(
        string name,
        Guid? persistentId,
        Guid? transformPersistentId,
        bool invokeReset)
    {
        EnsureNotDestroyed();
        var gameObject = new GameObject(this, name);
        gameObject.RegisterIdentity(persistentId);
        try
        {
            m_store.AddObject(gameObject);
            AddComponent(gameObject, typeof(Transform), transformPersistentId, invokeReset);
            return gameObject;
        }
        catch
        {
            if (Contains(gameObject))
                DestroyObject(gameObject);
            else if (!gameObject.isDestroyed)
                gameObject.DestroyDirect();
            throw;
        }
    }

    internal void Load()
    {
        EnsureNotDestroyed();
        if (m_isLoaded)
            throw new InvalidOperationException($"Scene '{m_name}' is already loaded.");
        m_isLoaded = true;
    }

    internal void Unload()
    {
        if (isDestroyed || m_isUnloading)
            return;

        m_isUnloading = true;
        m_isLoaded = false;
        Exception? firstException = null;
        GameObject[] objects = [.. m_store.GetOwnedObjects()];
        for (int i = 0; i < objects.Length; i++)
        {
            try
            {
                if (Contains(objects[i]))
                    DestroyObject(objects[i]);
            }
            catch (Exception exception)
            {
                firstException ??= exception;
            }
        }

        m_systems.Clear();
        m_hierarchy.Clear();
        m_store.Clear();
        m_sourceAsset = null;
        MarkDestroyed();
        if (firstException is not null)
            throw new InvalidOperationException($"Scene '{m_name}' encountered an error while unloading.", firstException);
    }

    internal void FixedUpdate(float fixedDeltaTime)
    {
        if (canDispatch)
            m_systems.FixedUpdate();
    }

    internal void Update(float deltaTime)
    {
        if (canDispatch)
            m_systems.Update();
    }

    internal void LateUpdate(float deltaTime)
    {
        if (canDispatch)
            m_systems.LateUpdate();
    }

    private void EnsureOwned(GameObject gameObject)
    {
        ArgumentNullException.ThrowIfNull(gameObject);
        EnsureNotDestroyed();
        if (!m_store.Contains(gameObject))
            throw new InvalidOperationException($"GameObject does not belong to scene '{m_name}'.");
    }

    private void EnsureNotDestroyed()
    {
        if (isDestroyed)
            throw new InvalidOperationException($"Scene '{m_name}' has been destroyed and cannot be reused.");
    }
}
