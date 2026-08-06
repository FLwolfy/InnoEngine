using System;

using Inno.Core.ECS;
using Inno.Core.Mathematics;

namespace Inno.Engine.Scene.Components;

/// <summary>
/// Minimal transform data component. TransformSystem owns hierarchy and local/world propagation.
/// </summary>
public sealed class Transform : Component
{
    private Func<Transform, TransformWorldState>? m_worldResolver;

    /// <summary>
    /// Gets or sets local position.
    /// </summary>
    public Vector3 localPosition { get; set; } = Vector3.ZERO;

    /// <summary>
    /// Gets or sets local rotation.
    /// </summary>
    public Quaternion localRotation { get; set; } = Quaternion.identity;

    /// <summary>
    /// Gets or sets local scale.
    /// </summary>
    public Vector3 localScale { get; set; } = Vector3.ONE;

    /// <summary>
    /// Gets the resolved world position.
    /// </summary>
    public Vector3 worldPosition => ResolveWorld().position;

    /// <summary>
    /// Gets the resolved world rotation.
    /// </summary>
    public Quaternion worldRotation => ResolveWorld().rotation;

    /// <summary>
    /// Gets the resolved world scale.
    /// </summary>
    public Vector3 worldScale => ResolveWorld().scale;

    /// <summary>
    /// Gets the parent entity id. Set through <see cref="SetParent"/>.
    /// </summary>
    internal int? parentTransformId { get; set; }

    /// <summary>
    /// Sets this transform's parent while preserving world transform on the next transform system update.
    /// </summary>
    /// <param name="parent">New parent transform, or <see langword="null"/> to unparent.</param>
    public void SetParent(Transform? parent)
    {
        if (ReferenceEquals(parent, this))
        {
            throw new InvalidOperationException("A transform cannot parent itself.");
        }

        parentTransformId = parent?.identity.runtimeId
            ?? (parent is null ? null : throw new InvalidOperationException("Parent transform is not registered in a world."));
    }

    /// <inheritdoc />
    public override void Reset()
    {
        localPosition = Vector3.ZERO;
        localRotation = Quaternion.identity;
        localScale = Vector3.ONE;
        parentTransformId = null;
        m_worldResolver = null;
    }

    internal void SetWorldResolver(Func<Transform, TransformWorldState>? resolver)
    {
        m_worldResolver = resolver;
    }

    private TransformWorldState ResolveWorld()
    {
        return m_worldResolver?.Invoke(this)
            ?? new TransformWorldState(localPosition, localRotation.normalized, localScale);
    }
}

internal readonly record struct TransformWorldState(Vector3 position, Quaternion rotation, Vector3 scale);
