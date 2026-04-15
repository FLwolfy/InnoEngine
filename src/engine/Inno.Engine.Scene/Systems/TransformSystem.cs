using System.Collections.Generic;
using Inno.Core.ECS;
using Inno.Core.Mathematics;
using Inno.Engine.Scene.Components;

namespace Inno.Engine.Scene.Systems;

/// <summary>
/// Handles transform hierarchy transactions and world transform propagation.
/// </summary>
public sealed class TransformSystem : System<Transform>
{
    private readonly HashSet<Transform> m_all = [];
    private readonly HashSet<Transform> m_roots = [];

    public override int order => -1000;

    protected override void Process(World world, float deltaTime)
    {
        IReadOnlyList<Transform> transforms = world.ViewComponents<Transform>();
        if (transforms.Count == 0)
        {
            return;
        }

        BuildSets(transforms);
        ResolveParentTransactions();
        RefreshRoots();
        UpdateHierarchies();
    }

    private void BuildSets(IReadOnlyList<Transform> transforms)
    {
        m_all.Clear();
        m_roots.Clear();

        for (int i = 0; i < transforms.Count; i++)
        {
            Transform transform = transforms[i];
            if (!transform.enabled)
            {
                continue;
            }

            m_all.Add(transform);
        }
    }

    private void ResolveParentTransactions()
    {
        foreach (Transform transform in m_all)
        {
            if (!transform.TryConsumeParentTransaction(
                    out Transform? requestedParent,
                    out TransformParentOptions options,
                    out Vector3 worldPosition,
                    out Quaternion worldRotation,
                    out Vector3 worldScale))
            {
                continue;
            }

            Transform? targetParent = requestedParent;
            if (targetParent is not null && !m_all.Contains(targetParent))
            {
                targetParent = null;
            }

            if (targetParent is not null)
            {
                if (ReferenceEquals(targetParent, transform))
                {
                    targetParent = null;
                }
                else if (WouldCreateCycle(transform, targetParent))
                {
                    continue;
                }
            }

            transform.ApplyParentFromSystem(targetParent);

            if (options == TransformParentOptions.KeepWorld)
            {
                transform.ApplyLocalFromWorldFromSystem(worldPosition, worldRotation, worldScale);
            }
            else if (options == TransformParentOptions.SnapToParent)
            {
                transform.SnapLocalToIdentityFromSystem();
            }
        }
    }

    private void RefreshRoots()
    {
        foreach (Transform transform in m_all)
        {
            Transform? parent = transform.parent;
            if (parent is null || !m_all.Contains(parent))
            {
                if (parent is not null)
                {
                    transform.ApplyParentFromSystem(null);
                }

                m_roots.Add(transform);
            }
        }
    }

    private void UpdateHierarchies()
    {
        foreach (Transform root in m_roots)
        {
            root.ApplyWorldHierarchyFromSystem(parentDirty: false);
        }
    }

    private static bool WouldCreateCycle(Transform transform, Transform requestedParent)
    {
        Transform? current = requestedParent;
        while (current is not null)
        {
            if (ReferenceEquals(current, transform))
            {
                return true;
            }

            current = current.parent;
        }

        return false;
    }
}
