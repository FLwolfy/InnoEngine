using System;
using System.Collections.Generic;
using System.Linq;

using Inno.Core.Serialization;
using Inno.Scene.Components;
using Inno.Scene.Layers;

namespace Inno.Scene;

/// <summary>
/// Represents a scene-owned object and exposes its component-oriented API.
/// </summary>
[RequiresSerializationConverter]
public sealed class GameObject : EngineObject, ISerializable
{
    /// <summary>
    /// Defines the tag assigned to newly created game objects.
    /// </summary>
    public const string defaultTag = "Untagged";

    private GameScene? m_scene;
    private Transform? m_transform;
    private PrefabInstanceInfo? m_prefabInstance;
    private PrefabConnectionRecord? m_prefabConnection;
    private string m_name;
    private string m_tag = defaultTag;
    private GameLayer m_layer = GameLayer.defaultLayer;
    private bool m_activeSelf = true;
    private bool m_activeInHierarchy = true;

    internal GameObject(GameScene scene, string name)
    {
        m_scene = scene ?? throw new ArgumentNullException(nameof(scene));
        m_name = name ?? string.Empty;
    }

    /// <summary>
    /// Gets whether this object is live in its owning scene.
    /// </summary>
    public bool isRuntimeValid => !isDestroyed && m_scene?.Contains(this) == true;

    /// <summary>
    /// Gets the owning scene.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when this object is destroyed or detached.
    /// </exception>
    public GameScene scene => EnsureAlive();

    /// <summary>
    /// Gets or sets the display name stored in the owning scene.
    /// </summary>
    public string name
    {
        get
        {
            EnsureAlive();
            return m_name;
        }
        set
        {
            GameScene owner = EnsureAlive();
            string requestedName = value ?? string.Empty;
            if (string.Equals(m_name, requestedName, StringComparison.Ordinal))
                return;
            m_name = requestedName;
            owner.NotifyObjectMetadataChanged(this);
        }
    }

    /// <summary>
    /// Gets or sets the ordinal tag used to categorize and query this game object.
    /// </summary>
    /// <remarks>
    /// Tag definitions are project configuration stored separately from scene data. Scene and prefab
    /// serialization persist the assigned ordinal value even when its current project definition is absent.
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// Thrown when the assigned tag is empty or contains only white-space characters.
    /// </exception>
    public string tag
    {
        get
        {
            EnsureAlive();
            return m_tag;
        }
        set
        {
            GameScene owner = EnsureAlive();
            string requestedTag = NormalizeTag(value);
            if (string.Equals(m_tag, requestedTag, StringComparison.Ordinal))
                return;
            m_tag = requestedTag;
            owner.NotifyObjectMetadataChanged(this);
        }
    }

    /// <summary>
    /// Gets or sets the single runtime layer used to filter this game object.
    /// </summary>
    /// <remarks>
    /// GameLayer names are project configuration stored separately from scene data. Scene and prefab
    /// serialization persist only the stable numeric layer slot.
    /// </remarks>
    public GameLayer layer
    {
        get
        {
            EnsureAlive();
            return m_layer;
        }
        set
        {
            GameScene owner = EnsureAlive();
            if (m_layer == value)
                return;
            m_layer = value;
            owner.NotifyObjectMetadataChanged(this);
        }
    }

    /// <summary>
    /// Gets the mandatory transform component.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when this object is destroyed or incompletely initialized.
    /// </exception>
    public Transform transform
    {
        get
        {
            EnsureAlive();
            return m_transform ?? throw new InvalidOperationException("GameObject transform has not been initialized.");
        }
    }

    /// <summary>
    /// Gets whether this object is explicitly active.
    /// </summary>
    public bool activeSelf
    {
        get
        {
            EnsureAlive();
            return m_activeSelf;
        }
    }

    /// <summary>
    /// Gets whether this object is active after parent hierarchy state is applied.
    /// </summary>
    public bool activeInHierarchy => !isDestroyed && m_activeInHierarchy;

    /// <summary>
    /// Gets whether this object retains a prefab source connection.
    /// </summary>
    public bool isPartOfPrefabInstance => !isDestroyed && m_prefabInstance is not null;

