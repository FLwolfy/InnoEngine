using System;
using System.Collections.Generic;

using Inno.Extensibility.Types;
using Inno.Scene;

namespace Inno.Editor.Scene;

internal sealed class SceneStateDiagnosticTracker
{
    private readonly SceneWorld m_world;
    private readonly TypeCatalog m_types;
    private WeakReference<GameScene>[] m_scenes = [];
    private long m_typeCacheVersion = -1;

    internal SceneStateDiagnosticTracker(SceneWorld world, TypeCatalog types)
    {
        m_world = world ?? throw new ArgumentNullException(nameof(world));
        m_types = types ?? throw new ArgumentNullException(nameof(types));
    }

    internal void Reconcile(bool force = false)
    {
        IReadOnlyList<GameScene> scenes = m_world.loadedScenes;
        long typeCacheVersion = m_types.isInitialized
            ? m_types.current.version
            : -1;
        bool scenesChanged = scenes.Count != m_scenes.Length;
        if (!scenesChanged)
        {
            for (int i = 0; i < scenes.Count; i++)
            {
                if (m_scenes[i].TryGetTarget(out GameScene? tracked) &&
                    ReferenceEquals(scenes[i], tracked))
                {
                    continue;
                }
                scenesChanged = true;
                break;
            }
        }
        if (!force && !scenesChanged && typeCacheVersion == m_typeCacheVersion)
            return;

        SceneStateDiagnosticPublisher.PublishMissingElements(scenes);
        var references = new WeakReference<GameScene>[scenes.Count];
        for (int i = 0; i < scenes.Count; i++)
            references[i] = new WeakReference<GameScene>(scenes[i]);
        m_scenes = references;
        m_typeCacheVersion = typeCacheVersion;
    }
}
