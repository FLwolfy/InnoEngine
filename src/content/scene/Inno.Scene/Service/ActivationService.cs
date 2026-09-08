using System.Collections.Generic;

using Inno.Scene.Components;

namespace Inno.Scene;

/// <summary>
/// Maintains derived hierarchy activation state.
/// </summary>
internal sealed class ActivationService
{
    internal void SetActive(GameObject gameObject, bool active)
    {
        if (gameObject.activeSelf == active)
            return;
        gameObject.SetActiveSelfDirect(active);
        RecomputeSubtree(gameObject);
    }

    internal void RecomputeSubtree(GameObject gameObject)
    {
        bool parentActive = gameObject.transform.parent?.gameObject.activeInHierarchy ?? true;
        Recompute(gameObject, parentActive);
    }

    private static void Recompute(GameObject gameObject, bool parentActive)
    {
        bool active = parentActive && gameObject.activeSelf;
        gameObject.SetActiveInHierarchyDirect(active);
        IReadOnlyList<Transform> children = gameObject.transform.children;
        for (int i = 0; i < children.Count; i++)
            Recompute(children[i].gameObject, active);
    }
}
