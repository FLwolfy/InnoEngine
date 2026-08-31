using System;
using System.Collections.Generic;

using Inno.Core.Reflection;
using Inno.Engine.Scene;

namespace Inno.Editor.Scene;

internal sealed class SceneStateDiagnosticTracker
{
    private WeakReference<GameScene>[] m_scenes = [];
    private long m_typeCacheVersion = -1;

    internal void Reconcile(bool force = false)
    {
        IReadOnlyList<GameScene> scenes = SceneManager.loadedScenes;
        long typeCacheVersion = TypeCacheManager.isInitialized
            ? TypeCacheManager.current.version
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

        SceneStateDiagnosticPublisher.PublishMissingElements();
        var references = new WeakReference<GameScene>[scenes.Count];
        for (int i = 0; i < scenes.Count; i++)
            references[i] = new WeakReference<GameScene>(scenes[i]);
        m_scenes = references;
        m_typeCacheVersion = typeCacheVersion;
    }
}