    /// <summary>
    /// Gets the root of this object's prefab instance connection.
    /// </summary>
    public GameObject? prefabInstanceRoot => isDestroyed ? null : m_prefabInstance?.instanceRoot;

    /// <summary>
    /// Gets read-only information about this object's prefab source connection.
    /// </summary>
    public PrefabInstanceInfo? prefabInstance => isDestroyed ? null : m_prefabInstance;

    /// <summary>
    /// Changes this object's explicit active state and updates its hierarchy subtree.
    /// </summary>
    /// <param name="value">
    /// The concrete value read or transformed by this operation.
    /// </param>
    public void SetActive(bool value) => EnsureAlive().SetActive(this, value);

    /// <summary>
    /// Creates and attaches a component of the requested type.
    /// </summary>
    /// <typeparam name="TComponent">
    /// Concrete component type.
    /// </typeparam>
    /// <returns>
    /// The attached component.
    /// </returns>
    public TComponent AddComponent<TComponent>() where TComponent : GameComponent
        => (TComponent)AddComponent(typeof(TComponent));

    /// <summary>
    /// Creates and attaches a component of the requested runtime type.
    /// </summary>
    /// <param name="componentType">
    /// Concrete component type.
    /// </param>
    /// <returns>
    /// The attached component.
    /// </returns>
    public GameComponent AddComponent(Type componentType)
        => EnsureAlive().AddComponent(this, componentType, persistentId: null, invokeReset: true);

    /// <summary>
    /// Gets the first attached component assignable to the requested type.
    /// </summary>
    /// <typeparam name="TComponent">
    /// Requested component type.
    /// </typeparam>
    /// <returns>
    /// The first matching component.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no matching component exists.
    /// </exception>
    public TComponent GetComponent<TComponent>() where TComponent : GameComponent
    {
        if (TryGetComponent(out TComponent? component) && component is not null)
            return component;
        throw new InvalidOperationException($"GameObject '{m_name}' does not contain component '{typeof(TComponent).FullName}'. Add it explicitly before calling GetComponent.");
    }

    /// <summary>
    /// Gets the first attached component assignable to a runtime type.
    /// </summary>
    /// <param name="componentType">
    /// Requested component type.
    /// </param>
    /// <returns>
    /// The first matching component.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no matching component exists.
    /// </exception>
    public GameComponent GetComponent(Type componentType)
    {
        ArgumentNullException.ThrowIfNull(componentType);
        if (EnsureAlive().TryGetComponent(this, componentType, out GameComponent? component) && component is not null)
            return component;
        throw new InvalidOperationException($"GameObject '{m_name}' does not contain component '{componentType.FullName}'. Add it explicitly before calling GetComponent.");
    }

    /// <summary>
    /// Tries to get the first attached component assignable to the requested type.
    /// </summary>
    /// <typeparam name="TComponent">
    /// Requested component type.
    /// </typeparam>
    /// <param name="component">
    /// Matching component when found.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when a matching component exists.
    /// </returns>
    public bool TryGetComponent<TComponent>(out TComponent? component) where TComponent : GameComponent
    {
        if (!isRuntimeValid)
        {
            component = null;
            return false;
        }

        return EnsureAlive().TryGetComponent(this, out component);
    }

    /// <summary>
    /// Gets whether a matching component is attached.
    /// </summary>
    /// <typeparam name="TComponent">
    /// Requested component type.
    /// </typeparam>
    /// <returns>
    /// <see langword="true"/> when a matching component exists.
    /// </returns>
    public bool HasComponent<TComponent>() where TComponent : GameComponent
        => TryGetComponent<TComponent>(out _);

    /// <summary>
    /// Gets all attached components assignable to the requested type in attachment order.
    /// </summary>
    /// <typeparam name="TComponent">
    /// Requested component type.
    /// </typeparam>
    /// <returns>
    /// A stable component snapshot.
    /// </returns>
    public IReadOnlyList<TComponent> GetComponents<TComponent>() where TComponent : GameComponent
        => EnsureAlive().GetComponents<TComponent>(this);

    /// <summary>
    /// Gets all attached components in attachment order.
    /// </summary>
    /// <returns>
    /// A stable component snapshot.
    /// </returns>
    public IReadOnlyList<GameComponent> GetComponents()
        => EnsureAlive().GetComponents(this);

