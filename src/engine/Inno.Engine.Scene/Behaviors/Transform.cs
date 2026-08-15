using System;
using System.Collections.Generic;

using Inno.Core.Mathematics;
using Inno.Core.Serialization;

namespace Inno.Engine.Scene.Components;

/// <summary>
/// Minimal transform data component with cached local and world state.
/// </summary>
public sealed class Transform : GameBehavior
{
    private readonly List<Transform> m_children = [];

    private Transform? m_parent;
    private Vector3 m_localPosition = Vector3.ZERO;
    private Quaternion m_localRotation = Quaternion.identity;
    private Vector3 m_localScale = Vector3.ONE;

    private Vector3 m_worldPosition = Vector3.ZERO;
    private Quaternion m_localToWorldRotation = Quaternion.identity;
    private Vector3 m_worldScale = Vector3.ONE;

    private Guid m_parentStableId = Guid.Empty;

    /// <summary>
    /// Gets or sets local position.
    /// </summary>
    [SerializableProperty]
    public Vector3 localPosition
    {
        get => m_localPosition;
        set => SetLocalPosition(value);
    }

    /// <summary>
    /// Gets or sets local rotation.
    /// </summary>
    [SerializableProperty]
    public Quaternion localRotation
    {
        get => m_localRotation;
        set => SetLocalRotation(value);
    }

    /// <summary>
    /// Gets or sets local scale.
    /// </summary>
    [SerializableProperty]
    public Vector3 localScale
    {
        get => m_localScale;
        set => SetLocalScale(value);
    }

    /// <summary>
    /// Gets the resolved world position.
    /// </summary>
    public Vector3 worldPosition
    {
        get => m_worldPosition;
        set => SetWorldPosition(value);
    }

    /// <summary>
    /// Gets the resolved world rotation.
    /// </summary>
    public Quaternion worldRotation
    {
        get => m_localToWorldRotation;
        set => SetWorldRotation(value);
    }

    /// <summary>
    /// Gets the resolved world scale.
    /// </summary>
    public Vector3 worldScale
    {
        get => m_worldScale;
        set => SetWorldScale(value);
    }

    /// <summary>
    /// Gets the parent transform.
    /// </summary>
    public Transform? parent => m_parent;

    /// <summary>
    /// Gets child transforms in explicit sibling order.
    /// </summary>
    public IReadOnlyList<Transform> children => m_children;

    /// <summary>
    /// Gets this transform's index among siblings or scene roots.
    /// </summary>
    public int siblingIndex
    {
        get
        {
            if (m_parent is not null)
            {
                return m_parent.m_children.IndexOf(this);
            }

            GameObject? owner = gameObject;
            return owner is null ? -1 : owner.scene.GetRootSiblingIndex(owner);
        }
    }

    /// <summary>
    /// Gets the parent stable id for persistence.
    /// </summary>
    [SerializableProperty(PropertyVisibility.Hide)]
    internal Guid parentStableId
    {
        get => m_parentStableId;
        set => m_parentStableId = value;
    }

    /// <summary>
    /// Sets this transform's parent while preserving world transform.
    /// </summary>
    /// <param name="parent">New parent transform, or <see langword="null"/> to unparent.</param>
    public void SetParent(Transform? parent)
    {
        if (ReferenceEquals(parent, this))
        {
            throw new InvalidOperationException("A transform cannot parent itself.");
        }

        if (parent is not null && parent.IsDescendantOf(this))
        {
            throw new InvalidOperationException("Setting this parent would create a cycle.");
        }

        Vector3 currentWorldPosition = m_worldPosition;
        Quaternion currentWorldRotation = m_localToWorldRotation;
        Vector3 currentWorldScale = m_worldScale;

        if (parent is not null && parent.gameObject is null)
        {
            throw new InvalidOperationException("Parent transform is not bound to a game object.");
        }

        GameObject? owner = gameObject;
        if (parent is not null && owner is not null &&
            !ReferenceEquals(parent.gameObject!.scene, owner.scene))
        {
            throw new InvalidOperationException("Transforms from different scenes cannot share a hierarchy.");
        }

        if (ReferenceEquals(m_parent, parent))
        {
            return;
        }

        Transform? previousParent = m_parent;
        m_parent?.RemoveChild(this);
        m_parent = parent;
        m_parentStableId = parent?.gameObject?.identity.persistentId ?? Guid.Empty;

        if (m_parent is not null)
        {
            m_parent.AddChild(this);
        }

        NotifySceneParentChanged(previousParent, m_parent);

        ApplyWorld(currentWorldPosition, currentWorldRotation, currentWorldScale);
    }

