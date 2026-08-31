using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Inno.Assets.Core;
using Inno.Core.Serialization;
using Inno.Engine.Scene;
using Inno.Engine.Scene.Components;

namespace Inno.Engine.Scene.Assets;

internal static class PrefabOverrideProcessor
{
    internal static PrefabOverrideSet Capture(
        PrefabConnectionRecord connection,
        GameScene scene,
        SerializationContext context,
        SceneGraphReferenceMap storageReferences)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(storageReferences);
        if (connection.sourceAsset.isMissing || connection.sourceAsset.runtimePayload.IsEmpty)
            return connection.overrides;

        (GameScene sourceScene, _, PrefabConnectionRecord sourceConnection) =
            InstantiateSource(connection.sourceAsset);
        try
        {
            SceneGraphReferenceMap currentReferences = CreateComparisonReferences(scene, connection);
            SceneGraphReferenceMap sourceReferences = CreateComparisonReferences(sourceScene, sourceConnection);
            var overrides = new PrefabOverrideSet();

            CaptureObjectOverrides(
                connection,
                sourceConnection,
                scene,
                sourceScene,
                overrides);
            CaptureAdditions(connection, scene, overrides);
            CaptureComponentOverrides(
                connection,
                sourceConnection,
                scene,
                sourceScene,
                context,
                currentReferences,
                sourceReferences,
                storageReferences,
                overrides);
            PreserveOrphanedOverrides(connection.overrides, sourceConnection, overrides);
            connection.overrides = overrides;
            return overrides;
        }
        finally
        {
            if (!sourceScene.isDestroyed)
                sourceScene.Unload();
        }
    }

    internal static void Reconcile(
        PrefabConnectionRecord connection,
        GameObject instanceRoot,
        SerializationContext context)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(instanceRoot);
        ArgumentNullException.ThrowIfNull(context);
        if (connection.sourceAsset.isMissing || connection.sourceAsset.runtimePayload.IsEmpty)
        {
            RefreshInstanceInfo(connection, instanceRoot);
            return;
        }

        GameScene scene = instanceRoot.scene;
        (GameScene sourceScene, _, PrefabConnectionRecord sourceConnection) =
            InstantiateSource(connection.sourceAsset);
        try
        {
            MarkOrphanedOverrides(connection, sourceConnection, sourceScene);
            RemoveDeletedSourceObjects(connection, sourceConnection, scene, instanceRoot);
            CreateNewSourceObjects(connection, sourceConnection, scene, sourceScene);
            RemoveDeletedSourceComponents(connection, sourceConnection, scene);
            CreateNewSourceComponents(connection, sourceConnection, scene, sourceScene);

            SceneGraphReferenceMap sourceReferences = CreateComparisonReferences(sourceScene, sourceConnection);
            SceneGraphReferenceMap targetReferences = CreateTargetReferences(scene, connection);
            RestoreSourceProperties(
                connection,
                sourceConnection,
                scene,
                sourceScene,
                context,
                sourceReferences,
                targetReferences);
            RestoreSourceObjectState(connection, sourceConnection, scene, sourceScene, instanceRoot);
            RefreshInstanceInfo(connection, instanceRoot);
        }
        finally
        {
            if (!sourceScene.isDestroyed)
                sourceScene.Unload();
        }
    }

    private static void CaptureObjectOverrides(
        PrefabConnectionRecord connection,
        PrefabConnectionRecord sourceConnection,
        GameScene scene,
        GameScene sourceScene,
        PrefabOverrideSet overrides)
    {
        foreach ((Guid sourceId, Guid sourceRuntimeId) in sourceConnection.objectIdentities)
        {
            GameObject? sourceObject = sourceScene.FindObject(sourceRuntimeId);
            GameObject? currentObject = connection.objectIdentities.TryGetValue(sourceId, out Guid currentRuntimeId)
                ? scene.FindObject(currentRuntimeId)
                : null;
            if (sourceObject is null)
                continue;
            if (currentObject is null)
            {
                overrides.MarkObjectRemoved(sourceId);
                continue;
            }

            PrefabObjectOverrideKind kind = PrefabObjectOverrideKind.None;
            if (!string.Equals(currentObject.name, sourceObject.name, StringComparison.Ordinal))
                kind |= PrefabObjectOverrideKind.Name;
            if (!string.Equals(currentObject.tag, sourceObject.tag, StringComparison.Ordinal))
                kind |= PrefabObjectOverrideKind.Tag;
            if (currentObject.activeSelf != sourceObject.activeSelf)
                kind |= PrefabObjectOverrideKind.ActiveSelf;
            if (GetMappedParentId(currentObject, connection) != GetMappedParentId(sourceObject, sourceConnection))
                kind |= PrefabObjectOverrideKind.Parent;
            if (currentObject.transform.siblingIndex != sourceObject.transform.siblingIndex)
                kind |= PrefabObjectOverrideKind.SiblingIndex;
            overrides.SetStructure(sourceId, kind);
        }
    }

    private static void CaptureComponentOverrides(
        PrefabConnectionRecord connection,
        PrefabConnectionRecord sourceConnection,
        GameScene scene,
        GameScene sourceScene,
        SerializationContext context,
        SceneGraphReferenceMap currentReferences,
        SceneGraphReferenceMap sourceReferences,
        SceneGraphReferenceMap storageReferences,
        PrefabOverrideSet overrides)
    {
        foreach ((Guid sourceId, Guid sourceRuntimeId) in sourceConnection.componentIdentities)
        {
            GameComponent? sourceComponent = sourceScene.FindComponent(sourceRuntimeId);
            GameComponent? currentComponent = connection.componentIdentities.TryGetValue(sourceId, out Guid currentRuntimeId)
                ? scene.FindComponent(currentRuntimeId)
                : null;
            if (sourceComponent is null)
                continue;
            if (currentComponent is null || currentComponent.GetType() != sourceComponent.GetType())
            {
                overrides.MarkComponentRemoved(sourceId);
                continue;
            }

            IReadOnlyList<SerializedPropertyValueCodec.PropertyMember> members =
                SerializedPropertyValueCodec.GetMembers(sourceComponent.GetType());
            for (int memberIndex = 0; memberIndex < members.Count; memberIndex++)
            {
                SerializedPropertyValueCodec.PropertyMember member = members[memberIndex];
                byte[] sourceValue;
                byte[] currentValue;
                try
                {
                    sourceValue = SerializedPropertyValueCodec.Encode(
                        member,
                        sourceComponent,
                        context,
                        sourceReferences);
                    currentValue = SerializedPropertyValueCodec.Encode(
                        member,
                        currentComponent,
                        context,
                        currentReferences);
                }
                catch (Exception exception)
                {
                    throw new InvalidOperationException(
                        $"Failed to compare prefab override for GameComponent " +
                        $"'{currentComponent.GetType().FullName}.{member.name}' on GameObject " +
                        $"'{currentComponent.gameObject.name}'.",
                        exception);
                }
                if (sourceValue.AsSpan().SequenceEqual(currentValue))
                    continue;
                byte[] persistedValue;
                try
                {
                    persistedValue = SerializedPropertyValueCodec.Encode(
                        member,
                        currentComponent,
                        context,
                        storageReferences);
                }
                catch (Exception exception)
                {
                    throw new InvalidOperationException(
                        $"Failed to persist prefab override for GameComponent " +
                        $"'{currentComponent.GetType().FullName}.{member.name}' on GameObject " +
                        $"'{currentComponent.gameObject.name}'.",
                        exception);
                }
                overrides.SetProperty(new PrefabPropertyOverride(sourceId, member.name, persistedValue));
            }
        }
    }

    private static void CaptureAdditions(
        PrefabConnectionRecord connection,
        GameScene scene,
        PrefabOverrideSet overrides)
    {
        if (!connection.objectIdentities.TryGetValue(connection.sourceRootId, out Guid rootRuntimeId))
            return;
        GameObject? root = scene.FindObject(rootRuntimeId);
        if (root is null)
            return;
        var mappedObjectIds = connection.objectIdentities.Values.ToHashSet();
        var mappedComponentIds = connection.componentIdentities.Values.ToHashSet();
        var stack = new Stack<GameObject>();
        stack.Push(root);
        while (stack.Count != 0)
        {
            GameObject gameObject = stack.Pop();
            if (!mappedObjectIds.Contains(gameObject.identity.persistentId))
                overrides.MarkObjectAdded(gameObject.identity.persistentId);
            foreach (GameComponent component in gameObject.GetComponents())
            {
                if (!mappedComponentIds.Contains(component.identity.persistentId))
                    overrides.MarkComponentAdded(component.identity.persistentId);
            }
            foreach (Transform child in gameObject.transform.children)
                stack.Push(child.gameObject);
        }
    }

    private static void PreserveOrphanedOverrides(
        PrefabOverrideSet previous,
        PrefabConnectionRecord sourceConnection,
        PrefabOverrideSet current)
    {
        foreach (PrefabPropertyOverride property in previous.properties)
        {
            if (!sourceConnection.componentIdentities.ContainsKey(property.sourceComponentId))
            {
                current.SetProperty(property with
                {
                    value = (byte[])property.value.Clone(),
                    isOrphaned = true
                });
            }
        }
        foreach (PrefabStructureOverride structure in previous.structures)
        {
            if (!sourceConnection.objectIdentities.ContainsKey(structure.sourceObjectId))
                current.SetStructure(structure.sourceObjectId, structure.kind, isOrphaned: true);
        }
    }

    private static void MarkOrphanedOverrides(
        PrefabConnectionRecord connection,
        PrefabConnectionRecord sourceConnection,
        GameScene sourceScene)
    {
        foreach (PrefabPropertyOverride property in connection.overrides.properties.ToArray())
        {
            bool hasProperty = false;
            if (sourceConnection.componentIdentities.TryGetValue(
                    property.sourceComponentId,
                    out Guid sourceRuntimeId) &&
                sourceScene.FindComponent(sourceRuntimeId) is GameComponent sourceComponent)
            {
                hasProperty = SerializedPropertyValueCodec.GetMembers(sourceComponent.GetType())
                    .Any(member => string.Equals(
                        member.name,
                        property.propertyName,
                        StringComparison.Ordinal));
            }
            if (!hasProperty && !property.isOrphaned)
                connection.overrides.SetProperty(property with { isOrphaned = true });
        }
        foreach (PrefabStructureOverride structure in connection.overrides.structures.ToArray())
        {
            if (!sourceConnection.objectIdentities.ContainsKey(structure.sourceObjectId) &&
                !structure.isOrphaned)
            {
                connection.overrides.SetStructure(
                    structure.sourceObjectId,
                    structure.kind,
                    isOrphaned: true);
            }
        }
    }

    private static void RemoveDeletedSourceObjects(
        PrefabConnectionRecord connection,
        PrefabConnectionRecord sourceConnection,
        GameScene scene,
        GameObject instanceRoot)
    {
        Guid[] removedSourceIds = connection.objectIdentities.Keys
            .Where(sourceId =>
                connection.overrides.IsObjectRemoved(sourceId) ||
                !sourceConnection.objectIdentities.ContainsKey(sourceId))
            .ToArray();
        for (int i = 0; i < removedSourceIds.Length; i++)
        {
            Guid sourceId = removedSourceIds[i];
            if (!connection.objectIdentities.TryGetValue(sourceId, out Guid runtimeId))
                continue;
            GameObject? target = scene.FindObject(runtimeId);
            if (target is null || ReferenceEquals(target, instanceRoot))
                continue;

            GameObject[] addedChildren = target.transform.children
                .Select(static child => child.gameObject)
                .Where(child => !connection.objectIdentities.Values.Contains(child.identity.persistentId))
                .ToArray();
            for (int childIndex = 0; childIndex < addedChildren.Length; childIndex++)
                scene.SetParent(addedChildren[childIndex].transform, instanceRoot.transform, worldPositionStays: false);
            scene.DestroyObject(target);
            connection.RemoveObject(sourceId);
        }
    }

    private static void CreateNewSourceObjects(
        PrefabConnectionRecord connection,
        PrefabConnectionRecord sourceConnection,
        GameScene scene,
        GameScene sourceScene)
    {
        foreach ((Guid sourceId, Guid sourceRuntimeId) in sourceConnection.objectIdentities)
        {
            if (connection.objectIdentities.ContainsKey(sourceId) || connection.overrides.IsObjectRemoved(sourceId))
                continue;
            GameObject sourceObject = sourceScene.FindObject(sourceRuntimeId)
                ?? throw new InvalidOperationException($"Prefab source object '{sourceId}' disappeared during reconciliation.");
            GameObject target = scene.CreateObject(
                sourceObject.name,
                persistentId: null,
                transformPersistentId: null,
                invokeReset: false);
            target.SetTagDirect(sourceObject.tag);
            target.SetActiveSelfDirect(sourceObject.activeSelf);
            connection.MapObject(sourceId, target);

            Guid sourceTransformId = sourceConnection.componentIdentities.Single(
                pair => pair.Value == sourceObject.transform.identity.persistentId).Key;
            connection.MapComponent(sourceTransformId, target.transform);
        }
    }

    private static void RemoveDeletedSourceComponents(
        PrefabConnectionRecord connection,
        PrefabConnectionRecord sourceConnection,
        GameScene scene)
    {
        Guid[] removedSourceIds = connection.componentIdentities.Keys
            .Where(sourceId =>
                connection.overrides.IsComponentRemoved(sourceId) ||
                !sourceConnection.componentIdentities.ContainsKey(sourceId))
            .ToArray();
        for (int i = 0; i < removedSourceIds.Length; i++)
        {
            Guid sourceId = removedSourceIds[i];
            if (!connection.componentIdentities.TryGetValue(sourceId, out Guid runtimeId))
                continue;
            GameComponent? target = scene.FindComponent(runtimeId);
            if (target is not null && target is not Transform)
                scene.RemoveComponent(target.gameObject, target);
            connection.RemoveComponent(sourceId);
        }
    }

    private static void CreateNewSourceComponents(
        PrefabConnectionRecord connection,
        PrefabConnectionRecord sourceConnection,
        GameScene scene,
        GameScene sourceScene)
    {
        foreach ((Guid sourceId, Guid sourceRuntimeId) in sourceConnection.componentIdentities)
        {
            if (connection.componentIdentities.ContainsKey(sourceId) ||
                connection.overrides.IsComponentRemoved(sourceId))
            {
                continue;
            }
            GameComponent sourceComponent = sourceScene.FindComponent(sourceRuntimeId)
                ?? throw new InvalidOperationException($"Prefab source component '{sourceId}' disappeared during reconciliation.");
            Guid sourceOwnerId = sourceConnection.objectIdentities.Single(
                pair => pair.Value == sourceComponent.gameObject.identity.persistentId).Key;
            if (!connection.objectIdentities.TryGetValue(sourceOwnerId, out Guid targetOwnerId))
                continue;
            GameObject targetOwner = scene.FindObject(targetOwnerId)
                ?? throw new InvalidOperationException($"Prefab target owner for source object '{sourceOwnerId}' is unavailable.");
            GameComponent target = sourceComponent is Transform
                ? targetOwner.transform
                : scene.AddComponent(targetOwner, sourceComponent.GetType(), persistentId: null, invokeReset: false);
            connection.MapComponent(sourceId, target);
        }
    }

    private static void RestoreSourceProperties(
        PrefabConnectionRecord connection,
        PrefabConnectionRecord sourceConnection,
        GameScene scene,
        GameScene sourceScene,
        SerializationContext context,
        SceneGraphReferenceMap sourceReferences,
        SceneGraphReferenceMap targetReferences)
    {
        foreach ((Guid sourceId, Guid sourceRuntimeId) in sourceConnection.componentIdentities)
        {
            if (!connection.componentIdentities.TryGetValue(sourceId, out Guid targetRuntimeId))
                continue;
            GameComponent? sourceComponent = sourceScene.FindComponent(sourceRuntimeId);
            GameComponent? targetComponent = scene.FindComponent(targetRuntimeId);
            if (sourceComponent is null || targetComponent is null ||
                sourceComponent.GetType() != targetComponent.GetType())
            {
                continue;
            }

            IReadOnlyList<SerializedPropertyValueCodec.PropertyMember> members =
                SerializedPropertyValueCodec.GetMembers(sourceComponent.GetType());
            for (int memberIndex = 0; memberIndex < members.Count; memberIndex++)
            {
                SerializedPropertyValueCodec.PropertyMember member = members[memberIndex];
                if (connection.overrides.IsPropertyOverridden(sourceId, member.name))
                    continue;
                byte[] value = SerializedPropertyValueCodec.Encode(
                    member,
                    sourceComponent,
                    context,
                    sourceReferences);
                SerializedPropertyValueCodec.Decode(
                    member,
                    targetComponent,
                    value,
                    context,
                    targetReferences);
            }
        }
    }

    private static void RestoreSourceObjectState(
        PrefabConnectionRecord connection,
        PrefabConnectionRecord sourceConnection,
        GameScene scene,
        GameScene sourceScene,
        GameObject instanceRoot)
    {
        foreach ((Guid sourceId, Guid sourceRuntimeId) in sourceConnection.objectIdentities)
        {
            if (!connection.objectIdentities.TryGetValue(sourceId, out Guid targetRuntimeId))
                continue;
            GameObject? sourceObject = sourceScene.FindObject(sourceRuntimeId);
            GameObject? targetObject = scene.FindObject(targetRuntimeId);
            if (sourceObject is null || targetObject is null)
                continue;

            PrefabObjectOverrideKind kind = connection.overrides.GetStructureOverride(sourceId);
            if ((kind & PrefabObjectOverrideKind.Name) == 0)
                targetObject.SetNameDirect(sourceObject.name);
            if ((kind & PrefabObjectOverrideKind.Tag) == 0)
                targetObject.SetTagDirect(sourceObject.tag);
            if ((kind & PrefabObjectOverrideKind.ActiveSelf) == 0)
                targetObject.SetActiveSelfDirect(sourceObject.activeSelf);
            if (!ReferenceEquals(targetObject, instanceRoot) &&
                (kind & PrefabObjectOverrideKind.Parent) == 0)
            {
                Guid sourceParentId = GetMappedParentId(sourceObject, sourceConnection);
                Transform? targetParent = sourceParentId == Guid.Empty
                    ? instanceRoot.transform
                    : connection.objectIdentities.TryGetValue(sourceParentId, out Guid parentRuntimeId)
                        ? scene.FindObject(parentRuntimeId)?.transform
                        : null;
                scene.SetParent(targetObject.transform, targetParent, worldPositionStays: false);
            }
        }

        foreach ((Guid sourceId, Guid sourceRuntimeId) in sourceConnection.objectIdentities
                     .OrderBy(pair => sourceScene.FindObject(pair.Value)?.transform.siblingIndex ?? int.MaxValue))
        {
            if (!connection.objectIdentities.TryGetValue(sourceId, out Guid targetRuntimeId))
                continue;
            GameObject? targetObject = scene.FindObject(targetRuntimeId);
            if (targetObject is null || ReferenceEquals(targetObject, instanceRoot))
                continue;
            if ((connection.overrides.GetStructureOverride(sourceId) & PrefabObjectOverrideKind.SiblingIndex) == 0)
            {
                GameObject? sourceObject = sourceScene.FindObject(sourceRuntimeId);
                if (sourceObject is not null)
                    scene.SetSiblingIndex(targetObject.transform, sourceObject.transform.siblingIndex);
            }
            scene.RecomputeActiveSubtree(targetObject);
        }
        scene.RecomputeActiveSubtree(instanceRoot);
    }

    private static void RefreshInstanceInfo(PrefabConnectionRecord connection, GameObject instanceRoot)
    {
        GameScene scene = instanceRoot.scene;
        foreach ((Guid sourceId, Guid runtimeId) in connection.objectIdentities)
        {
            GameObject? gameObject = scene.FindObject(runtimeId);
            if (gameObject is null)
                continue;
            gameObject.SetPrefabInstanceDirect(new PrefabInstanceInfo(
                connection.sourceAsset.identity.persistentId,
                sourceId,
                instanceRoot,
                ReferenceEquals(gameObject, instanceRoot),
                connection.isVariant,
                connection.sourceAsset.isMissing,
                connection.overrides.count,
                connection.overrides.orphanedCount,
                connection.sourceAsset));
        }
        instanceRoot.SetPrefabConnectionDirect(connection);
    }

    private static SceneGraphReferenceMap CreateComparisonReferences(
        GameScene scene,
        PrefabConnectionRecord connection)
    {
        SceneStructureSnapshot snapshot = scene.CaptureStructure();
        EngineObject[] allObjects = snapshot.objects
            .SelectMany(static entry => entry.components.Cast<EngineObject>().Prepend(entry.gameObject))
            .ToArray();
        var ids = new Dictionary<EngineObject, Guid>(ReferenceEqualityComparer.Instance);
        for (int objectIndex = 0; objectIndex < allObjects.Length; objectIndex++)
            ids.Add(allObjects[objectIndex], allObjects[objectIndex].identity.persistentId);
        foreach ((Guid sourceId, Guid runtimeId) in connection.objectIdentities)
        {
            GameObject? gameObject = scene.FindObject(runtimeId);
            if (gameObject is not null)
                ids[gameObject] = sourceId;
        }
        foreach ((Guid sourceId, Guid runtimeId) in connection.componentIdentities)
        {
            GameComponent? component = scene.FindComponent(runtimeId);
            if (component is not null)
                ids[component] = sourceId;
        }
        return new SceneGraphReferenceMap(scene, allObjects, ids);
    }

    private static SceneGraphReferenceMap CreateTargetReferences(
        GameScene scene,
        PrefabConnectionRecord connection)
    {
        SceneGraphReferenceMap references = CreateComparisonReferences(scene, connection);
        foreach ((Guid sourceId, Guid runtimeId) in connection.objectIdentities)
        {
            GameObject? gameObject = scene.FindObject(runtimeId);
            if (gameObject is not null)
                references.Register(sourceId, gameObject);
        }
        foreach ((Guid sourceId, Guid runtimeId) in connection.componentIdentities)
        {
            GameComponent? component = scene.FindComponent(runtimeId);
            if (component is not null)
                references.Register(sourceId, component);
        }
        return references;
    }

    private static Guid GetMappedParentId(GameObject gameObject, PrefabConnectionRecord connection)
    {
        Transform? parent = gameObject.transform.parent;
        if (parent is null)
            return Guid.Empty;
        Guid parentRuntimeId = parent.gameObject.identity.persistentId;
        foreach ((Guid sourceId, Guid runtimeId) in connection.objectIdentities)
        {
            if (runtimeId == parentRuntimeId)
                return sourceId;
        }
        return parentRuntimeId;
    }

    private static (GameScene scene, GameObject root, PrefabConnectionRecord connection) InstantiateSource(
        AssetObject sourceAsset)
    {
        var sourceScene = new GameScene("Prefab Source Comparison");
        SerializationContext context = SerializationContext.empty
            .With(sourceScene)
            .With(sourceAsset);
        try
        {
            using (SceneGraphReferenceMap.Suspend())
            {
                GameObject sourceRoot = SerializationManager.Deserialize<GameObject>(
                    sourceAsset.runtimePayload.Span,
                    context);
                if (!string.IsNullOrWhiteSpace(sourceAsset.sourcePath))
                    sourceRoot.name = Path.GetFileNameWithoutExtension(sourceAsset.sourcePath);
                PrefabConnectionRecord connection = sourceRoot.prefabConnection
                    ?? throw new InvalidOperationException(
                        $"Prefab source asset '{sourceAsset.identity.persistentId}' did not create a source mapping.");
                return (sourceScene, sourceRoot, connection);
            }
        }
        catch
        {
            if (!sourceScene.isDestroyed)
                sourceScene.Unload();
            throw;
        }
    }
}
