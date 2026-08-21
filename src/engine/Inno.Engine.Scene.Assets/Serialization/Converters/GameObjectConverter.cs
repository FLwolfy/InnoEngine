using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Inno.Assets.Core;
using Inno.Core.Serialization;
using Inno.Core.Serialization.Converters;
using Inno.Engine.Scene;
using Inno.Engine.Scene.Components;

namespace Inno.Engine.Scene.Assets;

[SerializationExtension]
internal sealed class GameObjectConverter : SerializationConverter<GameObject>
{
    private const string C_SOURCE_ROOT_ID_KEY = "sourceRootId";

    public override void Write(SerializationWriter writer, GameObject value)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(value);
        if (SceneGraphReferenceMap.TryGetCurrent(out SceneGraphReferenceMap? activeReferences))
        {
            WriteReference(writer, activeReferences!, value);
            return;
        }

        if (!value.isRuntimeValid)
            throw new InvalidOperationException("A destroyed or detached GameObject cannot be captured as a prefab root.");

        var subtree = new List<GameObject>();
        CollectSubtree(value, subtree);
        SceneStructureSnapshot snapshot = value.scene.CaptureStructure();
        var included = new HashSet<GameObject>(subtree, ReferenceEqualityComparer.Instance);
        SceneObjectStructureSnapshot[] entries = snapshot.objects
            .Where(entry => included.Contains(entry.gameObject))
            .ToArray();
        EngineObject[] engineObjects = entries
            .SelectMany(static entry => entry.components.Cast<EngineObject>().Prepend(entry.gameObject))
            .ToArray();
        var sourceIds = new Dictionary<EngineObject, Guid>(ReferenceEqualityComparer.Instance);
        PrefabConnectionRecord? existingConnection = value.prefabConnection;
        var mappedIds = new Dictionary<Guid, Guid>();
        if (existingConnection is not null)
        {
            foreach ((Guid sourceId, Guid runtimeId) in existingConnection.objectIdentities)
                mappedIds[runtimeId] = sourceId;
            foreach ((Guid sourceId, Guid runtimeId) in existingConnection.componentIdentities)
                mappedIds[runtimeId] = sourceId;
        }
        for (int i = 0; i < engineObjects.Length; i++)
        {
            sourceIds.Add(
                engineObjects[i],
                mappedIds.TryGetValue(engineObjects[i].identity.persistentId, out Guid mappedId)
                    ? mappedId
                    : Guid.NewGuid());
        }

