using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Inno.Core.Serialization;
using Inno.Core.Serialization.Converters;
using Inno.Scene;
using Inno.Scene.Components;

namespace Inno.Scene;

[SerializationExtension]
internal sealed class SceneSubtreeStateConverter : SerializationConverter<SceneSubtreeState>
{
    private const string C_ROOT_ID_KEY = "rootId";

    /// <summary>
    /// Writes the supplied value through the owning subsystem's validated output boundary.
    /// </summary>
    /// <param name="writer">
    /// The writer that receives the deterministic structured representation.
    /// </param>
    /// <param name="value">
    /// The concrete value read or transformed by this operation.
    /// </param>
    public override void Write(SerializationWriter writer, SceneSubtreeState value)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(value);
        GameObject root = value.root;
        GameScene scene = root.scene;
        SceneStructureSnapshot structure = scene.CaptureStructure();
        var included = new HashSet<GameObject>(ReferenceEqualityComparer.Instance);
        CollectSubtree(root, included);
        SceneObjectStructureSnapshot[] entries = structure.objects
            .Where(entry => included.Contains(entry.gameObject))
            .ToArray();
        EngineObject[] allObjects = GetAllObjects(scene);
        var sourceIds = new Dictionary<EngineObject, Guid>(ReferenceEqualityComparer.Instance);
        for (int i = 0; i < allObjects.Length; i++)
            sourceIds.Add(allObjects[i], allObjects[i].identity.persistentId);
        var references = new SceneGraphReferenceMap(scene, allObjects, sourceIds, root);

        writer.Write(C_ROOT_ID_KEY, root.identity.persistentId);
        using (references.Enter())
        {
            SceneGraphSerialization.WriteObjects(
                writer,
                entries,
                sourceIds,
                preserveRootSiblingOrder: true);
        }
    }

    /// <summary>
    /// Reads and validates the requested value without transferring storage ownership.
    /// </summary>
    /// <param name="reader">
    /// The reader positioned at the structured value to decode.
    /// </param>
    /// <returns>
    /// The validated scene subtree state that represents the completed operation.
    /// </returns>
    public override SceneSubtreeState Read(SerializationReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        GameScene scene = reader.context.GetRequired<GameScene>();
        Guid rootId = reader.Read<Guid>(C_ROOT_ID_KEY);
        IReadOnlyList<SerializationReader> objectReaders =
            reader.ReadObjectArray(SceneGraphSerialization.C_OBJECTS_KEY);
        SceneGraphSerialization.ValidateMissingReferenceAliases(reader);
        SceneGraphSerialization.ValidateObjects(objectReaders);
        var existing = new HashSet<GameObject>(scene.GetObjects(), ReferenceEqualityComparer.Instance);
        try
        {
            var references = new SceneGraphReferenceMap(scene);
            EngineObject[] externalObjects = GetAllObjects(scene);
            for (int i = 0; i < externalObjects.Length; i++)
                references.Register(externalObjects[i].identity.persistentId, externalObjects[i]);
            RestoredSceneGraph restored = SceneGraphSerialization.RestoreObjects(
                scene,
                objectReaders,
                preservePersistentIds: true,
                references,
                SceneGraphSerialization.ReadMissingReferenceAliases(reader),
                reader.context);
            if (!restored.objects.TryGetValue(rootId, out GameObject? root))
                throw new InvalidDataException($"Scene subtree root '{rootId}' is missing.");
            SceneGraphSerialization.ReconcilePrefabConnections(scene, reader.context, root);
            return new SceneSubtreeState(root);
        }
        catch (Exception exception)
        {
            SceneRestoreCompensation.RethrowAfterRemovingCreatedObjects(
                exception,
                scene,
                existing,
                "Scene subtree deserialization");
            throw;
        }
    }

    private static EngineObject[] GetAllObjects(GameScene scene)
        => scene.GetObjects()
            .SelectMany(static gameObject =>
                gameObject.GetComponents().Cast<EngineObject>().Prepend(gameObject))
            .Concat(scene.GetSystems())
            .ToArray();

    private static void CollectSubtree(GameObject gameObject, ISet<GameObject> result)
    {
        _ = result.Add(gameObject);
        IReadOnlyList<Transform> children = gameObject.transform.children;
        for (int i = 0; i < children.Count; i++)
            CollectSubtree(children[i].gameObject, result);
    }
}
