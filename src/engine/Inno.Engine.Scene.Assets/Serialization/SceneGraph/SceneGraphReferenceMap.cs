using System;
using System.Collections.Generic;
using System.Threading;

using Inno.Engine.Scene;

namespace Inno.Engine.Scene.Assets;

internal sealed class SceneGraphReferenceMap
{
    private static readonly AsyncLocal<SceneGraphReferenceMap?> C_CURRENT = new();

    private readonly Dictionary<EngineObject, Guid> m_sourceIdByObject = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Guid, EngineObject> m_objectBySourceId = new();
    private readonly HashSet<EngineObject> m_allowedObjects = new(ReferenceEqualityComparer.Instance);
    private readonly GameScene m_scene;
    private readonly GameObject? m_captureRoot;

    internal SceneGraphReferenceMap(
        GameScene scene,
        IEnumerable<EngineObject>? allowedObjects = null,
        IReadOnlyDictionary<EngineObject, Guid>? sourceIds = null,
        GameObject? captureRoot = null)
    {
        m_scene = scene ?? throw new ArgumentNullException(nameof(scene));
        m_captureRoot = captureRoot;
        if (allowedObjects is null)
            return;

        foreach (EngineObject engineObject in allowedObjects)
        {
            m_allowedObjects.Add(engineObject);
            m_sourceIdByObject.Add(
                engineObject,
                sourceIds is not null && sourceIds.TryGetValue(engineObject, out Guid sourceId)
                    ? sourceId
                    : engineObject.identity.persistentId);
        }
    }

    internal static SceneGraphReferenceMap current
        => C_CURRENT.Value ?? throw new InvalidOperationException(
            "A GameObject or GameComponent reference requires an active scene graph serialization boundary.");

    internal static bool TryGetCurrent(out SceneGraphReferenceMap? references)
    {
        references = C_CURRENT.Value;
        return references is not null;
    }

    internal IDisposable Enter()
    {
        SceneGraphReferenceMap? previous = C_CURRENT.Value;
        C_CURRENT.Value = this;
        return new Scope(previous);
    }

    internal static IDisposable Suspend()
    {
        SceneGraphReferenceMap? previous = C_CURRENT.Value;
        C_CURRENT.Value = null;
        return new Scope(previous);
    }

    internal EngineReferenceToken Capture(EngineObject engineObject, string path)
    {
        ArgumentNullException.ThrowIfNull(engineObject);
        if (engineObject.isDestroyed ||
            !m_allowedObjects.Contains(engineObject) ||
            !m_sourceIdByObject.TryGetValue(engineObject, out Guid sourceId))
        {
            throw new InvalidOperationException(
                $"Reference to '{engineObject.GetType().FullName}' with identity " +
                $"'{engineObject.identity.persistentId}' at '{path}' is outside the serialized graph boundary" +
                (m_captureRoot is null
                    ? "."
                    : $" rooted at GameObject '{m_captureRoot.name}' ({m_captureRoot.identity.persistentId})."));
        }

        GameScene ownerScene = engineObject switch
        {
            GameObject gameObject => gameObject.scene,
            GameComponent component => component.gameObject.scene,
            GameSystem system => system.ownerScene
                ?? throw new InvalidOperationException($"Detached GameSystem reference at '{path}' cannot be serialized."),
            _ => throw new InvalidOperationException(
                $"Engine object type '{engineObject.GetType().FullName}' cannot be serialized as a scene reference at '{path}'.")
        };
        if (!ReferenceEquals(ownerScene, m_scene))
            throw new InvalidOperationException($"Cross-scene object reference at '{path}' cannot be serialized.");

        EngineReferenceKind kind = engineObject switch
        {
            GameObject => EngineReferenceKind.GameObject,
            GameComponent => EngineReferenceKind.GameComponent,
            GameSystem => EngineReferenceKind.GameSystem,
            _ => throw new InvalidOperationException($"Unsupported scene reference at '{path}'.")
        };
        return new EngineReferenceToken(kind, sourceId);
    }

    internal EngineObject Resolve(EngineReferenceToken token, Type expectedType, string path)
    {
        if (!m_objectBySourceId.TryGetValue(token.sourceId, out EngineObject? engineObject))
        {
            throw new InvalidOperationException(
                $"Scene graph reference '{token.sourceId}' at '{path}' could not be resolved.");
        }
        if (!expectedType.IsInstanceOfType(engineObject))
        {
            throw new InvalidOperationException(
                $"Scene graph reference '{token.sourceId}' at '{path}' resolves to " +
                $"'{engineObject.GetType().FullName}', expected '{expectedType.FullName}'.");
        }
        return engineObject;
    }

    internal void Register(Guid sourceId, EngineObject engineObject)
    {
        if (!m_objectBySourceId.TryAdd(sourceId, engineObject) &&
            !ReferenceEquals(m_objectBySourceId[sourceId], engineObject))
        {
            throw new InvalidOperationException($"Duplicate scene graph local identity '{sourceId}'.");
        }
    }

    internal EngineObject GetRegistered(Guid sourceId)
        => m_objectBySourceId.TryGetValue(sourceId, out EngineObject? engineObject)
            ? engineObject
            : throw new InvalidOperationException(
                $"Scene graph reference alias target '{sourceId}' could not be resolved.");

    private sealed class Scope(SceneGraphReferenceMap? previous) : IDisposable
    {
        private SceneGraphReferenceMap? m_previous = previous;

        public void Dispose()
        {
            C_CURRENT.Value = m_previous;
            m_previous = null;
        }
    }
}
