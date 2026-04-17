using System.Collections.Generic;
using Inno.Core.ECS;
using Inno.Core.Mathematics;

namespace Inno.Engine.Scene.Components;

public enum TransformParentOptions
{
    KeepLocal = 0,
    KeepWorld = 1,
    SnapToParent = 2
}

/// <summary>
/// Transform component manages local/world TRS and parent-child hierarchy.
/// Hierarchy transactions are resolved by <c>TransformSystem</c>.
/// </summary>
public class Transform : Component
{
    public delegate void TransformChangedHandler();
    public event TransformChangedHandler? OnTransformChanged;

    private Vector3 m_localPosition = Vector3.ZERO;
    private Quaternion m_localRotation = Quaternion.identity;
    private Vector3 m_localScale = Vector3.ONE;

    private Vector3 m_worldPosition = Vector3.ZERO;
    private Quaternion m_worldRotation = Quaternion.identity;
    private Vector3 m_worldScale = Vector3.ONE;

    private readonly List<Transform> m_children = [];
    private Transform? m_parent;
    private bool m_isDirty = true;

    private bool m_hasPendingParentTransaction;
    private Transform? m_pendingParent;
    private TransformParentOptions m_pendingParentOptions = TransformParentOptions.KeepWorld;

    private bool m_hasPendingWorldTransaction;
    private Vector3 m_pendingWorldPosition = Vector3.ZERO;
    private Quaternion m_pendingWorldRotation = Quaternion.identity;
    private Vector3 m_pendingWorldScale = Vector3.ONE;

    public Vector3 localPosition
    {
        get => m_localPosition;
        set
        {
            m_localPosition = value;
            MarkDirty();
        }
    }

    public Vector3 localScale
    {
        get => m_localScale;
        set
        {
            m_localScale = value;
            MarkDirty();
        }
    }

    public Quaternion localRotation
    {
        get => m_localRotation;
        set
        {
            m_localRotation = value.normalized;
            MarkDirty();
        }
    }

    public Vector3 worldPosition
    {
        get => m_worldPosition;
        set
        {
            BeginWorldTransaction();
            m_pendingWorldPosition = value;
        }
    }

    public Quaternion worldRotation
    {
        get => m_worldRotation;
        set
        {
            BeginWorldTransaction();
            m_pendingWorldRotation = value.normalized;
        }
    }

    public Vector3 worldScale
    {
        get => m_worldScale;
        set
        {
            BeginWorldTransaction();
            m_pendingWorldScale = value;
        }
    }

    public Transform? parent => m_parent;
    public IReadOnlyList<Transform> children => m_children.AsReadOnly();

    public void SetParent(Transform? newParent, bool worldTransformStays = true)
    {
        SetParent(
            newParent,
            worldTransformStays ? TransformParentOptions.KeepWorld : TransformParentOptions.KeepLocal);
    }

    public void SetParent(Transform? newParent, TransformParentOptions options)
    {
        if (ReferenceEquals(newParent, this))
        {
            return;
        }

        m_pendingParent = newParent;
        m_pendingParentOptions = options;
        m_hasPendingParentTransaction = true;
    }

    public void OnDetach()
    {
        SetParent(null, worldTransformStays: true);
        for (int i = 0; i < m_children.Count; i++)
        {
            m_children[i].SetParent(null, worldTransformStays: true);
        }
    }

    public override void Reset()
    {
        m_localPosition = Vector3.ZERO;
        m_localRotation = Quaternion.identity;
        m_localScale = Vector3.ONE;
        m_worldPosition = Vector3.ZERO;
        m_worldRotation = Quaternion.identity;
        m_worldScale = Vector3.ONE;
        m_parent = null;
        m_children.Clear();
        m_isDirty = true;
        m_hasPendingParentTransaction = false;
        m_pendingParent = null;
        m_pendingParentOptions = TransformParentOptions.KeepWorld;
        m_hasPendingWorldTransaction = false;
        m_pendingWorldPosition = Vector3.ZERO;
        m_pendingWorldRotation = Quaternion.identity;
        m_pendingWorldScale = Vector3.ONE;
        enabled = true;
    }

    internal bool isDirty => m_isDirty;

    internal bool TryConsumeParentTransaction(
        out Transform? newParent,
        out TransformParentOptions options)
    {
        if (!m_hasPendingParentTransaction)
        {
            newParent = null;
            options = TransformParentOptions.KeepWorld;
            return false;
        }

        newParent = m_pendingParent;
        options = m_pendingParentOptions;

        m_hasPendingParentTransaction = false;
        m_pendingParent = null;
        m_pendingParentOptions = TransformParentOptions.KeepWorld;
        return true;
    }

