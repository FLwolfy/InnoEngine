using System.Collections.Generic;

using Inno.Core.ECS;
using Inno.Core.Mathematics;
using Inno.Engine.Scene.Components;
using EcsSystem = Inno.Core.ECS.System;

namespace Inno.Engine.Scene.Systems;

/// <summary>
/// Resolves hierarchy and propagates transform local/world TRS state.
/// </summary>
public sealed class TransformSystem : EcsSystem
{
    private readonly Dictionary<int, Transform> m_transformsByEntity = new();
    private readonly Dictionary<int, int> m_ownerByTransform = new();
    private readonly Dictionary<int, List<Transform>> m_childrenByParent = new();
    private readonly Dictionary<int, int?> m_previousParentByEntity = new();
    private readonly HashSet<int> m_seenThisFrame = [];
    private readonly List<Transform> m_roots = [];

    public override int order => -1000;

    public override void Process(World world, float deltaTime)
    {
        BuildTransformMap(world);
        if (m_transformsByEntity.Count == 0)
        {
            m_previousParentByEntity.Clear();
            return;
        }

        ValidateParents();
        PreserveWorldForParentChanges();
        BuildHierarchyCache();
        PropagateActiveState(world);
        SnapshotParents();
    }

    private void BuildTransformMap(World world)
    {
        ClearFrameCaches();

        foreach (Entity entity in world.ViewEntitiesFast())
        {
            if (entity.identity.runtimeId is not int ownerId)
            {
                continue;
            }

            IReadOnlyList<Transform> transforms = world.ViewComponents<Transform>(ownerId);
            for (int i = 0; i < transforms.Count; i++)
            {
                Transform transform = transforms[i];
                int transformId = GetTransformRuntimeId(transform);
                if (transformId == 0)
                {
                    continue;
                }

                m_transformsByEntity[transformId] = transform;
                m_ownerByTransform[transformId] = ownerId;
                transform.SetWorldResolver(ResolveWorldState);
                m_seenThisFrame.Add(transformId);
            }
        }
    }

    private void ValidateParents()
    {
        foreach (Transform transform in m_transformsByEntity.Values)
        {
            int transformId = GetTransformRuntimeId(transform);
            int? parentTransformId = transform.parentTransformId;
            if (parentTransformId is null)
            {
                continue;
            }

            if (parentTransformId.Value == transformId
                || !m_transformsByEntity.ContainsKey(parentTransformId.Value)
                || WouldCreateCycle(transformId, parentTransformId.Value))
            {
                transform.parentTransformId = null;
            }
        }
    }

    private void PreserveWorldForParentChanges()
    {
        foreach (Transform transform in m_transformsByEntity.Values)
        {
            int transformId = GetTransformRuntimeId(transform);
            int? currentParent = transform.parentTransformId;
            bool hadPreviousParent = m_previousParentByEntity.TryGetValue(transformId, out int? previousParent);
            if (hadPreviousParent && previousParent == currentParent)
            {
                continue;
            }

            if (!hadPreviousParent && currentParent is null)
            {
                continue;
            }

            TransformWorldState previousWorld = ResolveWorldState(transform, previousParent);
            ApplyLocalFromWorld(transform, previousWorld);
        }
    }

    private void BuildHierarchyCache()
    {
        m_childrenByParent.Clear();
        m_roots.Clear();

        foreach (Transform transform in m_transformsByEntity.Values)
        {
            int? parentTransformId = transform.parentTransformId;
            if (parentTransformId is null || !m_transformsByEntity.ContainsKey(parentTransformId.Value))
            {
                transform.parentTransformId = null;
                m_roots.Add(transform);
                continue;
            }

            if (!m_childrenByParent.TryGetValue(parentTransformId.Value, out List<Transform>? children))
            {
                children = [];
                m_childrenByParent[parentTransformId.Value] = children;
            }

            children.Add(transform);
        }
    }

    private void PropagateActiveState(World world)
    {
        for (int i = 0; i < m_roots.Count; i++)
        {
            PropagateActiveState(world, m_roots[i], parentActive: true);
        }
    }

    private void PropagateActiveState(World world, Transform transform, bool parentActive)
    {
        bool active = parentActive;
        int transformId = GetTransformRuntimeId(transform);
        IReadOnlyList<ActiveState> activeStates = m_ownerByTransform.TryGetValue(transformId, out int ownerId)
            ? world.ViewComponents<ActiveState>(ownerId)
            : [];
        if (activeStates.Count != 0)
        {
            ActiveState state = activeStates[0];
            active = parentActive && state.selfActive;
            state.activeInHierarchy = active;
        }

        if (!m_childrenByParent.TryGetValue(transformId, out List<Transform>? children))
        {
            return;
        }

        for (int i = 0; i < children.Count; i++)
        {
            PropagateActiveState(world, children[i], active);
        }
    }