    /// <summary>
    /// Gets the attachment index of a component on this object.
    /// </summary>
    /// <param name="component">
    /// Attached component to locate.
    /// </param>
    /// <returns>
    /// The zero-based attachment index.
    /// </returns>
    public int GetComponentIndex(GameComponent component)
    {
        ArgumentNullException.ThrowIfNull(component);
        return EnsureAlive().GetComponentIndex(this, component);
    }

    /// <summary>
    /// Moves an attached component to a requested attachment index.
    /// The mandatory Transform always remains at index zero.
    /// </summary>
    /// <param name="component">
    /// Attached component to move.
    /// </param>
    /// <param name="componentIndex">
    /// Requested zero-based attachment index.
    /// </param>
    public void SetComponentIndex(GameComponent component, int componentIndex)
    {
        ArgumentNullException.ThrowIfNull(component);
        EnsureAlive().SetComponentIndex(this, component, componentIndex);
    }

    /// <summary>
    /// Restores an attached component to the defaults defined by its optional Reset message.
    /// </summary>
    /// <param name="component">
    /// GameComponent instance to reset.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="component"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the object is invalid or the component is not attached to it.
    /// </exception>
    public void ResetComponent(GameComponent component)
    {
        ArgumentNullException.ThrowIfNull(component);
        EnsureAlive().ResetComponent(this, component);
    }

    /// <summary>
    /// Removes and destroys a specific attached component.
    /// </summary>
    /// <param name="component">
    /// GameComponent instance to remove.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the component was attached and removed.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when attempting to remove the mandatory transform.
    /// </exception>
    public bool RemoveComponent(GameComponent component)
    {
        ArgumentNullException.ThrowIfNull(component);
        return EnsureAlive().RemoveComponent(this, component);
    }

    internal string storedName => m_name;
    internal string storedTag => m_tag;
    internal GameLayer storedLayer => m_layer;
    internal PrefabConnectionRecord? prefabConnection => m_prefabConnection;

    internal void BindTransform(Transform transform)
    {
        if (m_transform is not null)
            throw new InvalidOperationException("GameObject transform is already initialized.");
        m_transform = transform;
    }

    internal void SetNameDirect(string value)
    {
        string requestedName = value ?? string.Empty;
        if (string.Equals(m_name, requestedName, StringComparison.Ordinal))
            return;
        m_name = requestedName;
        m_scene?.NotifyObjectMetadataChanged(this);
    }

    internal void SetTagDirect(string value)
    {
        string requestedTag = NormalizeTag(value);
        if (string.Equals(m_tag, requestedTag, StringComparison.Ordinal))
            return;
        m_tag = requestedTag;
        m_scene?.NotifyObjectMetadataChanged(this);
    }

    internal void SetLayerDirect(GameLayer value)
    {
        if (m_layer == value)
            return;
        m_layer = value;
        m_scene?.NotifyObjectMetadataChanged(this);
    }
    internal void SetActiveSelfDirect(bool value) => m_activeSelf = value;
    internal void SetActiveInHierarchyDirect(bool value) => m_activeInHierarchy = value;
    internal void SetSceneDirect(GameScene scene)
        => m_scene = scene ?? throw new ArgumentNullException(nameof(scene));
    internal void SetPrefabInstanceDirect(PrefabInstanceInfo? value) => m_prefabInstance = value;
    internal void SetPrefabConnectionDirect(PrefabConnectionRecord? value) => m_prefabConnection = value;

    internal void DestroyDirect()
    {
        m_activeInHierarchy = false;
        m_prefabInstance = null;
        m_prefabConnection = null;
        m_scene = null;
        MarkDestroyed();
    }

    private GameScene EnsureAlive()
    {
        if (isDestroyed || m_scene is null || !m_scene.Contains(this))
            throw new InvalidOperationException($"GameObject '{m_name}' is destroyed or detached from its scene.");
        return m_scene;
    }

    /// <summary>
    /// Validates and normalizes a tag used by runtime storage and serialization.
    /// </summary>
    /// <param name="value">
    /// The tag value to normalize.
    /// </param>
    /// <returns>
    /// The tag without surrounding white space.
    /// </returns>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="value"/> is empty or contains only white-space characters.
    /// </exception>
    internal static string NormalizeTag(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return value.Trim();
    }
}
