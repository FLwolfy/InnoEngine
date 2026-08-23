using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Inno.Assets.Core;
using Inno.Core.Reflection;
using Inno.Core.Serialization;
using Inno.Engine.Scene;
using Inno.Engine.Scene.Components;
using Inno.Engine.Scene.Layers;

namespace Inno.Engine.Scene.Assets;

internal static class SceneGraphSerialization
{
    internal const string C_SCENE_ID_KEY = "sceneId";
    internal const string C_NAME_KEY = "name";
    internal const string C_TAG_KEY = "tag";
    internal const string C_LAYER_KEY = "layer";
    internal const string C_OBJECTS_KEY = "objects";
    internal const string C_SYSTEMS_KEY = "systems";
    internal const string C_SYSTEM_ID_KEY = "systemId";
    internal const string C_OBJECT_ID_KEY = "objectId";
    internal const string C_ACTIVE_SELF_KEY = "activeSelf";
    internal const string C_PARENT_ID_KEY = "parentId";
    internal const string C_SIBLING_INDEX_KEY = "siblingIndex";
    internal const string C_COMPONENTS_KEY = "components";
    internal const string C_COMPONENT_ID_KEY = "componentId";
    internal const string C_STABLE_TYPE_ID_KEY = "stableTypeId";
    internal const string C_STATE_KEY = "state";
    internal const string C_HAS_PREFAB_CONNECTION_KEY = "hasPrefabConnection";
    internal const string C_PREFAB_SOURCE_ASSET_ID_KEY = "prefabSourceAssetId";
    internal const string C_PREFAB_SOURCE_OBJECT_ID_KEY = "prefabSourceObjectId";
    internal const string C_PREFAB_INSTANCE_ROOT_ID_KEY = "prefabInstanceRootId";
    internal const string C_PREFAB_IS_VARIANT_KEY = "prefabIsVariant";
    internal const string C_PREFAB_IS_MISSING_KEY = "prefabIsMissing";
    internal const string C_PREFAB_OVERRIDE_COUNT_KEY = "prefabOverrideCount";
    internal const string C_PREFAB_ORPHANED_OVERRIDE_COUNT_KEY = "prefabOrphanedOverrideCount";
    internal const string C_PREFAB_SOURCE_ASSET_KEY = "prefabSourceAsset";
    internal const string C_PREFAB_COMPONENT_MAPPINGS_KEY = "prefabComponentMappings";
    internal const string C_PREFAB_MAPPING_SOURCE_ID_KEY = "sourceId";
    internal const string C_PREFAB_MAPPING_INSTANCE_ID_KEY = "instanceId";
    internal const string C_PREFAB_PROPERTY_OVERRIDES_KEY = "prefabPropertyOverrides";
    internal const string C_PREFAB_PROPERTY_NAME_KEY = "propertyName";
    internal const string C_PREFAB_PROPERTY_VALUE_KEY = "propertyValue";
    internal const string C_PREFAB_OVERRIDE_ORPHANED_KEY = "isOrphaned";
    internal const string C_PREFAB_STRUCTURE_OVERRIDES_KEY = "prefabStructureOverrides";
    internal const string C_PREFAB_STRUCTURE_KIND_KEY = "structureKind";
    internal const string C_PREFAB_REMOVED_OBJECTS_KEY = "prefabRemovedObjects";
    internal const string C_PREFAB_REMOVED_COMPONENTS_KEY = "prefabRemovedComponents";
    internal const string C_PREFAB_ADDED_OBJECTS_KEY = "prefabAddedObjects";
    internal const string C_PREFAB_ADDED_COMPONENTS_KEY = "prefabAddedComponents";

    internal static void WriteObjects(
        SerializationWriter writer,
        IReadOnlyCollection<SceneObjectStructureSnapshot> entries,
        IReadOnlyDictionary<EngineObject, Guid> sourceIds,
        bool preserveRootSiblingOrder)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(sourceIds);

