using System;
using System.Collections.Generic;
using System.Linq;

using Inno.Core.Diagnose;
using Inno.Engine.Scene;

namespace Inno.Editor.Panel.Inspector;

/// <summary>Publishes targeted errors for loaded GameObjects whose assigned tag is undefined.</summary>
internal sealed class GameTagDiagnosticPublisher
{
    private const string C_DIAGNOSTIC_GROUP = "GameObject Tag Assignment";

    private readonly HashSet<Guid> m_activeTargets = [];

    /// <summary>Reconciles diagnostics against every GameObject in the loaded Scene setup.</summary>
    /// <param name="catalog">The active project tag catalog.</param>
    internal void Refresh(GameTagCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        var currentTargets = new HashSet<Guid>();
        IReadOnlyList<GameScene> scenes = SceneManager.loadedScenes;
        for (int sceneIndex = 0; sceneIndex < scenes.Count; sceneIndex++)
        {
            IReadOnlyList<GameObject> objects = scenes[sceneIndex].GetObjects();
            for (int objectIndex = 0; objectIndex < objects.Count; objectIndex++)
            {
                GameObject gameObject = objects[objectIndex];
                if (catalog.IsDefined(gameObject.tag))
                    continue;
                Guid targetId = gameObject.identity.persistentId;
                currentTargets.Add(targetId);
                if (m_activeTargets.Contains(targetId))
                    continue;
                Diagnostics.Set(
                    targetId,
                    C_DIAGNOSTIC_GROUP,
                    Diagnostic.Error(
                        "GAMEOBJECT-TAG-UNDEFINED",
                        $"GameObject '{gameObject.name}' uses undefined tag '{gameObject.tag}'."),
                    gameObject.name);
            }
        }

        foreach (Guid targetId in m_activeTargets.Where(id => !currentTargets.Contains(id)).ToArray())
            Diagnostics.Clear(targetId, C_DIAGNOSTIC_GROUP);
        m_activeTargets.Clear();
        m_activeTargets.UnionWith(currentTargets);
    }

    /// <summary>Clears every targeted tag-assignment diagnostic owned by this publisher.</summary>
    internal void Clear()
    {
        foreach (Guid targetId in m_activeTargets)
            Diagnostics.Clear(targetId, C_DIAGNOSTIC_GROUP);
        m_activeTargets.Clear();
    }
}
