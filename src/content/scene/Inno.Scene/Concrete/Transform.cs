using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

using Inno.Core.Mathematics;
using Inno.Extensibility.Types;
using Inno.Core.Serialization;

namespace Inno.Scene.Components;

/// <summary>
/// Stores local transform data and exposes the scene hierarchy relationship.
/// </summary>
[StableTypeId("39a944b8-f021-4ac9-9f66-506f15a96848")]
public sealed class Transform : GameComponent
{
    private readonly List<Transform> m_children = [];
    private readonly ReadOnlyCollection<Transform> m_readOnlyChildren;
    private Transform? m_parent;
    private Vector3 m_localPosition = Vector3.ZERO;
    private Quaternion m_localRotation = Quaternion.identity;
    private Vector3 m_localScale = Vector3.ONE;
    private Vector3 m_worldPosition = Vector3.ZERO;
    private Quaternion m_worldRotation = Quaternion.identity;
    private Vector3 m_worldScale = Vector3.ONE;
    private Matrix m_localToWorldMatrix = Matrix.identity;

    /// <summary>
    /// Creates a transform with identity local values.
    /// </summary>
    public Transform()
    {
        m_readOnlyChildren = m_children.AsReadOnly();
    }

    /// <summary>
    /// Gets or sets the local position relative to the parent.
    /// </summary>
    [SerializableProperty]
    public Vector3 localPosition
    {
        get => m_localPosition;
        set
        {
            m_localPosition = value;
            RecomputeWorldFromLocal();
        }
    }

    /// <summary>
    /// Gets or sets the local rotation relative to the parent.
    /// </summary>
    [SerializableProperty]
    public Quaternion localRotation
    {
        get => m_localRotation;
        set
        {
            m_localRotation = value.normalized;
            RecomputeWorldFromLocal();
        }
    }

    /// <summary>
    /// Gets or sets the local scale relative to the parent.
    /// </summary>
    [SerializableProperty]
    public Vector3 localScale
    {
        get => m_localScale;
        set
        {
            m_localScale = value;
            RecomputeWorldFromLocal();
        }
    }

    /// <summary>
    /// Gets or sets the world-space position.
    /// </summary>
    public Vector3 worldPosition
    {
        get => m_worldPosition;
        set => ApplyWorld(value, m_worldRotation, m_worldScale);
    }

    /// <summary>
    /// Gets or sets the world-space rotation.
    /// </summary>
    public Quaternion worldRotation
    {
        get => m_worldRotation;
        set => ApplyWorld(m_worldPosition, value.normalized, m_worldScale);
    }

    /// <summary>
    /// Gets or sets the world-space scale.
    /// </summary>
    public Vector3 worldScale
    {
        get => m_worldScale;
        set => ApplyWorld(m_worldPosition, m_worldRotation, value);
    }

    /// <summary>
    /// Gets the exact local-to-world matrix, including the complete parent hierarchy.
    /// </summary>
    public Matrix localToWorldMatrix => m_localToWorldMatrix;

    /// <summary>
    /// Gets the inverse of the exact local-to-world matrix.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when a zero-scale hierarchy is not invertible.
    /// </exception>
    public Matrix worldToLocalMatrix
    {
        get
        {
            EnsureInvertible(m_localToWorldMatrix);
            return Matrix.Invert(m_localToWorldMatrix);
        }
    }

    /// <summary>
    /// Gets the parent transform, or <see langword="null"/> for a scene-level object.
    /// </summary>
    public Transform? parent => m_parent;

    /// <summary>
    /// Gets child transforms in sibling order.
    /// </summary>
    public IReadOnlyList<Transform> children => m_readOnlyChildren;

    /// <summary>
    /// Gets or sets this transform's index among its siblings.
    /// </summary>
    public int siblingIndex
    {
        get => gameObject.scene.GetSiblingIndex(this);
        set => SetSiblingIndex(value);
    }

    /// <summary>
    /// Sets the parent while preserving this transform's world-space values.
    /// </summary>
    /// <param name="parent">
    /// New parent, or <see langword="null"/> to move to scene level.
    /// </param>
    /// <exception cref="InvalidOperationException">
    /// Thrown for hierarchy cycles, cross-scene parents, or a non-invertible parent hierarchy.
    /// </exception>
    public void SetParent(Transform? parent)
        => gameObject.scene.SetParent(this, parent, worldPositionStays: true);

    /// <summary>
    /// Moves this transform within its current sibling collection.
    /// </summary>
    /// <param name="siblingIndex">
    /// Requested zero-based sibling index.
    /// </param>
    public void SetSiblingIndex(int siblingIndex)
        => gameObject.scene.SetSiblingIndex(this, siblingIndex);

    /// <summary>
    /// Atomically applies world-space translation, rotation, and scale.
    /// </summary>
    /// <param name="position">
    /// Requested world-space translation.
    /// </param>
    /// <param name="rotation">
    /// Requested world-space rotation.
    /// </param>
    /// <param name="scale">
    /// Requested world-space scale.
    /// </param>
    public void SetWorldTransform(Vector3 position, Quaternion rotation, Vector3 scale)
        => ApplyWorld(position, rotation.normalized, scale);

