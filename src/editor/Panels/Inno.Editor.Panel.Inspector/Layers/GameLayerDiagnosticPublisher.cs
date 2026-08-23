using System;
using System.Collections.Generic;
using System.Linq;

using Inno.Core.Diagnose;
using Inno.Engine.Scene;
using Inno.Engine.Scene.Layers;

namespace Inno.Editor.Panel.Inspector;

/// <summary>
/// Publishes targeted errors for loaded GameObjects whose layer slot is not currently defined.
/// </summary>
internal sealed class GameLayerDiagnosticPublisher
{
    private const string C_DIAGNOSTIC_GROUP = "GameObject Layer Assignment";

    private readonly HashSet<Guid> m_activeTargets = [];

    /// <summary>
    /// Reconciles diagnostics against every GameObject in the loaded Scene setup.
    /// </summary>
    /// <param name="stack">The active project layer catalog.</param>
    internal void Refresh(GameLayerStack stack)
    {
        ArgumentNullException.ThrowIfNull(stack);
        var currentTargets = new HashSet<Guid>();
        IReadOnlyList<GameScene> scenes = SceneManager.loadedScenes;
        for (int sceneIndex = 0; sceneIndex < scenes.Count; sceneIndex++)
        {
            IReadOnlyList<GameObject> objects = scenes[sceneIndex].GetObjects();
            for (int objectIndex = 0; objectIndex < objects.Count; objectIndex++)
            {
                GameObject gameObject = objects[objectIndex];
                if (stack.IsDefined(gameObject.layer))
                    continue;
                Guid targetId = gameObject.identity.persistentId;
                currentTargets.Add(targetId);
                if (m_activeTargets.Contains(targetId))
                    continue;
                Diagnostics.Set(
                    targetId,
                    C_DIAGNOSTIC_GROUP,
                    Diagnostic.Error(
                        "GAMEOBJECT-LAYER-UNDEFINED",
                        $"GameObject '{gameObject.name}' uses undefined layer slot {gameObject.layer.index}."),
                    gameObject.name);
            }
        }

        foreach (Guid targetId in m_activeTargets.Where(id => !currentTargets.Contains(id)).ToArray())
            Diagnostics.Clear(targetId, C_DIAGNOSTIC_GROUP);
        m_activeTargets.Clear();
        m_activeTargets.UnionWith(currentTargets);
    }

    /// <summary>
    /// Clears every targeted layer-assignment diagnostic owned by this publisher.
    /// </summary>
    internal void Clear()
    {
        foreach (Guid targetId in m_activeTargets)
            Diagnostics.Clear(targetId, C_DIAGNOSTIC_GROUP);
        m_activeTargets.Clear();
    }
}