        var references = new SceneGraphReferenceMap(value.scene, engineObjects, sourceIds, value);
        writer.Write(C_SOURCE_ROOT_ID_KEY, sourceIds[value]);
        using (references.Enter())
        {
            SceneGraphSerialization.WriteObjects(
                writer,
                entries,
                sourceIds,
                preserveRootSiblingOrder: false);
        }
    }

    public override GameObject Read(SerializationReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        if (SceneGraphReferenceMap.TryGetCurrent(out SceneGraphReferenceMap? activeReferences))
            return (GameObject)ReadReference(reader, activeReferences!);

        GameScene scene = reader.context.GetRequired<GameScene>();
        Transform? parent = null;
        if (reader.context.TryGet(out Transform? configuredParent))
            parent = configuredParent;
        if (parent is not null && !ReferenceEquals(parent.gameObject.scene, scene))
            throw new InvalidOperationException("The prefab parent must belong to the target scene.");

        ValidatePrefab(reader);
        var existingObjects = new HashSet<GameObject>(scene.GetObjects(), ReferenceEqualityComparer.Instance);
        try
        {
            var references = new SceneGraphReferenceMap(scene);
            RestoredSceneGraph restored = SceneGraphSerialization.RestoreObjects(
                scene,
                reader.ReadObjectArray(SceneGraphSerialization.C_OBJECTS_KEY),
                preservePersistentIds: false,
                references);
            Guid sourceRootId = reader.Read<Guid>(C_SOURCE_ROOT_ID_KEY);
            if (!restored.objects.TryGetValue(sourceRootId, out GameObject? root))
                throw new InvalidDataException($"Prefab root '{sourceRootId}' is missing at '{reader.path}'.");
            if (reader.context.TryGet(out AssetObject? sourceAsset) && sourceAsset is not null)
            {
                PrefabConnectionRecord? embeddedConnection = root.prefabConnection;
                bool sourceRepresentsVariant = embeddedConnection is not null &&
                    root.prefabInstance?.isRoot == true;
                if (sourceRepresentsVariant)
                {
                    PrefabOverrideProcessor.Reconcile(
                        embeddedConnection!,
                        root,
                        reader.context);
                }
                var connection = new PrefabConnectionRecord(
                    sourceAsset,
                    sourceRootId,
                    sourceRepresentsVariant);
                foreach ((Guid sourceObjectId, GameObject gameObject) in restored.objects)
                    connection.MapObject(sourceObjectId, gameObject);
                foreach ((Guid sourceComponentId, GameComponent component) in restored.components)
                    connection.MapComponent(sourceComponentId, component);
                if (sourceRepresentsVariant)
                {
                    foreach ((Guid sourceObjectId, Guid runtimeId) in embeddedConnection!.objectIdentities)
                    {
                        GameObject? mappedObject = scene.FindObject(runtimeId);
                        if (mappedObject is not null)
                            connection.MapObject(sourceObjectId, mappedObject);
                    }
                    foreach ((Guid sourceComponentId, Guid runtimeId) in embeddedConnection.componentIdentities)
                    {
                        GameComponent? mappedComponent = scene.FindComponent(runtimeId);
                        if (mappedComponent is not null)
                            connection.MapComponent(sourceComponentId, mappedComponent);
                    }
                }
                root.SetPrefabConnectionDirect(connection);

                foreach ((Guid sourceObjectId, Guid runtimeId) in connection.objectIdentities)
                {
                    GameObject? gameObject = scene.FindObject(runtimeId);
                    if (gameObject is null)
                        continue;
                    PrefabInstanceInfo? existingConnection = gameObject.prefabInstance;
                    if (existingConnection is not null &&
                        !ReferenceEquals(existingConnection.instanceRoot, root))
                    {
                        continue;
                    }
                    gameObject.SetPrefabInstanceDirect(new PrefabInstanceInfo(
                        sourceAsset.identity.persistentId,
                        sourceObjectId,
                        root,
                        ReferenceEquals(gameObject, root),
                        sourceRepresentsVariant,
                        sourceAsset.isMissing,
                        existingConnection?.overrideCount ?? 0,
                        existingConnection?.orphanedOverrideCount ?? 0,
                        sourceAsset));
                }
            }
            SceneGraphSerialization.ReconcilePrefabConnections(scene, reader.context, root);
            if (parent is not null)
                scene.SetParent(root.transform, parent, worldPositionStays: false);
            return root;
        }
        catch
        {
            GameObject[] createdObjects = scene.GetObjects()
                .Where(gameObject => !existingObjects.Contains(gameObject))
                .ToArray();
            for (int i = 0; i < createdObjects.Length; i++)
            {
                if (createdObjects[i].isRuntimeValid)
                    scene.DestroyObject(createdObjects[i]);
            }
            throw;
        }
    }

    private static void WriteReference(
        SerializationWriter writer,
        SceneGraphReferenceMap references,
        GameObject gameObject)
    {
        EngineReferenceToken token = references.Capture(gameObject, writer.path);
        writer.Write("kind", (int)token.kind);
        writer.Write("sourceId", token.sourceId);
    }

    private static EngineObject ReadReference(
        SerializationReader reader,
        SceneGraphReferenceMap references)
    {
        var token = new EngineReferenceToken(
            (EngineReferenceKind)reader.Read<int>("kind"),
            reader.Read<Guid>("sourceId"));
        if (token.kind != EngineReferenceKind.GameObject)
            throw new InvalidDataException($"Reference token at '{reader.path}' is not a GameObject reference.");
        return references.Resolve(token, reader.valueType, reader.path);
    }

    private static void ValidatePrefab(SerializationReader reader)
    {
        Guid rootId = reader.Read<Guid>(C_SOURCE_ROOT_ID_KEY);
        if (rootId == Guid.Empty)
            throw new InvalidDataException($"Prefab source root identity at '{reader.path}' cannot be empty.");
        IReadOnlyList<SerializationReader> objectReaders =
            reader.ReadObjectArray(SceneGraphSerialization.C_OBJECTS_KEY);
        SceneGraphSerialization.ValidateObjects(objectReaders);
        if (!objectReaders.Any(objectReader =>
                objectReader.Read<Guid>(SceneGraphSerialization.C_OBJECT_ID_KEY) == rootId))
        {
            throw new InvalidDataException($"Prefab root '{rootId}' is missing at '{reader.path}'.");
        }
    }

    private static void CollectSubtree(GameObject gameObject, ICollection<GameObject> result)
    {
        result.Add(gameObject);
        IReadOnlyList<Transform> children = gameObject.transform.children;
        for (int i = 0; i < children.Count; i++)
            CollectSubtree(children[i].gameObject, result);
    }
}
