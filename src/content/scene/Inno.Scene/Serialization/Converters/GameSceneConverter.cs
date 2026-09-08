using System;
using System.Collections.Generic;
using System.Linq;

using Inno.Assets;
using Inno.Core.Serialization;
using Inno.Core.Serialization.Converters;
using Inno.Scene;

namespace Inno.Scene;

[SerializationExtension]
internal sealed class GameSceneConverter : SerializationConverter<GameScene>
{
    /// <summary>
    /// Writes the supplied value through the owning subsystem's validated output boundary.
    /// </summary>
    /// <param name="writer">
    /// The writer that receives the deterministic structured representation.
    /// </param>
    /// <param name="value">
    /// The concrete value read or transformed by this operation.
    /// </param>
    public override void Write(SerializationWriter writer, GameScene value)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(value);
        if (value.isDestroyed)
            throw new InvalidOperationException("A destroyed scene cannot be serialized.");

        SceneStructureSnapshot snapshot = value.CaptureStructure();
        IReadOnlyList<GameSystem> systems = value.GetSystems();
        EngineObject[] engineObjects = snapshot.objects
            .SelectMany(static entry => entry.components.Cast<EngineObject>().Prepend(entry.gameObject))
            .Concat(systems)
            .ToArray();
        var sourceIds = new Dictionary<EngineObject, Guid>(ReferenceEqualityComparer.Instance);
        for (int i = 0; i < engineObjects.Length; i++)
            sourceIds.Add(engineObjects[i], engineObjects[i].identity.persistentId);
        var references = new SceneGraphReferenceMap(value, engineObjects, sourceIds);

        writer.Write(SceneGraphSerialization.C_SCENE_ID_KEY, value.identity.persistentId);
        writer.Write(SceneGraphSerialization.C_NAME_KEY, value.name);
        using (references.Enter())
        {
            SceneGraphSerialization.WriteObjects(
                writer,
                snapshot.objects,
                sourceIds,
                preserveRootSiblingOrder: true);
            SceneGraphSerialization.WriteSystems(writer, systems, sourceIds);
        }
    }

    /// <summary>
    /// Reads and validates the requested value without transferring storage ownership.
    /// </summary>
    /// <param name="reader">
    /// The reader positioned at the structured value to decode.
    /// </param>
    /// <returns>
    /// The validated game scene that represents the completed operation.
    /// </returns>
    public override GameScene Read(SerializationReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        SceneGraphSerialization.ValidateScene(reader);
        Guid sceneId = reader.Read<Guid>(SceneGraphSerialization.C_SCENE_ID_KEY);
        bool instantiateFromAsset = reader.context.TryGet(out AssetObject? sourceAsset) && sourceAsset is not null;
        var scene = new GameScene(
            SceneWorld.current.typeCatalog,
            reader.Read<string>(SceneGraphSerialization.C_NAME_KEY),
            instantiateFromAsset ? null : sceneId);
        if (sourceAsset is not null)
            scene.SetSourceAsset(sourceAsset);
        try
        {
            RestoreSceneGraph(scene, reader, preservePersistentIds: !instantiateFromAsset);
            SceneGraphSerialization.ReconcilePrefabConnections(scene, reader.context);
            return scene;
        }
        catch
        {
            if (!scene.isDestroyed)
                scene.Unload();
            throw;
        }
    }

    /// <summary>
    /// Restores the supplied snapshot while preserving current invariants.
    /// </summary>
    /// <param name="reader">
    /// The reader positioned at the structured value to decode.
    /// </param>
    /// <param name="target">
    /// The existing target that receives the validated result.
    /// </param>
    public override void Restore(SerializationReader reader, GameScene target)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(target);
        SceneGraphSerialization.ValidateScene(reader);
        Guid sceneId = reader.Read<Guid>(SceneGraphSerialization.C_SCENE_ID_KEY);
        target.PrepareRestoreTarget(sceneId);
        target.name = reader.Read<string>(SceneGraphSerialization.C_NAME_KEY);
        try
        {
            RestoreSceneGraph(target, reader, preservePersistentIds: true);
            SceneGraphSerialization.ReconcilePrefabConnections(target, reader.context);
        }
        catch
        {
            foreach (GameSystem system in target.GetSystems().ToArray())
                target.RemoveSystem(system);
            GameObject[] createdObjects = target.GetObjects().ToArray();
            for (int i = 0; i < createdObjects.Length; i++)
            {
                if (createdObjects[i].isRuntimeValid)
                    target.DestroyObject(createdObjects[i]);
            }
            throw;
        }
    }

    private static void RestoreSceneGraph(
        GameScene scene,
        SerializationReader reader,
        bool preservePersistentIds)
    {
        var references = new SceneGraphReferenceMap(scene);
        RestoredSceneGraph graph = SceneGraphSerialization.RestoreObjects(
            scene,
            reader.ReadObjectArray(SceneGraphSerialization.C_OBJECTS_KEY),
            preservePersistentIds,
            references,
            SceneGraphSerialization.ReadMissingReferenceAliases(reader),
            reader.context,
            restoreProperties: false);
        IReadOnlyList<SerializationReader> systemReaders =
            reader.ReadObjectArray(SceneGraphSerialization.C_SYSTEMS_KEY);
        var missingPlaceholders = graph.missingPlaceholders.ToList();
        IReadOnlyList<(GameSystem system, byte[] state)> systemStates =
            SceneGraphSerialization.CreateSystems(
                scene,
                systemReaders,
                preservePersistentIds,
                references,
                missingPlaceholders);
        SceneGraphSerialization.RestoreMissingReferenceAliases(
            graph with { missingPlaceholders = missingPlaceholders },
            references);
        using (references.Enter())
        {
            SerializationRegistry serialization = reader.context.GetRequired<SerializationRegistry>();
            foreach ((GameComponent component, byte[] state) in graph.componentStates)
                serialization.RestorePropertiesData(component, state, context: reader.context);
            foreach ((GameSystem system, byte[] state) in systemStates)
                serialization.RestorePropertiesData(system, state, context: reader.context);
        }
    }
}
