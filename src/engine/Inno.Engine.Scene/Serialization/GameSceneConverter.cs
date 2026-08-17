using System;
using System.Collections.Generic;
using System.Linq;

using Inno.Assets.Core;
using Inno.Core.Serialization;
using Inno.Core.Serialization.Converters;

namespace Inno.Engine.Scene;

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
        EngineObject[] engineObjects = snapshot.objects
            .SelectMany(static entry => entry.components.Cast<EngineObject>().Prepend(entry.gameObject))
            .ToArray();
        var sourceIds = new Dictionary<EngineObject, Guid>(ReferenceEqualityComparer.Instance);
        for (int i = 0; i < engineObjects.Length; i++)
            sourceIds.Add(engineObjects[i], engineObjects[i].identity.persistentId);
        var references = new SceneGraphReferenceMap(value, engineObjects, sourceIds);

        writer.Write(SceneGraphSerialization.C_SCHEMA_VERSION_KEY, SceneGraphSerialization.C_SCHEMA_VERSION);
        writer.Write(SceneGraphSerialization.C_SCENE_ID_KEY, value.identity.persistentId);
        writer.Write(SceneGraphSerialization.C_NAME_KEY, value.name);
        using (references.Enter())
        {
            SceneGraphSerialization.WriteObjects(
                writer,
                snapshot.objects,
                sourceIds,
                preserveRootSiblingOrder: true);
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
            var references = new SceneGraphReferenceMap(scene);
            SceneGraphSerialization.RestoreObjects(
                scene,
                reader.ReadObjectArray(SceneGraphSerialization.C_OBJECTS_KEY),
                preservePersistentIds: !instantiateFromAsset,
                references);
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
        target.ValidateRestoreTarget(sceneId);
        target.name = reader.Read<string>(SceneGraphSerialization.C_NAME_KEY);
        try
        {
            var references = new SceneGraphReferenceMap(target);
            SceneGraphSerialization.RestoreObjects(
                target,
                reader.ReadObjectArray(SceneGraphSerialization.C_OBJECTS_KEY),
                preservePersistentIds: true,
                references);
            SceneGraphSerialization.ReconcilePrefabConnections(target, reader.context);
        }
        catch
        {
            GameObject[] createdObjects = target.GetObjects().ToArray();
            for (int i = 0; i < createdObjects.Length; i++)
            {
                if (createdObjects[i].isRuntimeValid)
                    target.DestroyObject(createdObjects[i]);
            }
            throw;
        }
    }
}
