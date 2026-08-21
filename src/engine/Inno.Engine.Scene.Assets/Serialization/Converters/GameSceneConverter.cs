using System;
using System.Collections.Generic;
using System.Linq;

using Inno.Assets.Core;
using Inno.Core.Serialization;
using Inno.Core.Serialization.Converters;
using Inno.Engine.Scene;

namespace Inno.Engine.Scene.Assets;

[SerializationExtension]
internal sealed class GameSceneConverter : SerializationConverter<GameScene>
{
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

    public override GameScene Read(SerializationReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        SceneGraphSerialization.ValidateScene(reader);
        Guid sceneId = reader.Read<Guid>(SceneGraphSerialization.C_SCENE_ID_KEY);
        bool instantiateFromAsset = reader.context.TryGet(out AssetObject? sourceAsset) && sourceAsset is not null;
        var scene = new GameScene(
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
            restoreProperties: false);
        IReadOnlyList<SerializationReader> systemReaders =
            reader.ReadObjectArray(SceneGraphSerialization.C_SYSTEMS_KEY);
        IReadOnlyList<(GameSystem system, SerializationReader state)> systemStates =
            SceneGraphSerialization.CreateSystems(scene, systemReaders, preservePersistentIds, references);
        using (references.Enter())
        {
            foreach ((GameComponent component, SerializationReader state) in graph.componentStates)
                state.RestoreProperties(component);
            foreach ((GameSystem system, SerializationReader state) in systemStates)
                state.RestoreProperties(system);
        }
    }
}
