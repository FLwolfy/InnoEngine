using System;

using Inno.Core.Serialization;
using Inno.Engine.Scene.Components;

namespace Inno.Engine.Scene;

/// <summary>
/// Base type for data and behavior objects attached to a <see cref="GameObject"/>.
/// </summary>
public abstract class GameComponent : EngineObject, ISerializable
{
    private GameObject? m_gameObject;

    /// <summary>
    /// Gets the owning game object.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when the component is detached or destroyed.</exception>
    public GameObject gameObject
        => m_gameObject is not null && !isDestroyed
            ? m_gameObject
            : throw new InvalidOperationException($"GameComponent '{GetType().FullName}' is detached or destroyed.");

    /// <summary>
    /// Gets the owning game object's transform.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when the component is detached or destroyed.</exception>
    public Transform transform => gameObject.transform;

    internal GameObject? ownerOrNull => m_gameObject;

    internal void Attach(GameObject owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        if (m_gameObject is not null)
            throw new InvalidOperationException($"GameComponent '{GetType().FullName}' is already attached.");
        m_gameObject = owner;
    }

    internal void Detach()
    {
        m_gameObject = null;
        MarkDestroyed();
    }
}
