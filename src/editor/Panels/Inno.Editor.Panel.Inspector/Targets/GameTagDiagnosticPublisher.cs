using System;
using System.Collections.Generic;
using System.Linq;

using Inno.Core.Diagnostics;
using Inno.Scene;

namespace Inno.Editor.Panel.Inspector;

/// <summary>
/// Publishes targeted errors for loaded GameObjects whose assigned tag is undefined.
/// </summary>
internal sealed class GameTagDiagnosticPublisher
{
    private const string C_DIAGNOSTIC_GROUP = "GameObject Tag Assignment";

    private readonly SceneWorld m_world;
    private readonly DiagnosticHub m_diagnostics;
    private readonly HashSet<Guid> m_activeTargets = [];

    internal GameTagDiagnosticPublisher(SceneWorld world, DiagnosticHub diagnostics)
    {
        m_world = world ?? throw new ArgumentNullException(nameof(world));
        m_diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
    }

    /// <summary>
    /// Reconciles diagnostics against every GameObject in the loaded Scene setup.
    /// </summary>
    /// <param name="catalog">
    /// The active project tag catalog.
    /// </param>
    internal void Refresh(GameTagCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        var currentTargets = new HashSet<Guid>();
        IReadOnlyList<GameScene> scenes = m_world.loadedScenes;
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
                m_diagnostics.Set(
                    CreateSource(targetId, gameObject.name),
                    [Diagnostic.Error(
                        "GAMEOBJECT-TAG-UNDEFINED",
                        $"GameObject '{gameObject.name}' uses undefined tag '{gameObject.tag}'.")]);
            }
        }

        foreach (Guid targetId in m_activeTargets.Where(id => !currentTargets.Contains(id)).ToArray())
            m_diagnostics.Clear(CreateSource(targetId, targetId.ToString("D")));
        m_activeTargets.Clear();
        m_activeTargets.UnionWith(currentTargets);
    }

    /// <summary>
    /// Clears every targeted tag-assignment diagnostic owned by this publisher.
    /// </summary>
    internal void Clear()
    {
        foreach (Guid targetId in m_activeTargets)
            m_diagnostics.Clear(CreateSource(targetId, targetId.ToString("D")));
        m_activeTargets.Clear();
    }

    private static DiagnosticSource CreateSource(Guid targetId, string displayName)
        => new($"editor.scene.tag:{targetId:N}", $"{C_DIAGNOSTIC_GROUP}: {displayName}");
}