    internal bool TryConsumeWorldTransaction(out Vector3 worldPosition, out Quaternion worldRotation, out Vector3 worldScale)
    {
        if (!m_hasPendingWorldTransaction)
        {
            worldPosition = Vector3.ZERO;
            worldRotation = Quaternion.identity;
            worldScale = Vector3.ONE;
            return false;
        }

        worldPosition = m_pendingWorldPosition;
        worldRotation = m_pendingWorldRotation;
        worldScale = m_pendingWorldScale;
        m_hasPendingWorldTransaction = false;
        return true;
    }

    internal void ApplyParentFromSystem(Transform? newParent)
    {
        if (ReferenceEquals(m_parent, newParent))
        {
            return;
        }

        m_parent?.m_children.Remove(this);
        m_parent = newParent;
        m_parent?.m_children.Add(this);
        MarkDirty();
    }

    internal void ApplyLocalFromWorldFromSystem(Vector3 worldPosition, Quaternion worldRotation, Vector3 worldScale)
    {
        if (m_parent is null)
        {
            m_localPosition = worldPosition;
            m_localRotation = worldRotation.normalized;
            m_localScale = worldScale;
            MarkDirty();
            return;
        }

        Quaternion invParentRot = Quaternion.Inverse(m_parent.worldRotation);
        Vector3 parentScale = m_parent.worldScale;
        Vector3 delta = worldPosition - m_parent.worldPosition;
        Vector3 scaled = new(
            SafeDiv(delta.x, parentScale.x),
            SafeDiv(delta.y, parentScale.y),
            SafeDiv(delta.z, parentScale.z));

        m_localPosition = Vector3.Transform(scaled, invParentRot);
        m_localRotation = (invParentRot * worldRotation).normalized;
        m_localScale = new(
            SafeDiv(worldScale.x, parentScale.x),
            SafeDiv(worldScale.y, parentScale.y),
            SafeDiv(worldScale.z, parentScale.z));
        MarkDirty();
    }

    internal void SnapLocalToIdentityFromSystem()
    {
        m_localPosition = Vector3.ZERO;
        m_localRotation = Quaternion.identity;
        m_localScale = Vector3.ONE;
        MarkDirty();
    }

    internal void ApplyWorldHierarchyFromSystem(bool parentDirty)
    {
        bool dirty = parentDirty || m_isDirty;
        if (dirty)
        {
            if (m_parent is null)
            {
                m_worldPosition = m_localPosition;
                m_worldRotation = m_localRotation;
                m_worldScale = m_localScale;
            }
            else
            {
                Vector3 parentScale = m_parent.worldScale;
                Quaternion parentRotation = m_parent.worldRotation;
                Vector3 parentPosition = m_parent.worldPosition;

                m_worldScale = new(
                    m_localScale.x * parentScale.x,
                    m_localScale.y * parentScale.y,
                    m_localScale.z * parentScale.z);

                m_worldRotation = parentRotation * m_localRotation;

                Vector3 scaled = new(
                    m_localPosition.x * parentScale.x,
                    m_localPosition.y * parentScale.y,
                    m_localPosition.z * parentScale.z);

                Vector3 rotated = Vector3.Transform(scaled, parentRotation);
                m_worldPosition = parentPosition + rotated;
                m_worldRotation = m_worldRotation.normalized;
            }

            m_isDirty = false;
            OnTransformChanged?.Invoke();
        }

        for (int i = 0; i < m_children.Count; i++)
        {
            m_children[i].ApplyWorldHierarchyFromSystem(dirty);
        }
    }

    private void BeginWorldTransaction()
    {
        if (!m_hasPendingWorldTransaction)
        {
            m_pendingWorldPosition = m_worldPosition;
            m_pendingWorldRotation = m_worldRotation;
            m_pendingWorldScale = m_worldScale;
        }

        m_hasPendingWorldTransaction = true;
        MarkDirty();
    }

    private void MarkDirty()
    {
        if (m_isDirty)
        {
            return;
        }

        m_isDirty = true;
        for (int i = 0; i < m_children.Count; i++)
        {
            m_children[i].MarkDirty();
        }
    }

    private static float SafeDiv(float value, float divisor)
    {
        return MathHelper.AlmostEquals(divisor, 0f) ? 0f : value / divisor;
    }
}
