using System;
using System.Collections.Generic;
using System.Linq;

using Inno.Assets.Core;
using Inno.Core.Serialization;
using Inno.Engine.Scene.Components;
using Inno.Engine.Scene.Layers;

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
        SceneTypeCatalog.EnsureRegistered();
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
        ArgumentNullException.ThrowIfNull(name);
        return m_store.FindObject(name);
    }

    /// <summary>
    /// Finds the first game object with an ordinally matching tag in scene storage order.
    /// </summary>
    /// <param name="tag">Tag to find.</param>
    /// <returns>The first match, or <see langword="null"/> when no object matches.</returns>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="tag"/> is empty or contains only white-space characters.
    /// </exception>
    public GameObject? FindObjectWithTag(string tag)
    {
        EnsureNotDestroyed();
        return m_store.FindObjectWithTag(GameObject.NormalizeTag(tag));
    }

    /// <summary>
    /// Finds every game object with an ordinally matching tag in scene storage order.
    /// </summary>
    /// <param name="tag">Tag to find.</param>
    /// <returns>A stable snapshot containing every matching game object.</returns>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="tag"/> is empty or contains only white-space characters.
    /// </exception>
    public IReadOnlyList<GameObject> FindObjectsWithTag(string tag)
    {
        EnsureNotDestroyed();
        return m_store.FindObjectsWithTag(GameObject.NormalizeTag(tag));
    }

    /// <summary>
    /// Finds the first game object assigned to a layer in scene storage order.
    /// </summary>
    /// <param name="layer">The layer slot to find.</param>
    /// <returns>The first matching game object, or <see langword="null"/> when no object matches.</returns>
    public GameObject? FindObjectWithLayer(GameLayer layer)
    {
        EnsureNotDestroyed();
        return m_store.FindObjectWithLayer(layer);
    }

    /// <summary>
    /// Finds every game object assigned to one layer in scene storage order.
    /// </summary>
    /// <param name="layer">The layer slot to find.</param>
    /// <returns>A stable snapshot containing every matching game object.</returns>
    public IReadOnlyList<GameObject> FindObjectsWithLayer(GameLayer layer)
    {
        EnsureNotDestroyed();
        return m_store.FindObjectsWithLayer(layer);
    }

    /// <summary>
    /// Finds every game object assigned to any layer contained in a mask.
    /// </summary>
    /// <param name="layers">The set of accepted layer slots.</param>
    /// <returns>A stable snapshot in scene storage order.</returns>
    public IReadOnlyList<GameObject> FindObjectsWithLayers(GameLayerMask layers)
    {
        EnsureNotDestroyed();
        return m_store.FindObjectsWithLayers(layers);
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

    internal void NotifyObjectMetadataChanged(GameObject gameObject)
    {
        EnsureOwned(gameObject);
        m_store.NotifyObjectMetadataChanged(gameObject);
    }

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

        if (!SceneTypeCatalog.TryGetComponent(componentType, out SceneComponentTypeDescriptor? descriptor) ||
            !descriptor!.isConcrete)
        {
            throw new ArgumentException(
                $"Type '{componentType.FullName}' is not an active concrete scene component.",
                nameof(componentType));
        }

        bool allowsMultiple = descriptor.allowsMultiple;
        GameComponent component = ComponentFactory.Create(componentType);
        component.Attach(owner);
        bool addedToStore = false;
        bool registeredHierarchy = false;
        try
        {
            component.RegisterIdentity(persistentId);
            m_store.AddComponent(owner, component, descriptor, allowsMultiple);
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
        return m_store.GetComponents<TComponent>(owner, SceneTypeCatalog.GetComponent(typeof(TComponent)));
    }

    internal IReadOnlyList<TComponent> GetComponents<TComponent>() where TComponent : GameComponent
        => m_store.GetComponents<TComponent>(SceneTypeCatalog.GetComponent(typeof(TComponent)));

    internal bool TryGetComponent<TComponent>(GameObject owner, out TComponent? component)
        where TComponent : GameComponent
    {
        EnsureOwned(owner);
        return m_store.TryGetComponent(
            owner,
            SceneTypeCatalog.GetComponent(typeof(TComponent)),
            out component);
    }

    internal bool TryGetComponent(GameObject owner, Type componentType, out GameComponent? component)
    {
        EnsureOwned(owner);
        ArgumentNullException.ThrowIfNull(componentType);
        if (!SceneTypeCatalog.TryGetComponent(componentType, out SceneComponentTypeDescriptor? descriptor))
        {
            component = null;
            return false;
        }
        return m_store.TryGetComponent(owner, descriptor!, out component);
    }

    internal IReadOnlyList<GameObject> Query<T1>() where T1 : GameComponent
        => m_store.Query(SceneTypeCatalog.GetComponent(typeof(T1)));

    internal IReadOnlyList<GameObject> Query<T1, T2>()
        where T1 : GameComponent
        where T2 : GameComponent
        => m_store.Query(
            SceneTypeCatalog.GetComponent(typeof(T1)),
            SceneTypeCatalog.GetComponent(typeof(T2)));

    internal IReadOnlyList<GameObject> Query<T1, T2, T3>()
        where T1 : GameComponent
        where T2 : GameComponent
        where T3 : GameComponent
        => m_store.Query(
            SceneTypeCatalog.GetComponent(typeof(T1)),
            SceneTypeCatalog.GetComponent(typeof(T2)),
            SceneTypeCatalog.GetComponent(typeof(T3)));

    internal IDisposable BeginExecutionPhase() => m_store.BeginExecutionPhase();

    internal SceneStructureSnapshot CaptureStructure() => m_store.CaptureStructure();

    internal GameObject? FindObject(Guid persistentId)
        => m_store.FindObject(persistentId);

    internal GameComponent? FindComponent(Guid persistentId)
        => m_store.FindComponent(persistentId);

    internal void ReplaceComponentForReload(
        GameComponent previous,
        GameComponent replacement,
        int replacementRuntimeTypeId)
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
            m_store.ReplaceComponent(previous, replacement, replacementRuntimeTypeId);
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

    internal void ReplaceSystemForReload(
        GameSystem previous,
        GameSystem replacement,
        int replacementRuntimeTypeId)
        => m_systems.ReplaceForReload(previous, replacement, replacementRuntimeTypeId);

    internal bool canDispatch => m_isLoaded && !m_isUnloading && !isDestroyed;

    internal void SetSourceAsset(AssetObject sourceAsset)
    {
        ArgumentNullException.ThrowIfNull(sourceAsset);
        m_sourceAsset = sourceAsset;
    }

    internal void PrepareRestoreTarget(Guid persistentId)
    {
        EnsureNotDestroyed();
        if (identity.persistentId != persistentId)
        {
            throw new InvalidOperationException(
                $"Scene identity '{identity.persistentId}' does not match serialized identity '{persistentId}'.");
        }

        foreach (GameSystem system in GetSystems().ToArray())
            RemoveSystem(system);
        GameObject[] objects = GetObjects().ToArray();
        for (int i = 0; i < objects.Length; i++)
        {
            if (objects[i].isRuntimeValid)
                DestroyObject(objects[i]);
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

    internal void TransferObjectTo(GameObject gameObject, GameScene destination)
    {
        EnsureOwned(gameObject);
        ArgumentNullException.ThrowIfNull(destination);
        destination.EnsureNotDestroyed();
        if (ReferenceEquals(this, destination))
            return;
        if (m_store.isExecuting || m_store.hasPendingChanges ||
            destination.m_store.isExecuting || destination.m_store.hasPendingChanges)
        {
            throw new InvalidOperationException(
                "A GameObject cannot move between scenes during a scene execution phase or while structural changes are pending.");
        }

        Transform rootTransform = gameObject.transform;
        Transform? previousParent = rootTransform.parent;
        int previousSiblingIndex = rootTransform.siblingIndex;
        GameObject[] subtree = EnumerateSubtree(rootTransform).ToArray();
        var components = new Dictionary<GameObject, GameComponent[]>(ReferenceEqualityComparer.Instance);
        for (int i = 0; i < subtree.Length; i++)
            components.Add(subtree[i], m_store.GetComponents(subtree[i]).ToArray());

        if (previousParent is not null)
            m_hierarchy.SetParent(rootTransform, parent: null, worldPositionStays: true);
        m_hierarchy.Unregister(rootTransform);

        int transferredCount = 0;
        bool registeredDestinationHierarchy = false;
        try
        {
            for (int i = 0; i < subtree.Length; i++)
            {
                GameObject current = subtree[i];
                _ = m_store.RemoveObject(current);
                destination.m_store.AddObject(current);
                transferredCount++;
                GameComponent[] attached = components[current];
                for (int componentIndex = 0; componentIndex < attached.Length; componentIndex++)
                {
                    GameComponent component = attached[componentIndex];
                    destination.m_store.AddComponent(
                        current,
                        component,
                        SceneTypeCatalog.GetComponent(component.GetType()),
                        allowsMultiple: true);
                }
                current.SetSceneDirect(destination);
            }

            destination.m_hierarchy.Register(rootTransform);
            registeredDestinationHierarchy = true;
            destination.m_activation.RecomputeSubtree(gameObject);
        }
        catch
        {
            if (registeredDestinationHierarchy)
                destination.m_hierarchy.Unregister(rootTransform);
            for (int i = transferredCount - 1; i >= 0; i--)
            {
                GameObject current = subtree[i];
                _ = destination.m_store.RemoveObject(current);
                m_store.AddObject(current);
                GameComponent[] attached = components[current];
                for (int componentIndex = 0; componentIndex < attached.Length; componentIndex++)
                {
                    GameComponent component = attached[componentIndex];
                    m_store.AddComponent(
                        current,
                        component,
                        SceneTypeCatalog.GetComponent(component.GetType()),
                        allowsMultiple: true);
                }
                current.SetSceneDirect(this);
            }

            m_hierarchy.Register(rootTransform);
            if (previousParent is not null)
                m_hierarchy.SetParent(rootTransform, previousParent, worldPositionStays: true);
            m_hierarchy.SetSiblingIndex(rootTransform, previousSiblingIndex);
            m_activation.RecomputeSubtree(gameObject);
            throw;
        }
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

    private static IEnumerable<GameObject> EnumerateSubtree(Transform root)
    {
        yield return root.gameObject;
        IReadOnlyList<Transform> children = root.children;
        for (int i = 0; i < children.Count; i++)
        {
            foreach (GameObject descendant in EnumerateSubtree(children[i]))
                yield return descendant;
        }
    }
}
