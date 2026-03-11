using System;
using System.Collections.Generic;
using Inno.Core.Math;
using Inno.Core.Serialization;

namespace Inno.Core.ECS;

/// <summary>
/// Transform component manages position, rotation, scale and parent-child hierarchy.
/// Supports 3D transforms (even if your engine is 2D only, this matches Unity design).
/// </summary>
public class Transform : GameComponent
{
    public override ComponentTag orderTag => ComponentTag.Transform;
    
    // Dirty listener
    public delegate void TransformChangedHandler();
    public event TransformChangedHandler? OnTransformChanged;
    
    // Local transform relative to parent
    private Vector3 m_localPosition = Vector3.ZERO;
    private Quaternion m_localRotation = Quaternion.identity;
    private Vector3 m_localScale = Vector3.ONE;

    // Children transforms
    private readonly List<Transform> m_children = [];

    // Cached world transform, updated lazily
    private Vector3 m_worldPosition = Vector3.ZERO;
    private Quaternion m_worldRotation = Quaternion.identity;
    private Vector3 m_worldScale = Vector3.ONE;

    // Dirty flag to mark transform changes needing update
    private bool m_isDirty = true;
    
    #region Local Serializable Properties

    /// <summary>
    /// Local position relative to parent transform.
    /// </summary>
    [SerializableProperty]
    public Vector3 localPosition
    {
        get => m_localPosition;
        set { m_localPosition = value; MarkDirty(); }
    }
    
    /// <summary>
    /// Local scale relative to parent transform.
    /// </summary>
    [SerializableProperty]
    public Vector3 localScale
    {
        get => m_localScale;
        set { m_localScale = value; MarkDirty(); }
    }

    /// <summary>
    /// Local rotation relative to parent transform.
    /// </summary>
    [SerializableProperty]
    public Quaternion localRotation
    {
        get => m_localRotation;
        set
        {
            m_localRotation = value.normalized;
            MarkDirty();
        }
    }
    
    #endregion

    #region World Properties

    /// <summary>
    /// World position in global space.
    /// </summary>
    public Vector3 worldPosition
    {
        get
        {
            UpdateIfDirty();
            return m_worldPosition;
        }
        set
        {
            if (parent == null)
            {
                localPosition = value;
            }
            else
            {
                var invParentRot = Quaternion.Inverse(parent.worldRotation);
                var parentScale = parent.worldScale;

                var delta = value - parent.worldPosition;
                var scaled = new Vector3(delta.x / parentScale.x, delta.y / parentScale.y, delta.z / parentScale.z);
                localPosition = Vector3.Transform(scaled, invParentRot);
            }
            MarkDirty();
        }
    }

    /// <summary>
    /// World rotation in global space.
    /// </summary>
    public Quaternion worldRotation
    {
        get
        {
            UpdateIfDirty();
            return m_worldRotation;
        }
        set
        {
            if (parent == null)
            {
                localRotation = value;
            }
            else
            {
                var invParentRot = Quaternion.Inverse(parent.worldRotation);
                localRotation = invParentRot * value;
            }
            MarkDirty();
        }
    }

    /// <summary>
    /// World scale in global space.
    /// </summary>
    public Vector3 worldScale
    {
        get
        {
            UpdateIfDirty();
            return m_worldScale;
        }
        set
        {
            if (parent == null)
            {
                localScale = value;
            }
            else
            {
                var parentScale = parent.worldScale;
                localScale = new Vector3(
                    value.x / parentScale.x,
                    value.y / parentScale.y,
                    value.z / parentScale.z
                );
            }
            MarkDirty();
        }
    }

    #endregion
    
    #region Parent Properties
    
    /// <summary>
    /// Parent transform. Null if root.
    /// </summary>
    public Transform? parent { get; private set; }
    [SerializableProperty(PropertyVisibility.Hide)] internal Guid parentId { get; private set; } = Guid.Empty;

    /// <summary>
    /// Read-only list of children transforms.
    /// </summary>
    public IReadOnlyList<Transform> children => m_children.AsReadOnly();
    
    #endregion
    
    #region APIs

    /// <summary>
    /// Sets the parent transform.
    /// If worldPositionStays is true, keeps the world transform unchanged after reparenting.
    /// </summary>
    public void SetParent(Transform? newParent, bool worldTransformStays = true)
    {
        if (parent == newParent)
            return;

        if (newParent != null && m_children.Contains(newParent))
            newParent.SetParent(parent);
        
        UpdateIfDirty(); // Ensure current world transform is up to date

        if (worldTransformStays)
        {
            // Calculate new local transform to keep world transform same after reparent
            var currentWorldPos = worldPosition;
            var currentWorldRot = worldRotation;
            var currentWorldScale = worldScale;

            // Remove from old parent
            parent?.m_children.Remove(this);

            parent = newParent;
            parent?.m_children.Add(this);

            // Compute new local transform from world transform and new parent
            if (parent == null)
            {
                localPosition = currentWorldPos;
                localRotation = currentWorldRot;
                localScale = currentWorldScale;
            }
            else
            {
                var invParentRot = Quaternion.Inverse(parent.worldRotation);
                var parentScale = parent.worldScale;
                
                var delta = (currentWorldPos - parent.worldPosition);
                var scaled = new Vector3(delta.x / parentScale.x, delta.y / parentScale.y, delta.z / parentScale.z);
                
                localPosition = Vector3.Transform(scaled, invParentRot);
                localRotation = invParentRot * currentWorldRot;
                localScale = new Vector3(
                    currentWorldScale.x / parentScale.x,
                    currentWorldScale.y / parentScale.y,
                    currentWorldScale.z / parentScale.z
                );
            }
        }
        else
        {
            // Just reparent, local transform remains same
            parent?.m_children.Remove(this);
            parent = newParent;
            parent?.m_children.Add(this);
        }

        parentId = newParent == null ? Guid.Empty : newParent.gameObject.id;
        MarkDirty();
    }

    /// <summary>
    /// Updates this transform (called each frame by ECS).
    /// </summary>
    public override void Update()
    {
        UpdateIfDirty();
    }

    /// <summary>
    /// Called when the component is detached, cleans up parent and children references.
    /// </summary>
    public override void OnDetach()
    {
        SetParent(null);
        foreach (var child in m_children.ToArray())
        {
            child.SetParent(null);
        }
        m_children.Clear();
    }
    
    #endregion
    
    #region Helpers
    
    private void MarkDirty()
    {
        m_isDirty = true;
        foreach (var child in m_children)
            child.MarkDirty();
    }
    
    private void UpdateIfDirty()
    {
        if (!m_isDirty)
            return;

        if (parent == null)
        {
            m_worldPosition = m_localPosition;
            m_worldRotation = m_localRotation;
            m_worldScale = m_localScale;
        }
        else
        {
            // Compose world transform: scale, rotate, translate
            m_worldScale = new Vector3(
                m_localScale.x * parent.worldScale.x,
                m_localScale.y * parent.worldScale.y,
                m_localScale.z * parent.worldScale.z
            );

            m_worldRotation = parent.worldRotation * m_localRotation;
            
            var scaled = new Vector3(m_localPosition.x * parent.worldScale.x, m_localPosition.y * parent.worldScale.y, m_localPosition.z * parent.worldScale.z);
            var rotated = Vector3.Transform(scaled, parent.worldRotation);

            m_worldPosition = parent.worldPosition + rotated;
        }

        OnTransformChanged?.Invoke();
        m_isDirty = false;
    }
    
    #endregion
}