        var included = new HashSet<GameObject>(
            entries.Select(static entry => entry.gameObject),
            ReferenceEqualityComparer.Instance);
        writer.WriteObjectArray(C_OBJECTS_KEY, entries, (objectWriter, entry) =>
        {
            GameObject gameObject = entry.gameObject;
            Transform? parent = gameObject.transform.parent;
            bool parentIsIncluded = parent is not null && included.Contains(parent.gameObject);
            objectWriter.Write(C_OBJECT_ID_KEY, GetSourceId(sourceIds, gameObject));
            objectWriter.Write(C_NAME_KEY, gameObject.name);
            objectWriter.Write(C_TAG_KEY, gameObject.tag);
            objectWriter.Write(C_LAYER_KEY, gameObject.layer.index);
            objectWriter.Write(C_ACTIVE_SELF_KEY, gameObject.activeSelf);
            objectWriter.Write(
                C_PARENT_ID_KEY,
                parentIsIncluded ? GetSourceId(sourceIds, parent!.gameObject) : Guid.Empty);
            objectWriter.Write(
                C_SIBLING_INDEX_KEY,
                parentIsIncluded || preserveRootSiblingOrder ? gameObject.transform.siblingIndex : 0);
            PrefabInstanceInfo? prefab = gameObject.prefabInstance;
            bool hasPrefabConnection = prefab is not null && included.Contains(prefab.instanceRoot);
            objectWriter.Write(C_HAS_PREFAB_CONNECTION_KEY, hasPrefabConnection);
            if (hasPrefabConnection)
            {
                AssetObject sourceAsset = prefab!.sourceAsset;
                objectWriter.Write(C_PREFAB_SOURCE_ASSET_KEY, sourceAsset);
                objectWriter.Write(C_PREFAB_SOURCE_ASSET_ID_KEY, prefab!.sourceAssetId);
                objectWriter.Write(C_PREFAB_SOURCE_OBJECT_ID_KEY, prefab.sourceObjectId);
                objectWriter.Write(C_PREFAB_INSTANCE_ROOT_ID_KEY, GetSourceId(sourceIds, prefab.instanceRoot));
                objectWriter.Write(C_PREFAB_IS_VARIANT_KEY, prefab.isVariant);
                objectWriter.Write(C_PREFAB_IS_MISSING_KEY, prefab.isMissing);
                PrefabOverrideSet? capturedOverrides = null;
                if (prefab.isRoot)
                {
                    PrefabConnectionRecord connection = gameObject.prefabConnection
                        ?? throw new InvalidOperationException(
                            $"Prefab instance root '{gameObject.identity.persistentId}' has no connection mapping.");
                    capturedOverrides = PrefabOverrideProcessor.Capture(
                        connection,
                        gameObject.scene,
                        objectWriter.context,
                        SceneGraphReferenceMap.current);
                    WritePrefabConnection(
                        objectWriter,
                        connection,
                        capturedOverrides,
                        sourceIds,
                        gameObject.scene);
                }
                objectWriter.Write(
                    C_PREFAB_OVERRIDE_COUNT_KEY,
                    capturedOverrides?.count ?? prefab.overrideCount);
                objectWriter.Write(
                    C_PREFAB_ORPHANED_OVERRIDE_COUNT_KEY,
                    capturedOverrides?.orphanedCount ?? prefab.orphanedOverrideCount);
            }
            objectWriter.WriteObjectArray(C_COMPONENTS_KEY, entry.components, (componentWriter, component) =>
            {
                componentWriter.Write(C_COMPONENT_ID_KEY, GetSourceId(sourceIds, component));
                componentWriter.Write(C_STABLE_TYPE_ID_KEY, GetStableComponentTypeId(component.GetType()));
                componentWriter.WriteObject(C_STATE_KEY, stateWriter =>
                {
                    try
                    {
                        stateWriter.WriteProperties(component);
                    }
                    catch (Exception exception)
                    {
                        throw new InvalidOperationException(
                            $"Failed to serialize GameComponent '{component.GetType().FullName}' on " +
                            $"GameObject '{gameObject.name}' at '{stateWriter.path}'. {exception.Message}",
                            exception);
                    }
                });
            });
        });
    }

    internal static RestoredSceneGraph RestoreObjects(
        GameScene scene,
        IReadOnlyList<SerializationReader> objectReaders,
        bool preservePersistentIds,
        SceneGraphReferenceMap references,
        bool restoreProperties = true)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(objectReaders);
        ArgumentNullException.ThrowIfNull(references);
        ValidateObjects(objectReaders);

        var gameObjectBySourceId = new Dictionary<Guid, GameObject>();
        var componentBySourceId = new Dictionary<Guid, GameComponent>();
        var componentRestores = new List<(GameComponent component, SerializationReader state)>();

        for (int objectIndex = 0; objectIndex < objectReaders.Count; objectIndex++)
        {
            SerializationReader objectReader = objectReaders[objectIndex];
            Guid sourceObjectId = objectReader.Read<Guid>(C_OBJECT_ID_KEY);
            IReadOnlyList<SerializationReader> componentReaders = objectReader.ReadObjectArray(C_COMPONENTS_KEY);
            SerializationReader transformReader = componentReaders.Single(IsTransform);
            Guid sourceTransformId = transformReader.Read<Guid>(C_COMPONENT_ID_KEY);
            GameObject gameObject = scene.CreateObject(
                objectReader.Read<string>(C_NAME_KEY),
                preservePersistentIds ? sourceObjectId : null,
                preservePersistentIds ? sourceTransformId : null,
                invokeReset: false);
            gameObject.SetTagDirect(
                objectReader.Read<string>(C_TAG_KEY));
            gameObject.SetLayerDirect(new Layer(objectReader.Read<int>(C_LAYER_KEY)));
            gameObject.SetActiveSelfDirect(objectReader.Read<bool>(C_ACTIVE_SELF_KEY));
            gameObjectBySourceId.Add(sourceObjectId, gameObject);
            componentBySourceId.Add(sourceTransformId, gameObject.transform);
            componentRestores.Add((gameObject.transform, transformReader.ReadObject(C_STATE_KEY)));
        }

        for (int objectIndex = 0; objectIndex < objectReaders.Count; objectIndex++)
        {
            SerializationReader objectReader = objectReaders[objectIndex];
            Guid sourceObjectId = objectReader.Read<Guid>(C_OBJECT_ID_KEY);
            GameObject gameObject = gameObjectBySourceId[sourceObjectId];
            IReadOnlyList<SerializationReader> componentReaders = objectReader.ReadObjectArray(C_COMPONENTS_KEY);
            for (int componentIndex = 0; componentIndex < componentReaders.Count; componentIndex++)
            {
                SerializationReader componentReader = componentReaders[componentIndex];
                Type componentType = ResolveComponentType(componentReader.Read<Guid>(C_STABLE_TYPE_ID_KEY));
                if (componentType == typeof(Transform))
                    continue;

                Guid sourceComponentId = componentReader.Read<Guid>(C_COMPONENT_ID_KEY);
                GameComponent component = scene.AddComponent(
                    gameObject,
                    componentType,
                    preservePersistentIds ? sourceComponentId : null,
                    invokeReset: false);
                componentBySourceId.Add(sourceComponentId, component);
                componentRestores.Add((component, componentReader.ReadObject(C_STATE_KEY)));
            }
        }

        foreach ((Guid sourceId, GameObject gameObject) in gameObjectBySourceId)
            references.Register(sourceId, gameObject);
        foreach ((Guid sourceId, GameComponent component) in componentBySourceId)
            references.Register(sourceId, component);

        if (restoreProperties)
        {
            using (references.Enter())
            {
                for (int i = 0; i < componentRestores.Count; i++)
                    componentRestores[i].state.RestoreProperties(componentRestores[i].component);
            }
        }

        for (int objectIndex = 0; objectIndex < objectReaders.Count; objectIndex++)
        {
            SerializationReader objectReader = objectReaders[objectIndex];
            Guid sourceObjectId = objectReader.Read<Guid>(C_OBJECT_ID_KEY);
            Guid parentId = objectReader.Read<Guid>(C_PARENT_ID_KEY);
            Transform? parent = parentId == Guid.Empty
                ? null
                : gameObjectBySourceId.TryGetValue(parentId, out GameObject? parentObject)
                    ? parentObject.transform
                    : throw new InvalidDataException(
                        $"Parent object '{parentId}' for object '{sourceObjectId}' is outside the restored boundary.");
            scene.SetParent(gameObjectBySourceId[sourceObjectId].transform, parent, worldPositionStays: false);
        }

        foreach (IGrouping<Guid, SerializationReader> siblingGroup in objectReaders.GroupBy(
                     objectReader => objectReader.Read<Guid>(C_PARENT_ID_KEY)))
        {
            foreach (SerializationReader objectReader in siblingGroup.OrderBy(
                         reader => reader.Read<int>(C_SIBLING_INDEX_KEY)))
            {
                scene.SetSiblingIndex(
                    gameObjectBySourceId[objectReader.Read<Guid>(C_OBJECT_ID_KEY)].transform,
                    objectReader.Read<int>(C_SIBLING_INDEX_KEY));
            }
        }

        var connectionByRootId = new Dictionary<Guid, PrefabConnectionRecord>();
        for (int objectIndex = 0; objectIndex < objectReaders.Count; objectIndex++)
        {
            SerializationReader objectReader = objectReaders[objectIndex];
            if (!objectReader.Read<bool>(C_HAS_PREFAB_CONNECTION_KEY))
                continue;
            Guid serializedObjectId = objectReader.Read<Guid>(C_OBJECT_ID_KEY);
            Guid instanceRootId = objectReader.Read<Guid>(C_PREFAB_INSTANCE_ROOT_ID_KEY);
            if (serializedObjectId != instanceRootId)
                continue;
            if (!gameObjectBySourceId.TryGetValue(instanceRootId, out GameObject? instanceRoot))
            {
                throw new InvalidDataException(
                    $"Prefab instance root '{instanceRootId}' at '{objectReader.path}' is outside the restored graph.");
            }
            AssetObject sourceAsset = objectReader.Read<AssetObject>(C_PREFAB_SOURCE_ASSET_KEY);
            Guid sourceAssetId = objectReader.Read<Guid>(C_PREFAB_SOURCE_ASSET_ID_KEY);
            if (sourceAsset.identity.persistentId != sourceAssetId)
            {
                throw new InvalidDataException(
                    $"Prefab source token and source identity disagree at '{objectReader.path}'.");
            }

            PrefabOverrideSet overrides = ReadPrefabOverrides(objectReader);
            var connection = new PrefabConnectionRecord(
                sourceAsset,
                objectReader.Read<Guid>(C_PREFAB_SOURCE_OBJECT_ID_KEY),
                objectReader.Read<bool>(C_PREFAB_IS_VARIANT_KEY),
                overrides);
            foreach (SerializationReader mappingReader in
                     objectReader.ReadObjectArray(C_PREFAB_COMPONENT_MAPPINGS_KEY))
            {
                Guid componentSourceId = mappingReader.Read<Guid>(C_PREFAB_MAPPING_SOURCE_ID_KEY);
                Guid componentInstanceId = mappingReader.Read<Guid>(C_PREFAB_MAPPING_INSTANCE_ID_KEY);
                if (!componentBySourceId.TryGetValue(componentInstanceId, out GameComponent? component))
                {
                    throw new InvalidDataException(
                        $"Prefab component mapping '{componentInstanceId}' at '{mappingReader.path}' does not resolve.");
                }
                connection.MapComponent(componentSourceId, component);
            }
            connectionByRootId.Add(instanceRootId, connection);
            instanceRoot.SetPrefabConnectionDirect(connection);
        }

        for (int objectIndex = 0; objectIndex < objectReaders.Count; objectIndex++)
        {
            SerializationReader objectReader = objectReaders[objectIndex];
            if (!objectReader.Read<bool>(C_HAS_PREFAB_CONNECTION_KEY))
                continue;
            Guid serializedObjectId = objectReader.Read<Guid>(C_OBJECT_ID_KEY);
            Guid instanceRootId = objectReader.Read<Guid>(C_PREFAB_INSTANCE_ROOT_ID_KEY);
            if (!gameObjectBySourceId.TryGetValue(instanceRootId, out GameObject? instanceRoot) ||
                !connectionByRootId.TryGetValue(instanceRootId, out PrefabConnectionRecord? connection))
            {
                throw new InvalidDataException(
                    $"Prefab instance root '{instanceRootId}' at '{objectReader.path}' has no connection record.");
            }
            GameObject gameObject = gameObjectBySourceId[serializedObjectId];
            Guid prefabSourceObjectId = objectReader.Read<Guid>(C_PREFAB_SOURCE_OBJECT_ID_KEY);
            connection.MapObject(prefabSourceObjectId, gameObject);
            gameObject.SetPrefabInstanceDirect(new PrefabInstanceInfo(
                connection.sourceAsset.identity.persistentId,
                prefabSourceObjectId,
                instanceRoot,
                ReferenceEquals(gameObject, instanceRoot),
                connection.isVariant,
                connection.sourceAsset.isMissing,
                connection.overrides.count,
                connection.overrides.orphanedCount,
                connection.sourceAsset));
        }

        foreach (GameObject root in gameObjectBySourceId.Values.Where(
                     static candidate => candidate.transform.parent is null))
        {
            scene.RecomputeActiveSubtree(root);
        }
        return new RestoredSceneGraph(gameObjectBySourceId, componentBySourceId, componentRestores);
    }

    internal static void ValidateScene(SerializationReader reader)
    {
        EnsurePersistentId(reader.Read<Guid>(C_SCENE_ID_KEY), $"{reader.path}.{C_SCENE_ID_KEY}");
        _ = reader.Read<string>(C_NAME_KEY);
        ValidateObjects(reader.ReadObjectArray(C_OBJECTS_KEY));
        ValidateSystems(reader.ReadObjectArray(C_SYSTEMS_KEY));
    }

    internal static void WriteSystems(
        SerializationWriter writer,
        IReadOnlyCollection<GameSystem> systems,
        IReadOnlyDictionary<EngineObject, Guid> sourceIds)
    {
        writer.WriteObjectArray(C_SYSTEMS_KEY, systems, (systemWriter, system) =>
        {
            systemWriter.Write(C_SYSTEM_ID_KEY, GetSourceId(sourceIds, system));
            systemWriter.Write(C_STABLE_TYPE_ID_KEY, GetStableSystemTypeId(system.GetType()));
            systemWriter.WriteObject(C_STATE_KEY, stateWriter => stateWriter.WriteProperties(system));
        });
    }

    internal static IReadOnlyList<(GameSystem system, SerializationReader state)> CreateSystems(
        GameScene scene,
        IReadOnlyList<SerializationReader> systemReaders,
        bool preservePersistentIds,
        SceneGraphReferenceMap references)
    {
        ValidateSystems(systemReaders);
        var result = new List<(GameSystem, SerializationReader)>(systemReaders.Count);
        foreach (SerializationReader systemReader in systemReaders)
        {
            Guid sourceId = systemReader.Read<Guid>(C_SYSTEM_ID_KEY);
            Type systemType = ResolveSystemType(systemReader.Read<Guid>(C_STABLE_TYPE_ID_KEY));
            GameSystem system = scene.AddSystem(
                systemType,
                preservePersistentIds ? sourceId : null,
                invokeReset: false);
            references.Register(sourceId, system);
            result.Add((system, systemReader.ReadObject(C_STATE_KEY)));
        }
        return result;
    }

    private static void ValidateSystems(IReadOnlyList<SerializationReader> systemReaders)
    {
        var identities = new HashSet<Guid>();
        foreach (SerializationReader systemReader in systemReaders)
        {
            Guid systemId = systemReader.Read<Guid>(C_SYSTEM_ID_KEY);
            EnsurePersistentId(systemId, $"{systemReader.path}.{C_SYSTEM_ID_KEY}");
            if (!identities.Add(systemId))
                throw new InvalidDataException($"Duplicate GameSystem identity '{systemId}' at '{systemReader.path}'.");
            _ = ResolveSystemType(systemReader.Read<Guid>(C_STABLE_TYPE_ID_KEY));
            _ = systemReader.ReadObject(C_STATE_KEY);
        }
    }

    internal static void ReconcilePrefabConnections(
        GameScene scene,
        SerializationContext context,
        GameObject? excludedRoot = null)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(context);
        GameObject[] roots = scene.GetObjects()
            .Where(gameObject =>
                gameObject.prefabInstance?.isRoot == true &&
                gameObject.prefabConnection is not null &&
                !ReferenceEquals(gameObject, excludedRoot))
            .ToArray();
        for (int i = 0; i < roots.Length; i++)
        {
            if (!roots[i].isRuntimeValid || roots[i].prefabConnection is null)
                continue;
            PrefabOverrideProcessor.Reconcile(roots[i].prefabConnection!, roots[i], context);
        }
    }

    internal static void ValidateObjects(IReadOnlyList<SerializationReader> objectReaders)
    {
        var objectIds = new HashSet<Guid>();
        var componentIds = new HashSet<Guid>();
        for (int objectIndex = 0; objectIndex < objectReaders.Count; objectIndex++)
        {
            SerializationReader objectReader = objectReaders[objectIndex];
            Guid objectId = objectReader.Read<Guid>(C_OBJECT_ID_KEY);
            EnsurePersistentId(objectId, $"{objectReader.path}.{C_OBJECT_ID_KEY}");
            if (!objectIds.Add(objectId))
                throw new InvalidDataException($"Duplicate GameObject local identity '{objectId}' at '{objectReader.path}'.");
            _ = objectReader.Read<string>(C_NAME_KEY);
            if (objectReader.Contains(C_TAG_KEY))
                _ = GameObject.NormalizeTag(objectReader.Read<string>(C_TAG_KEY));
            _ = objectReader.Read<bool>(C_ACTIVE_SELF_KEY);
            _ = objectReader.Read<Guid>(C_PARENT_ID_KEY);
            _ = objectReader.Read<int>(C_SIBLING_INDEX_KEY);
            bool hasPrefabConnection = objectReader.Read<bool>(C_HAS_PREFAB_CONNECTION_KEY);
            if (hasPrefabConnection)
            {
                if (!objectReader.Contains(C_PREFAB_SOURCE_ASSET_KEY))
                {
                    throw new InvalidDataException(
                        $"Prefab source token is missing at '{objectReader.path}'.");
                }
                EnsurePersistentId(
                    objectReader.Read<Guid>(C_PREFAB_SOURCE_ASSET_ID_KEY),
                    $"{objectReader.path}.{C_PREFAB_SOURCE_ASSET_ID_KEY}");
                EnsurePersistentId(
                    objectReader.Read<Guid>(C_PREFAB_SOURCE_OBJECT_ID_KEY),
                    $"{objectReader.path}.{C_PREFAB_SOURCE_OBJECT_ID_KEY}");
                EnsurePersistentId(
                    objectReader.Read<Guid>(C_PREFAB_INSTANCE_ROOT_ID_KEY),
                    $"{objectReader.path}.{C_PREFAB_INSTANCE_ROOT_ID_KEY}");
                _ = objectReader.Read<bool>(C_PREFAB_IS_VARIANT_KEY);
                _ = objectReader.Read<bool>(C_PREFAB_IS_MISSING_KEY);
                _ = objectReader.Read<int>(C_PREFAB_OVERRIDE_COUNT_KEY);
                _ = objectReader.Read<int>(C_PREFAB_ORPHANED_OVERRIDE_COUNT_KEY);
                if (objectId == objectReader.Read<Guid>(C_PREFAB_INSTANCE_ROOT_ID_KEY))
                {
                    _ = objectReader.ReadObjectArray(C_PREFAB_COMPONENT_MAPPINGS_KEY);
                    _ = objectReader.ReadObjectArray(C_PREFAB_PROPERTY_OVERRIDES_KEY);
                    _ = objectReader.ReadObjectArray(C_PREFAB_STRUCTURE_OVERRIDES_KEY);
                    _ = objectReader.Read<Guid[]>(C_PREFAB_REMOVED_OBJECTS_KEY);
                    _ = objectReader.Read<Guid[]>(C_PREFAB_REMOVED_COMPONENTS_KEY);
                    _ = objectReader.Read<Guid[]>(C_PREFAB_ADDED_OBJECTS_KEY);
                    _ = objectReader.Read<Guid[]>(C_PREFAB_ADDED_COMPONENTS_KEY);
                }
            }

            IReadOnlyList<SerializationReader> componentReaders = objectReader.ReadObjectArray(C_COMPONENTS_KEY);
            int transformCount = 0;
            for (int componentIndex = 0; componentIndex < componentReaders.Count; componentIndex++)
            {
                SerializationReader componentReader = componentReaders[componentIndex];
                Guid componentId = componentReader.Read<Guid>(C_COMPONENT_ID_KEY);
                EnsurePersistentId(componentId, $"{componentReader.path}.{C_COMPONENT_ID_KEY}");
                if (!componentIds.Add(componentId))
                    throw new InvalidDataException($"Duplicate GameComponent local identity '{componentId}' at '{componentReader.path}'.");
                if (ResolveComponentType(componentReader.Read<Guid>(C_STABLE_TYPE_ID_KEY)) == typeof(Transform))
                    transformCount++;
                _ = componentReader.ReadObject(C_STATE_KEY);
            }

            if (transformCount != 1)
            {
                throw new InvalidDataException(
                    $"Scene graph object '{objectId}' must contain exactly one Transform, but found '{transformCount}'.");
            }
        }

        for (int objectIndex = 0; objectIndex < objectReaders.Count; objectIndex++)
        {
            Guid parentId = objectReaders[objectIndex].Read<Guid>(C_PARENT_ID_KEY);
            if (parentId != Guid.Empty && !objectIds.Contains(parentId))
            {
                throw new InvalidDataException(
                    $"Parent object '{parentId}' at '{objectReaders[objectIndex].path}' is outside the serialized boundary.");
            }
        }
    }

    private static Guid GetSourceId(
        IReadOnlyDictionary<EngineObject, Guid> sourceIds,
        EngineObject engineObject)
        => sourceIds.TryGetValue(engineObject, out Guid sourceId)
            ? sourceId
            : throw new InvalidOperationException(
                $"Engine object '{engineObject.identity.persistentId}' has no source-local identity.");

    private static void WritePrefabConnection(
        SerializationWriter writer,
        PrefabConnectionRecord connection,
        PrefabOverrideSet overrides,
        IReadOnlyDictionary<EngineObject, Guid> serializedIds,
        GameScene scene)
    {
        KeyValuePair<Guid, Guid>[] liveComponentMappings = connection.componentIdentities
            .Where(pair => scene.FindComponent(pair.Value) is not null)
            .ToArray();
        writer.WriteObjectArray(
            C_PREFAB_COMPONENT_MAPPINGS_KEY,
            liveComponentMappings,
            (mappingWriter, pair) =>
            {
                GameComponent component = scene.FindComponent(pair.Value)!;
                mappingWriter.Write(C_PREFAB_MAPPING_SOURCE_ID_KEY, pair.Key);
                mappingWriter.Write(C_PREFAB_MAPPING_INSTANCE_ID_KEY, GetSourceId(serializedIds, component));
            });
        writer.WriteObjectArray(
            C_PREFAB_PROPERTY_OVERRIDES_KEY,
            overrides.properties,
            static (overrideWriter, property) =>
            {
                overrideWriter.Write(C_PREFAB_MAPPING_SOURCE_ID_KEY, property.sourceComponentId);
                overrideWriter.Write(C_PREFAB_PROPERTY_NAME_KEY, property.propertyName);
                overrideWriter.Write(C_PREFAB_PROPERTY_VALUE_KEY, property.value);
                overrideWriter.Write(C_PREFAB_OVERRIDE_ORPHANED_KEY, property.isOrphaned);
            });
        writer.WriteObjectArray(
            C_PREFAB_STRUCTURE_OVERRIDES_KEY,
            overrides.structures,
            static (overrideWriter, structure) =>
            {
                overrideWriter.Write(C_PREFAB_MAPPING_SOURCE_ID_KEY, structure.sourceObjectId);
                overrideWriter.Write(C_PREFAB_STRUCTURE_KIND_KEY, (int)structure.kind);
                overrideWriter.Write(C_PREFAB_OVERRIDE_ORPHANED_KEY, structure.isOrphaned);
            });
        writer.Write(C_PREFAB_REMOVED_OBJECTS_KEY, overrides.removedObjects.ToArray());
        writer.Write(C_PREFAB_REMOVED_COMPONENTS_KEY, overrides.removedComponents.ToArray());
        writer.Write(C_PREFAB_ADDED_OBJECTS_KEY, overrides.addedObjects.ToArray());
        writer.Write(C_PREFAB_ADDED_COMPONENTS_KEY, overrides.addedComponents.ToArray());
    }

    private static PrefabOverrideSet ReadPrefabOverrides(SerializationReader reader)
    {
        var result = new PrefabOverrideSet();
        foreach (SerializationReader propertyReader in
                 reader.ReadObjectArray(C_PREFAB_PROPERTY_OVERRIDES_KEY))
        {
            result.SetProperty(new PrefabPropertyOverride(
                propertyReader.Read<Guid>(C_PREFAB_MAPPING_SOURCE_ID_KEY),
                propertyReader.Read<string>(C_PREFAB_PROPERTY_NAME_KEY),
                propertyReader.Read<byte[]>(C_PREFAB_PROPERTY_VALUE_KEY),
                propertyReader.Read<bool>(C_PREFAB_OVERRIDE_ORPHANED_KEY)));
        }
        foreach (SerializationReader structureReader in
                 reader.ReadObjectArray(C_PREFAB_STRUCTURE_OVERRIDES_KEY))
        {
            result.SetStructure(
                structureReader.Read<Guid>(C_PREFAB_MAPPING_SOURCE_ID_KEY),
                (PrefabObjectOverrideKind)structureReader.Read<int>(C_PREFAB_STRUCTURE_KIND_KEY),
                structureReader.Read<bool>(C_PREFAB_OVERRIDE_ORPHANED_KEY));
        }
        foreach (Guid sourceId in reader.Read<Guid[]>(C_PREFAB_REMOVED_OBJECTS_KEY))
            result.MarkObjectRemoved(sourceId);
        foreach (Guid sourceId in reader.Read<Guid[]>(C_PREFAB_REMOVED_COMPONENTS_KEY))
            result.MarkComponentRemoved(sourceId);
        foreach (Guid instanceId in reader.Read<Guid[]>(C_PREFAB_ADDED_OBJECTS_KEY))
            result.MarkObjectAdded(instanceId);
        foreach (Guid instanceId in reader.Read<Guid[]>(C_PREFAB_ADDED_COMPONENTS_KEY))
            result.MarkComponentAdded(instanceId);
        return result;
    }

    private static bool IsTransform(SerializationReader reader)
        => ResolveComponentType(reader.Read<Guid>(C_STABLE_TYPE_ID_KEY)) == typeof(Transform);

    private static Guid GetStableComponentTypeId(Type componentType)
    {
        if (!TypeCacheManager.TryGetStableTypeId(componentType, out Guid stableTypeId))
        {
            throw new InvalidOperationException(
                $"GameComponent type '{componentType.FullName}' requires a loaded StableTypeId before persistence.");
        }
        return stableTypeId;
    }

    private static Type ResolveComponentType(Guid stableTypeId)
    {
        if (!TypeCacheManager.TryResolveType(stableTypeId, out Type? componentType) || componentType is null)
            throw new SceneTypeResolutionException(stableTypeId, "component");
        if (!typeof(GameComponent).IsAssignableFrom(componentType) || componentType.IsAbstract)
        {
            throw new InvalidDataException(
                $"Stable type id '{stableTypeId}' resolves to invalid component type '{componentType.FullName}'.");
        }
        return componentType;
    }

    private static Guid GetStableSystemTypeId(Type systemType)
    {
        if (!TypeCacheManager.TryGetStableTypeId(systemType, out Guid stableTypeId))
            throw new InvalidOperationException($"GameSystem type '{systemType.FullName}' requires a loaded StableTypeId.");
        return stableTypeId;
    }

    private static Type ResolveSystemType(Guid stableTypeId)
    {
        if (!TypeCacheManager.TryResolveType(stableTypeId, out Type? systemType) || systemType is null)
            throw new SceneTypeResolutionException(stableTypeId, "system");
        if (!typeof(GameSystem).IsAssignableFrom(systemType) || systemType.IsAbstract)
            throw new InvalidDataException($"Stable type id '{stableTypeId}' resolves to invalid system '{systemType.FullName}'.");
        return systemType;
    }

    private static void EnsurePersistentId(Guid persistentId, string path)
    {
        if (persistentId == Guid.Empty)
            throw new InvalidDataException($"Persistent or local identity at '{path}' cannot be empty.");
    }
}