    /// <summary>
    /// Moves this transform within its current sibling collection.
    /// </summary>
    /// <param name="siblingIndex">Requested zero-based sibling index.</param>
    public void SetSiblingIndex(int siblingIndex)
    {
        if (m_parent is null)
        {
            GameObject? owner = gameObject;
            if (owner is null)
            {
                throw new InvalidOperationException("Transform is not bound to a game object.");
            }

            owner.scene.SetRootSiblingIndex(owner, siblingIndex);
            return;
        }

        int currentIndex = m_parent.m_children.IndexOf(this);
        if (currentIndex < 0)
        {
            throw new InvalidOperationException("Transform is missing from its parent child collection.");
        }

        int clampedIndex = Math.Clamp(siblingIndex, 0, m_parent.m_children.Count - 1);
        if (currentIndex == clampedIndex)
        {
            return;
        }

        m_parent.m_children.RemoveAt(currentIndex);
        m_parent.m_children.Insert(clampedIndex, this);
    }

    /// <summary>
    /// Resets transform cache and hierarchy links.
    /// </summary>
    public override void Reset()
    {
        foreach (Transform child in m_children.ToArray())
        {
            child.UnparentPreserveWorld();
        }

        DetachFromParent(preserveWorld: false);

        m_localPosition = Vector3.ZERO;
        m_localRotation = Quaternion.identity;
        m_localScale = Vector3.ONE;

        m_worldPosition = Vector3.ZERO;
        m_localToWorldRotation = Quaternion.identity;
        m_worldScale = Vector3.ONE;

        m_parentStableId = Guid.Empty;
        m_parent = null;
        m_children.Clear();
        base.Reset();
    }

    private bool IsDescendantOf(Transform ancestor)
    {
        Transform? current = m_parent;
        while (current is not null)
        {
            if (ReferenceEquals(current, ancestor))
            {
                return true;
            }

            current = current.m_parent;
        }

        return false;
    }
    
    internal override void BindGameObject(GameObject go)
    {
        base.BindGameObject(go);
        if (m_parent is not null && m_parentStableId == Guid.Empty)
        {
            m_parentStableId = m_parent.gameObject?.identity.persistentId ?? Guid.Empty;
        }
    }
    
    internal bool TryGetSelfActiveState(out ActiveState? state)
    {
        GameObject? owner = gameObject;
        if (owner is null)
        {
            state = null;
            return false;
        }

        return owner.TryGetComponent(out state);
    }

    private void SetLocalPosition(Vector3 value)
    {
        m_localPosition = value;
        RecomputeWorldFromLocal();
    }

    private void SetLocalRotation(Quaternion value)
    {
        m_localRotation = value.normalized;
        RecomputeWorldFromLocal();
    }

    private void SetLocalScale(Vector3 value)
    {
        m_localScale = value;
        RecomputeWorldFromLocal();
    }

    private void SetWorldPosition(Vector3 value)
    {
        ApplyWorld(new Vector3(value.x, value.y, value.z), m_localToWorldRotation, m_worldScale);
    }

    private void SetWorldRotation(Quaternion value)
    {
        ApplyWorld(m_worldPosition, value.normalized, m_worldScale);
    }

    private void SetWorldScale(Vector3 value)
    {
        ApplyWorld(m_worldPosition, m_localToWorldRotation, value);
    }

