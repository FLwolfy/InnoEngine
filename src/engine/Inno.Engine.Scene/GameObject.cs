using System;
using System.Collections.Generic;

using Inno.Core.ECS;
using Inno.Core.Identity;
using Inno.Engine.Scene.Components;

namespace Inno.Engine.Scene;

/// <summary>
/// Scene object entity with user-facing component and hierarchy APIs.
/// </summary>
public sealed class GameObject : Entity, IEquatable<GameObject>, IIdentityObject
{
    private GameScene? m_scene;

    /// <summary>
    /// Gets whether this object is still alive in its scene world.
    /// </summary>
    public bool isRuntimeValid => identity.runtimeId is int runtimeId
        && m_scene is not null
        && m_scene.ContainsEntityId(runtimeId);

    /// <summary>
    /// Gets the owning scene.
    /// </summary>
    public GameScene scene => m_scene ?? throw new InvalidOperationException("GameObject is not bound to a scene.");

    /// <summary>
    /// Gets or sets the display name stored in the internal Name component.
    /// </summary>
    public string name
    {
        get => GetComponent<Name>().value;
        set => GetComponent<Name>().value = value ?? string.Empty;
    }

    /// <summary>
    /// Gets or sets whether this object is explicitly active.
    /// </summary>
    public bool active
    {
        get => GetComponent<ActiveState>().selfActive;
        set => GetComponent<ActiveState>().selfActive = value;
    }

    /// <summary>
    /// Gets whether this object is active after hierarchy rules are applied.
    /// </summary>
    public bool activeInHierarchy
    {
        get
        {
            if (!isRuntimeValid)
            {
                return false;
            }

            if (!TryGetComponent<ActiveState>(out ActiveState? state))
            {
                return false;
            }

            return IsActiveInHierarchy(state);
        }
    }

    /// <summary>
    /// Adds a component to this object and returns it.
    /// </summary>
    /// <typeparam name="TComponent">Component type.</typeparam>
    /// <returns>The added component.</returns>
    public TComponent AddComponent<TComponent>()
        where TComponent : Component, new()
    {
        GameScene owner = EnsureAlive();
        owner.world.AddComponent<TComponent>(this);
        owner.world.FlushPending();

        TComponent component = GetComponent<TComponent>();
        BindComponent(component);
        return component;
    }

    /// <summary>
    /// Removes a component from this object.
    /// </summary>
    /// <typeparam name="TComponent">Component type.</typeparam>
    /// <returns><see langword="true"/> when an existing or pending component was found.</returns>
    public bool RemoveComponent<TComponent>()
        where TComponent : Component
    {
        GameScene owner = EnsureAlive();
        bool removed = owner.world.RemoveComponent<TComponent>(this);
        owner.world.FlushPending();
        return removed;
    }

    /// <summary>
    /// Gets an attached component.
    /// </summary>
    /// <typeparam name="TComponent">Component type.</typeparam>
    /// <returns>The attached component.</returns>
    public TComponent GetComponent<TComponent>()
        where TComponent : Component
    {
        GameScene owner = EnsureAlive();
        int ownerId = GetRuntimeId();
        IReadOnlyList<TComponent> components = owner.world.ViewComponents<TComponent>(ownerId);
        if (components.Count != 0)
        {
            TComponent component = components[0];
            BindComponent(component);
            return component;
        }

        throw new InvalidOperationException(
            $"Entity '{ownerId}' does not have component '{typeof(TComponent).FullName}'.");
    }

    /// <summary>
    /// Tries to get an attached component.
    /// </summary>
    /// <typeparam name="TComponent">Component type.</typeparam>
    /// <param name="component">Resolved component when found.</param>
    /// <returns><see langword="true"/> when found.</returns>
    public bool TryGetComponent<TComponent>(out TComponent? component)
        where TComponent : Component
    {
        GameScene owner = EnsureAlive();
        IReadOnlyList<TComponent> components = owner.world.ViewComponents<TComponent>(GetRuntimeId());
        if (components.Count != 0)
        {
            component = components[0];
            BindComponent(component);
            return true;
        }

        component = null;
        return false;
    }

    /// <summary>
    /// Returns whether this object has the requested component.
    /// </summary>
    /// <typeparam name="TComponent">Component type.</typeparam>
    /// <returns><see langword="true"/> when found.</returns>
    public bool HasComponent<TComponent>()
        where TComponent : Component
        => TryGetComponent<TComponent>(out _);

    /// <inheritdoc />
    public bool Equals(GameObject? other)
        => ReferenceEquals(this, other);

    /// <inheritdoc />
    public override bool Equals(object? obj)
        => ReferenceEquals(this, obj);

    /// <inheritdoc />
    public override int GetHashCode()
        => HashCode.Combine(m_scene, identity.persistentId);

    /// <summary>
    /// Compares two game objects by reference identity.
    /// </summary>
    public static bool operator ==(GameObject? left, GameObject? right) => ReferenceEquals(left, right);

    /// <summary>
    /// Compares two game objects by reference identity.
    /// </summary>
    public static bool operator !=(GameObject? left, GameObject? right) => !ReferenceEquals(left, right);

    internal void BindScene(GameScene scene)
    {
        m_scene = scene ?? throw new ArgumentNullException(nameof(scene));
    }

    private GameScene EnsureAlive()
    {
        GameScene owner = scene;
        int runtimeId = GetRuntimeId();
        if (!owner.ContainsEntityId(runtimeId))
        {
            throw new InvalidOperationException($"GameObject entity '{runtimeId}' is no longer alive.");
        }

        return owner;
    }

    private int GetRuntimeId()
    {
        return identity.runtimeId
            ?? throw new InvalidOperationException("GameObject is not registered in a world.");
    }

    private void BindComponent(Component component)
    {
        if (component is GameBehavior behavior)
        {
            behavior.BindGameObject(this);
        }

        if (component is Transform transform)
        {
            transform.BindGameObject(this);
        }
    }

    private bool IsActiveInHierarchy(ActiveState state)
    {
        if (!state.selfActive)
        {
            return false;
        }

        if (!TryGetComponent<Transform>(out Transform? transform))
        {
            return true;
        }

        return IsActiveInHierarchy(transform);
    }

    private bool IsActiveInHierarchy(Transform transform)
    {
        Transform? current = transform;
        while (current is not null)
        {
            if (!current.TryGetSelfActiveState(out ActiveState? selfState))
            {
                return true;
            }

            if (!selfState.selfActive)
            {
                return false;
            }

            current = current.parent;
            if (current is null || current.gameObject is null)
            {
                return true;
            }
        }

        return true;
    }

    internal void BindDefaultComponents()
    {
        GameScene owner = scene;
        int ownerId = GetRuntimeId();
        foreach (Component component in owner.world.ViewComponents<Component>(ownerId))
        {
            BindComponent(component);
        }
    }
}