    /// <summary>
    /// Transforms a local-space point through the complete parent hierarchy.
    /// </summary>
    /// <param name="point">
    /// Point expressed in this transform's local space.
    /// </param>
    /// <returns>
    /// The corresponding world-space point.
    /// </returns>
    public Vector3 TransformPoint(Vector3 point)
        => Vector3.Transform(point, m_localToWorldMatrix);

    /// <summary>
    /// Transforms a world-space point into this transform's local space.
    /// </summary>
    /// <param name="point">
    /// Point expressed in world space.
    /// </param>
    /// <returns>
    /// The corresponding local-space point.
    /// </returns>
    public Vector3 InverseTransformPoint(Vector3 point)
        => Vector3.Transform(point, worldToLocalMatrix);

    /// <summary>
    /// Restores this instance to its initial reusable state.
    /// </summary>
    protected override void Reset()
    {
        m_localPosition = Vector3.ZERO;
        m_localRotation = Quaternion.identity;
        m_localScale = Vector3.ONE;
        RecomputeWorldFromLocal();
    }

    internal bool IsDescendantOf(Transform ancestor)
    {
        for (Transform? current = m_parent; current is not null; current = current.m_parent)
        {
            if (ReferenceEquals(current, ancestor))
                return true;
        }
        return false;
    }

    internal void SetParentDirect(Transform? parent) => m_parent = parent;

    internal void AddChildDirect(Transform child)
    {
        if (!m_children.Contains(child))
            m_children.Add(child);
    }

    internal void RemoveChildDirect(Transform child) => m_children.Remove(child);

    internal int IndexOfChild(Transform child) => m_children.IndexOf(child);

    internal void MoveChild(int currentIndex, int targetIndex)
    {
        Transform child = m_children[currentIndex];
        m_children.RemoveAt(currentIndex);
        m_children.Insert(targetIndex, child);
    }

    internal void ApplyWorld(Vector3 position, Quaternion rotation, Vector3 scale)
    {
        if (m_parent is null)
        {
            m_localPosition = position;
            m_localRotation = rotation.normalized;
            m_localScale = scale;
        }
        else
        {
            m_parent.RecomputeWorldFromLocal(notifyChildren: false);
            Matrix parentWorldToLocal = m_parent.worldToLocalMatrix;
            Vector3 parentScale = m_parent.m_worldScale;
            Quaternion inverseParentRotation = Quaternion.Inverse(m_parent.m_worldRotation);
            m_localPosition = Vector3.Transform(position, parentWorldToLocal);
            m_localRotation = (inverseParentRotation * rotation).normalized;
            m_localScale = new Vector3(
                SafeDivide(scale.x, parentScale.x),
                SafeDivide(scale.y, parentScale.y),
                SafeDivide(scale.z, parentScale.z));
        }

        RecomputeWorldFromLocal();
    }

    internal void RecomputeWorldFromLocal(bool notifyChildren = true)
    {
        if (m_parent is null)
        {
            m_worldRotation = m_localRotation.normalized;
            m_worldScale = m_localScale;
        }
        else
        {
            m_parent.RecomputeWorldFromLocal(notifyChildren: false);
            m_worldRotation = (m_parent.m_worldRotation * m_localRotation).normalized;
            m_worldScale = new Vector3(
                m_localScale.x * m_parent.m_worldScale.x,
                m_localScale.y * m_parent.m_worldScale.y,
                m_localScale.z * m_parent.m_worldScale.z);
        }

        RecomputeMatrixFromLocal();
        m_worldPosition = new Vector3(
            m_localToWorldMatrix.m14,
            m_localToWorldMatrix.m24,
            m_localToWorldMatrix.m34);

        if (notifyChildren)
            NotifyChildrenWorldChanged();
    }

    private void RecomputeMatrixFromLocal()
    {
        Matrix localMatrix = Matrix.CreateTranslation(m_localPosition)
            * Matrix.CreateFromQuaternion(m_localRotation)
            * Matrix.CreateScale(m_localScale);
        m_localToWorldMatrix = m_parent is null
            ? localMatrix
            : m_parent.m_localToWorldMatrix * localMatrix;
    }

    private void NotifyChildrenWorldChanged()
    {
        for (int i = 0; i < m_children.Count; i++)
            m_children[i].RecomputeWorldFromLocal();
    }

    private static float SafeDivide(float numerator, float denominator)
        => MathHelper.AlmostEquals(denominator, 0f) ? 0f : numerator / denominator;

    private static void EnsureInvertible(Matrix matrix)
    {
        if (MathF.Abs(Matrix.Determinant(matrix)) < MathHelper.C_TOLERANCE)
        {
            throw new InvalidOperationException(
                "A transform hierarchy with zero scale cannot be converted from world space.");
        }
    }
}