    private void ApplyWorld(Vector3 worldPosition, Quaternion worldRotation, Vector3 worldScale)
    {
        if (m_parent is null)
        {
            m_worldPosition = worldPosition;
            m_localToWorldRotation = worldRotation.normalized;
            m_worldScale = worldScale;

            m_localPosition = worldPosition;
            m_localRotation = worldRotation.normalized;
            m_localScale = worldScale;
        }
        else
        {
            m_parent.RecomputeWorldFromLocal(notifyChildren: false);

            Vector3 parentScale = m_parent.m_worldScale;
            Quaternion parentRotation = m_parent.m_localToWorldRotation;
            Vector3 parentPosition = m_parent.m_worldPosition;

            Quaternion inverseParentRotation = Quaternion.Inverse(parentRotation);
            Vector3 delta = worldPosition - parentPosition;
            Vector3 localDelta = Vector3.Transform(delta, inverseParentRotation);

            m_localPosition = new Vector3(
                SafeDiv(localDelta.x, parentScale.x),
                SafeDiv(localDelta.y, parentScale.y),
                SafeDiv(localDelta.z, parentScale.z));

            m_localRotation = (inverseParentRotation * worldRotation).normalized;
            m_localScale = new Vector3(
                SafeDiv(worldScale.x, parentScale.x),
                SafeDiv(worldScale.y, parentScale.y),
                SafeDiv(worldScale.z, parentScale.z));

            m_worldPosition = worldPosition;
            m_localToWorldRotation = worldRotation.normalized;
            m_worldScale = worldScale;
        }

        NotifyChildrenWorldChanged();
    }

    private void RecomputeWorldFromLocal(bool notifyChildren = true)
    {
        if (m_parent is null)
        {
            m_worldPosition = m_localPosition;
            m_localToWorldRotation = m_localRotation.normalized;
            m_worldScale = m_localScale;
            if (notifyChildren)
            {
                NotifyChildrenWorldChanged();
            }

            return;
        }

        m_parent.RecomputeWorldFromLocal(notifyChildren: false);

        Quaternion parentRotation = m_parent.m_localToWorldRotation;
        Vector3 parentScale = m_parent.m_worldScale;
        Vector3 scaled = new(
            m_localPosition.x * parentScale.x,
            m_localPosition.y * parentScale.y,
            m_localPosition.z * parentScale.z);

        m_worldPosition = m_parent.m_worldPosition + Vector3.Transform(scaled, parentRotation);
        m_localToWorldRotation = (parentRotation * m_localRotation).normalized;
        m_worldScale = new Vector3(
            m_localScale.x * parentScale.x,
            m_localScale.y * parentScale.y,
            m_localScale.z * parentScale.z);

        if (notifyChildren)
        {
            NotifyChildrenWorldChanged();
        }
    }

    private void NotifyChildrenWorldChanged()
    {
        for (int i = 0; i < m_children.Count; i++)
        {
            m_children[i].RecomputeWorldFromLocal();
        }
    }

    private void AddChild(Transform child)
    {
        if (m_children.Contains(child))
        {
            return;
        }

        m_children.Add(child);
    }

    private void DetachFromParent(bool preserveWorld)
    {
        if (m_parent is null)
        {
            return;
        }

        Vector3 detachedWorldPosition = m_worldPosition;
        Quaternion detachedWorldRotation = m_localToWorldRotation;
        Vector3 detachedWorldScale = m_worldScale;
        Transform previousParent = m_parent;
        m_parent.RemoveChild(this);
        m_parent = null;
        m_parentStableId = Guid.Empty;
        NotifySceneParentChanged(previousParent, null);

        if (preserveWorld)
        {
            ApplyWorld(detachedWorldPosition, detachedWorldRotation, detachedWorldScale);
        }
    }

    private void UnparentPreserveWorld()
    {
        if (m_parent is null)
        {
            return;
        }

        Vector3 unparentWorldPosition = m_worldPosition;
        Quaternion unparentWorldRotation = m_localToWorldRotation;
        Vector3 unparentWorldScale = m_worldScale;

        Transform previousParent = m_parent;
        m_parent.RemoveChild(this);
        m_parent = null;
        m_parentStableId = Guid.Empty;
        NotifySceneParentChanged(previousParent, null);

        m_localPosition = unparentWorldPosition;
        m_localRotation = unparentWorldRotation.normalized;
        m_localScale = unparentWorldScale;
        m_worldPosition = unparentWorldPosition;
        m_localToWorldRotation = unparentWorldRotation.normalized;
        m_worldScale = unparentWorldScale;
    }

    private void RemoveChild(Transform child)
    {
        m_children.Remove(child);
    }

    private void NotifySceneParentChanged(Transform? previousParent, Transform? currentParent)
    {
        GameObject? owner = gameObject;
        if (owner is null)
        {
            return;
        }

        owner.scene.OnTransformParentChanged(this, previousParent, currentParent);
    }

    private static float SafeDiv(float numerator, float denominator)
    {
        return MathHelper.AlmostEquals(denominator, 0f) ? 0f : numerator / denominator;
    }
}