    private void ApplyLocalFromWorld(Transform transform, TransformWorldState worldState)
    {
        if (transform.parentTransformId is not int parentTransformId
            || !m_transformsByEntity.TryGetValue(parentTransformId, out Transform? parent))
        {
            transform.localPosition = worldState.position;
            transform.localRotation = worldState.rotation.normalized;
            transform.localScale = worldState.scale;
            return;
        }

        TransformWorldState parentWorld = ResolveWorldState(parent);
        Quaternion invParentRot = Quaternion.Inverse(parentWorld.rotation);
        Vector3 parentScale = parentWorld.scale;
        Vector3 delta = worldState.position - parentWorld.position;
        Vector3 scaled = new(
            SafeDiv(delta.x, parentScale.x),
            SafeDiv(delta.y, parentScale.y),
            SafeDiv(delta.z, parentScale.z));

        transform.localPosition = Vector3.Transform(scaled, invParentRot);
        transform.localRotation = (invParentRot * worldState.rotation).normalized;
        transform.localScale = new Vector3(
            SafeDiv(worldState.scale.x, parentScale.x),
            SafeDiv(worldState.scale.y, parentScale.y),
            SafeDiv(worldState.scale.z, parentScale.z));
    }

    private TransformWorldState ResolveWorldState(Transform transform)
        => ResolveWorldState(transform, transform.parentTransformId, []);

    private TransformWorldState ResolveWorldState(Transform transform, int? parentTransformId)
        => ResolveWorldState(transform, parentTransformId, []);

    private TransformWorldState ResolveWorldState(
        Transform transform,
        int? parentTransformId,
        HashSet<int> resolving)
    {
        int transformId = GetTransformRuntimeId(transform);
        if (!resolving.Add(transformId))
        {
            return new TransformWorldState(transform.localPosition, transform.localRotation.normalized, transform.localScale);
        }

        if (parentTransformId is not int parentId || !m_transformsByEntity.TryGetValue(parentId, out Transform? parent))
        {
            resolving.Remove(transformId);
            return new TransformWorldState(transform.localPosition, transform.localRotation.normalized, transform.localScale);
        }

        TransformWorldState parentWorld = ResolveWorldState(parent, parent.parentTransformId, resolving);
        Vector3 parentScale = parentWorld.scale;
        Quaternion parentRotation = parentWorld.rotation;

        Vector3 worldScale = new(
            transform.localScale.x * parentScale.x,
            transform.localScale.y * parentScale.y,
            transform.localScale.z * parentScale.z);

        Quaternion worldRotation = (parentRotation * transform.localRotation).normalized;

        Vector3 scaled = new(
            transform.localPosition.x * parentScale.x,
            transform.localPosition.y * parentScale.y,
            transform.localPosition.z * parentScale.z);

        Vector3 worldPosition = parentWorld.position + Vector3.Transform(scaled, parentRotation);
        resolving.Remove(transformId);
        return new TransformWorldState(worldPosition, worldRotation, worldScale);
    }

    private bool WouldCreateCycle(int transformEntityId, int requestedParentEntityId)
    {
        int? current = requestedParentEntityId;
        while (current is int currentEntityId)
        {
            if (currentEntityId == transformEntityId)
            {
                return true;
            }

            current = m_transformsByEntity.TryGetValue(currentEntityId, out Transform? currentTransform)
                ? currentTransform.parentTransformId
                : null;
        }

        return false;
    }

    private void SnapshotParents()
    {
        List<int> stale = [];
        foreach (int entityId in m_previousParentByEntity.Keys)
        {
            if (!m_seenThisFrame.Contains(entityId))
            {
                stale.Add(entityId);
            }
        }

        for (int i = 0; i < stale.Count; i++)
        {
            m_previousParentByEntity.Remove(stale[i]);
        }

        foreach (Transform transform in m_transformsByEntity.Values)
        {
            m_previousParentByEntity[GetTransformRuntimeId(transform)] = transform.parentTransformId;
        }
    }

    private void ClearFrameCaches()
    {
        m_transformsByEntity.Clear();
        m_ownerByTransform.Clear();
        m_childrenByParent.Clear();
        m_seenThisFrame.Clear();
        m_roots.Clear();
    }

    private static float SafeDiv(float value, float divisor)
    {
        return MathHelper.AlmostEquals(divisor, 0f) ? 0f : value / divisor;
    }

    private static int GetTransformRuntimeId(Transform transform)
        => transform.identity.runtimeId ?? 0;
}
