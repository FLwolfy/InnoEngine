using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Inno.Assets;
using Inno.Assets.Core;
using Inno.Assets.Serialization;
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
    internal const string C_TYPE_NAME_KEY = "typeName";
    internal const string C_STATE_KEY = "state";
    internal const string C_STATE_DEPENDENCIES_KEY = "stateDependencies";
    internal const string C_STATE_DEPENDENCY_ASSETS_KEY = "stateDependencyAssets";
    internal const string C_MISSING_REFERENCE_SOURCE_IDS_KEY = "missingReferenceSourceIds";
    internal const string C_MISSING_REFERENCE_TARGET_IDS_KEY = "missingReferenceTargetIds";
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
        WriteMissingReferenceAliases(writer, sourceIds);
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
                if (component is MissingGameComponent missing)
                {
                    componentWriter.Write(C_STABLE_TYPE_ID_KEY, missing.missingTypeId);
                    componentWriter.Write(C_TYPE_NAME_KEY, missing.missingTypeName);
                    componentWriter.Write(C_STATE_KEY, missing.CaptureSerializedState());
                    WriteStateDependencies(componentWriter, missing.dependencies);
                    return;
                }
                componentWriter.Write(C_STABLE_TYPE_ID_KEY, GetStableComponentTypeId(component.GetType()));
                componentWriter.Write(C_TYPE_NAME_KEY, component.GetType().FullName ?? component.GetType().Name);
                try
                {
                    CapturedSceneState state = CaptureState(component, componentWriter.context);
                    componentWriter.Write(C_STATE_KEY, state.data);
                    WriteStateDependencies(componentWriter, state.dependencies);
                }
                catch (Exception exception)
                {
                    throw new InvalidOperationException(
                        $"Failed to serialize GameComponent '{component.GetType().FullName}' on " +
                        $"GameObject '{gameObject.name}' at '{componentWriter.path}'. {exception.Message}",
                        exception);
                }
            });
        });
    }

    internal static RestoredSceneGraph RestoreObjects(
        GameScene scene,
        IReadOnlyList<SerializationReader> objectReaders,
        bool preservePersistentIds,
        SceneGraphReferenceMap references,
        IReadOnlyList<KeyValuePair<Guid, Guid>> missingReferenceAliases,
        SerializationContext context,
        bool restoreProperties = true)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(objectReaders);
        ArgumentNullException.ThrowIfNull(references);
        ValidateObjects(objectReaders);

        var gameObjectBySourceId = new Dictionary<Guid, GameObject>();
        var componentBySourceId = new Dictionary<Guid, GameComponent>();
        var componentRestores = new List<(GameComponent component, byte[] state)>();
        var missingPlaceholders = new List<EngineObject>();

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
            gameObject.SetLayerDirect(new GameLayer(objectReader.Read<int>(C_LAYER_KEY)));
            gameObject.SetActiveSelfDirect(objectReader.Read<bool>(C_ACTIVE_SELF_KEY));
            gameObjectBySourceId.Add(sourceObjectId, gameObject);
            componentBySourceId.Add(sourceTransformId, gameObject.transform);
            componentRestores.Add((gameObject.transform, transformReader.Read<byte[]>(C_STATE_KEY)));
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
                Guid stableTypeId = componentReader.Read<Guid>(C_STABLE_TYPE_ID_KEY);
                if (TryResolveComponentType(stableTypeId, out Type? componentType) &&
                    componentType == typeof(Transform))
                    continue;

                Guid sourceComponentId = componentReader.Read<Guid>(C_COMPONENT_ID_KEY);
                byte[] state = componentReader.Read<byte[]>(C_STATE_KEY);
                AssetDependency[] dependencies = ReadStateDependencies(componentReader);
                GameComponent component;
                if (componentType is null)
                {
                    component = scene.AddMissingComponent(
                        gameObject,
                        stableTypeId,
                        componentReader.Read<string>(C_TYPE_NAME_KEY),
                        state,
                        preservePersistentIds ? sourceComponentId : null,
                        dependencies);
                    missingPlaceholders.Add(component);
                }
                else
                {
                    component = scene.AddComponent(
                        gameObject,
                        componentType,
                        preservePersistentIds ? sourceComponentId : null,
                        invokeReset: false);
                    componentRestores.Add((component, state));
                }
                componentBySourceId.Add(sourceComponentId, component);
            }
        }

        foreach ((Guid sourceId, GameObject gameObject) in gameObjectBySourceId)
            references.Register(sourceId, gameObject);
        foreach ((Guid sourceId, GameComponent component) in componentBySourceId)
            references.Register(sourceId, component);

        if (restoreProperties)
        {
            RestoreMissingReferenceAliases(missingReferenceAliases, references, missingPlaceholders);
            using (references.Enter())
            {
                for (int i = 0; i < componentRestores.Count; i++)
                {
                    SerializationManager.RestorePropertiesData(
                        componentRestores[i].component,
                        componentRestores[i].state,
                        context: context);
                }
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
        return new RestoredSceneGraph(
            gameObjectBySourceId,
            componentBySourceId,
            componentRestores,
            missingPlaceholders,
            missingReferenceAliases);
    }

    internal static void ValidateScene(SerializationReader reader)
    {
        EnsurePersistentId(reader.Read<Guid>(C_SCENE_ID_KEY), $"{reader.path}.{C_SCENE_ID_KEY}");
        _ = reader.Read<string>(C_NAME_KEY);
        ValidateMissingReferenceAliases(reader);
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
            if (system is MissingGameSystem missing)
            {
                systemWriter.Write(C_STABLE_TYPE_ID_KEY, missing.missingTypeId);
                systemWriter.Write(C_TYPE_NAME_KEY, missing.missingTypeName);
                systemWriter.Write(C_STATE_KEY, missing.CaptureSerializedState());
                WriteStateDependencies(systemWriter, missing.dependencies);
                return;
            }
            systemWriter.Write(C_STABLE_TYPE_ID_KEY, GetStableSystemTypeId(system.GetType()));
            systemWriter.Write(C_TYPE_NAME_KEY, system.GetType().FullName ?? system.GetType().Name);
            CapturedSceneState state = CaptureState(system, systemWriter.context);
            systemWriter.Write(C_STATE_KEY, state.data);
            WriteStateDependencies(systemWriter, state.dependencies);
        });
    }

    internal static IReadOnlyList<(GameSystem system, byte[] state)> CreateSystems(
        GameScene scene,
        IReadOnlyList<SerializationReader> systemReaders,
        bool preservePersistentIds,
        SceneGraphReferenceMap references,
        ICollection<EngineObject>? missingPlaceholders = null)
    {
        ValidateSystems(systemReaders);
        var result = new List<(GameSystem, byte[])>(systemReaders.Count);
        foreach (SerializationReader systemReader in systemReaders)
        {
            Guid sourceId = systemReader.Read<Guid>(C_SYSTEM_ID_KEY);
            Guid stableTypeId = systemReader.Read<Guid>(C_STABLE_TYPE_ID_KEY);
            byte[] state = systemReader.Read<byte[]>(C_STATE_KEY);
            AssetDependency[] dependencies = ReadStateDependencies(systemReader);
            GameSystem system;
            if (!TryResolveSystemType(stableTypeId, out Type? systemType) || systemType is null)
            {
                system = scene.AddMissingSystem(
                    stableTypeId,
                    systemReader.Read<string>(C_TYPE_NAME_KEY),
                    state,
                    preservePersistentIds ? sourceId : null,
                    dependencies);
                missingPlaceholders?.Add(system);
            }
            else
            {
                system = scene.AddSystem(
                    systemType,
                    preservePersistentIds ? sourceId : null,
                    invokeReset: false);
                result.Add((system, state));
            }
            references.Register(sourceId, system);
        }
        return result;
    }

    internal static void RestoreMissingReferenceAliases(
        RestoredSceneGraph graph,
        SceneGraphReferenceMap references)
        => RestoreMissingReferenceAliases(graph.missingReferenceAliases, references, graph.missingPlaceholders);

    private static void ValidateSystems(IReadOnlyList<SerializationReader> systemReaders)
    {
        var identities = new HashSet<Guid>();
        foreach (SerializationReader systemReader in systemReaders)
        {
            Guid systemId = systemReader.Read<Guid>(C_SYSTEM_ID_KEY);
            EnsurePersistentId(systemId, $"{systemReader.path}.{C_SYSTEM_ID_KEY}");
            if (!identities.Add(systemId))
                throw new InvalidDataException($"Duplicate GameSystem identity '{systemId}' at '{systemReader.path}'.");
            Guid stableTypeId = systemReader.Read<Guid>(C_STABLE_TYPE_ID_KEY);
            EnsurePersistentId(stableTypeId, $"{systemReader.path}.{C_STABLE_TYPE_ID_KEY}");
            _ = TryResolveSystemType(stableTypeId, out _);
            EnsureTypeName(systemReader.Read<string>(C_TYPE_NAME_KEY), systemReader.path);
            _ = systemReader.Read<byte[]>(C_STATE_KEY);
            _ = ReadStateDependencies(systemReader);
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
                Guid stableTypeId = componentReader.Read<Guid>(C_STABLE_TYPE_ID_KEY);
                EnsurePersistentId(stableTypeId, $"{componentReader.path}.{C_STABLE_TYPE_ID_KEY}");
                if (TryResolveComponentType(stableTypeId, out Type? componentType) &&
                    componentType == typeof(Transform))
                    transformCount++;
                EnsureTypeName(componentReader.Read<string>(C_TYPE_NAME_KEY), componentReader.path);
                _ = componentReader.Read<byte[]>(C_STATE_KEY);
                _ = ReadStateDependencies(componentReader);
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

    private static void WriteMissingReferenceAliases(
        SerializationWriter writer,
        IReadOnlyDictionary<EngineObject, Guid> sourceIds)
    {
        EngineObject[] missing = sourceIds.Keys
            .Where(static engineObject => engineObject is MissingGameComponent or MissingGameSystem)
            .ToArray();
        if (missing.Length == 0)
        {
            writer.Write(C_MISSING_REFERENCE_SOURCE_IDS_KEY, Array.Empty<Guid>());
            writer.Write(C_MISSING_REFERENCE_TARGET_IDS_KEY, Array.Empty<Guid>());
            return;
        }

        var targetByPersistentId = sourceIds.Keys.ToDictionary(
            static engineObject => engineObject.identity.persistentId,
            engineObject => sourceIds[engineObject]);
        var aliases = new Dictionary<Guid, Guid>();
        foreach ((Guid persistentId, Guid sourceId) in targetByPersistentId)
            aliases[persistentId] = sourceId;
        for (int i = 0; i < missing.Length; i++)
        {
            IReadOnlyDictionary<Guid, Guid> retained = missing[i] switch
            {
                MissingGameComponent component => component.referenceAliases,
                MissingGameSystem system => system.referenceAliases,
                _ => throw new InvalidOperationException("Unknown missing scene element.")
            };
            foreach ((Guid alias, Guid runtimePersistentId) in retained)
            {
                if (targetByPersistentId.TryGetValue(runtimePersistentId, out Guid targetSourceId))
                    aliases[alias] = targetSourceId;
            }
        }
        KeyValuePair<Guid, Guid>[] ordered = aliases
            .OrderBy(static pair => pair.Key)
            .ToArray();
        writer.Write(C_MISSING_REFERENCE_SOURCE_IDS_KEY, ordered.Select(static pair => pair.Key).ToArray());
        writer.Write(C_MISSING_REFERENCE_TARGET_IDS_KEY, ordered.Select(static pair => pair.Value).ToArray());
    }

    internal static IReadOnlyList<KeyValuePair<Guid, Guid>> ReadMissingReferenceAliases(
        SerializationReader rootReader)
    {
        Guid[] aliases = rootReader.Read<Guid[]>(C_MISSING_REFERENCE_SOURCE_IDS_KEY);
        Guid[] targets = rootReader.Read<Guid[]>(C_MISSING_REFERENCE_TARGET_IDS_KEY);
        var result = new KeyValuePair<Guid, Guid>[aliases.Length];
        for (int i = 0; i < aliases.Length; i++)
            result[i] = new KeyValuePair<Guid, Guid>(aliases[i], targets[i]);
        return result;
    }

    private static void RestoreMissingReferenceAliases(
        IReadOnlyList<KeyValuePair<Guid, Guid>> aliases,
        SceneGraphReferenceMap references,
        IReadOnlyList<EngineObject> missingPlaceholders)
    {
        var retained = new Dictionary<Guid, Guid>();
        foreach ((Guid alias, Guid targetSourceId) in aliases)
        {
            EngineObject target = references.GetRegistered(targetSourceId);
            references.Register(alias, target);
            retained[alias] = target.identity.persistentId;
        }
        for (int i = 0; i < missingPlaceholders.Count; i++)
        {
            if (missingPlaceholders[i] is MissingGameComponent component)
                component.SetReferenceAliases(retained);
            else if (missingPlaceholders[i] is MissingGameSystem system)
                system.SetReferenceAliases(retained);
        }
    }

    internal static void ValidateMissingReferenceAliases(SerializationReader reader)
    {
        Guid[] aliases = reader.Read<Guid[]>(C_MISSING_REFERENCE_SOURCE_IDS_KEY);
        Guid[] targets = reader.Read<Guid[]>(C_MISSING_REFERENCE_TARGET_IDS_KEY);
        if (aliases.Length != targets.Length)
            throw new InvalidDataException($"Missing scene reference alias arrays disagree at '{reader.path}'.");
        if (aliases.Any(static value => value == Guid.Empty) || targets.Any(static value => value == Guid.Empty))
            throw new InvalidDataException($"Missing scene reference aliases contain an empty identity at '{reader.path}'.");
        if (aliases.Distinct().Count() != aliases.Length)
            throw new InvalidDataException($"Missing scene reference aliases contain duplicates at '{reader.path}'.");
    }

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
        => TryResolveComponentType(reader.Read<Guid>(C_STABLE_TYPE_ID_KEY), out Type? type) &&
           type == typeof(Transform);

    private static Guid GetStableComponentTypeId(Type componentType)
    {
        if (!TypeCacheManager.TryGetStableTypeId(componentType, out Guid stableTypeId))
        {
            throw new InvalidOperationException(
                $"GameComponent type '{componentType.FullName}' requires a loaded StableTypeId before persistence.");
        }
        return stableTypeId;
    }

    private static bool TryResolveComponentType(Guid stableTypeId, out Type? componentType)
    {
        if (!TypeCacheManager.TryResolveType(stableTypeId, out componentType) || componentType is null)
            return false;
        if (!typeof(GameComponent).IsAssignableFrom(componentType) || componentType.IsAbstract)
        {
            throw new InvalidDataException(
                $"Stable type id '{stableTypeId}' resolves to invalid component type '{componentType.FullName}'.");
        }
        return true;
    }

    private static Guid GetStableSystemTypeId(Type systemType)
    {
        if (!TypeCacheManager.TryGetStableTypeId(systemType, out Guid stableTypeId))
            throw new InvalidOperationException($"GameSystem type '{systemType.FullName}' requires a loaded StableTypeId.");
        return stableTypeId;
    }

    private static bool TryResolveSystemType(Guid stableTypeId, out Type? systemType)
    {
        if (!TypeCacheManager.TryResolveType(stableTypeId, out systemType) || systemType is null)
            return false;
        if (!typeof(GameSystem).IsAssignableFrom(systemType) || systemType.IsAbstract)
            throw new InvalidDataException($"Stable type id '{stableTypeId}' resolves to invalid system '{systemType.FullName}'.");
        return true;
    }

    private static CapturedSceneState CaptureState(
        ISerializable value,
        SerializationContext outerContext)
    {
        var dependencies = new AssetDependencyCollection();
        byte[] data = SerializationManager.CapturePropertiesData(
            value,
            outerContext.With(dependencies));
        return new CapturedSceneState(data, dependencies.dependencies.ToArray());
    }

    private static void WriteStateDependencies(
        SerializationWriter writer,
        IReadOnlyList<AssetDependency> dependencies)
    {
        writer.Write(C_STATE_DEPENDENCIES_KEY, dependencies.ToArray());
        AssetObject[] assets = dependencies
            .Select(static dependency => AssetManager.Load<AssetObject>(dependency.persistentId))
            .ToArray();
        writer.Write(C_STATE_DEPENDENCY_ASSETS_KEY, assets);
    }

    private static AssetDependency[] ReadStateDependencies(SerializationReader reader)
    {
        AssetDependency[] dependencies = reader.Read<AssetDependency[]>(C_STATE_DEPENDENCIES_KEY);
        AssetObject[] assets = reader.Read<AssetObject[]>(C_STATE_DEPENDENCY_ASSETS_KEY);
        if (dependencies.Length != assets.Length)
        {
            throw new InvalidDataException(
                $"Scene state dependency descriptors and assets disagree at '{reader.path}'.");
        }
        for (int i = 0; i < dependencies.Length; i++)
        {
            if (dependencies[i].persistentId != assets[i].identity.persistentId)
            {
                throw new InvalidDataException(
                    $"Scene state dependency '{dependencies[i].persistentId}' does not match its asset token at '{reader.path}'.");
            }
        }
        return dependencies;
    }

    private static void EnsurePersistentId(Guid persistentId, string path)
    {
        if (persistentId == Guid.Empty)
            throw new InvalidDataException($"Persistent or local identity at '{path}' cannot be empty.");
    }

    private static void EnsureTypeName(string typeName, string path)
    {
        if (string.IsNullOrWhiteSpace(typeName))
            throw new InvalidDataException($"Scene element type name at '{path}' cannot be empty.");
    }

    private readonly record struct CapturedSceneState(
        byte[] data,
        AssetDependency[] dependencies);
}
