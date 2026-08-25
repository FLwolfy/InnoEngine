using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using System.Linq;

using Inno.Core.Reflection;
using Inno.Core.Serialization;
using Inno.Editor.Core;
using Inno.Editor.Interactions;
using Inno.Engine.Scene;
using Inno.Engine.Scene.Assets;
using Inno.Engine.Scene.Components;
using Inno.Engine.Scene.Layers;

namespace Inno.Editor.Scene;

/// <summary>
/// Applies scene-document mutations and records compact, reload-safe inverse data in editor history.
/// </summary>
[EditorModule("scene-edits", order: 210)]
public sealed class SceneEdits : EditorModule
{
    private readonly EditorSceneWorkspace m_workspace;
    private readonly EditorInteractions m_interactions;

    /// <summary>
    /// Creates the scene editing service used by editor actions and drag handlers.
    /// </summary>
    /// <param name="workspace">The current scene document workspace.</param>
    /// <param name="interactions">The current editor interaction runtime.</param>
    internal SceneEdits(EditorSceneWorkspace workspace, EditorInteractions interactions)
    {
        m_workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        m_interactions = interactions ?? throw new ArgumentNullException(nameof(interactions));
    }

    /// <summary>
    /// Creates an additive scene and records a reversible document change.
    /// </summary>
    /// <param name="historyName">The user-facing history entry name.</param>
    /// <returns>The newly created active scene.</returns>
    public GameScene CreateScene(string historyName = "Create Scene")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(historyName);
        Guid? activeBefore = GetActiveSceneId();
        Guid? selectedBefore = GetSelectionId();
        GameScene scene = m_workspace.CreateScene();
        RecordWithRollback(
            () =>
            {
                EditorSceneWorkspace.SceneDocumentSnapshot snapshot =
                    m_workspace.CaptureDocumentSnapshot(scene);
                RecordDocument(
                    historyName,
                    existsBefore: false,
                    existsAfter: true,
                    snapshot,
                    activeBefore,
                    GetActiveSceneId(),
                    selectedBefore,
                    scene.identity.persistentId);
            },
            () =>
            {
                if (!m_workspace.CloseScene(scene))
                    throw new InvalidOperationException("The unrecorded scene could not be removed.");
                m_workspace.RestoreActiveScene(activeBefore);
            });
        return scene;
    }

    /// <summary>
    /// Creates a GameObject, optionally parents it, and records only the new subtree state.
    /// </summary>
    /// <param name="scene">The loaded scene that will own the object.</param>
    /// <param name="parent">The optional parent transform.</param>
    /// <param name="historyName">The user-facing history entry name.</param>
    /// <returns>The newly created GameObject.</returns>
    public GameObject CreateGameObject(
        GameScene scene,
        Transform? parent = null,
        string historyName = "Create GameObject")
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentException.ThrowIfNullOrWhiteSpace(historyName);
        if (parent is not null && !ReferenceEquals(parent.gameObject.scene, scene))
            throw new ArgumentException("The parent belongs to another scene.", nameof(parent));
        Guid? selectedBefore = GetSelectionId();
        GameObject gameObject = scene.CreateObject();
        RecordWithRollback(
            () =>
            {
                if (parent is not null)
                    gameObject.transform.SetParent(parent);
                byte[] subtree = SceneSubtreeSerialization.Capture(gameObject);
                RecordSubtree(
                    historyName,
                    gameObject,
                    existsBefore: false,
                    existsAfter: true,
                    subtree,
                    [],
                    selectedBefore,
                    gameObject.identity.persistentId);
            },
            () =>
            {
                if (gameObject.isRuntimeValid && !scene.DestroyObject(gameObject))
                    throw new InvalidOperationException("The unrecorded GameObject could not be removed.");
            });
        return gameObject;
    }

    /// <summary>
    /// Deletes a GameObject subtree and records only that subtree plus incoming serialized references.
    /// </summary>
    /// <param name="gameObject">The live subtree root to delete.</param>
    /// <param name="historyName">The user-facing history entry name.</param>
    /// <returns><see langword="true"/> when the subtree was deleted and recorded.</returns>
    public bool DeleteGameObject(GameObject gameObject, string historyName = "Delete GameObject")
    {
        ArgumentNullException.ThrowIfNull(gameObject);
        ArgumentException.ThrowIfNullOrWhiteSpace(historyName);
        if (!gameObject.isRuntimeValid)
            return false;
        GameScene scene = gameObject.scene;
        byte[] subtree = SceneSubtreeSerialization.Capture(gameObject);
        SceneIncomingReferenceState[] incoming = SceneReferenceIndex.CaptureIncoming(gameObject);
        Guid? parentId = gameObject.transform.parent?.gameObject.identity.persistentId;
        int siblingIndex = gameObject.transform.siblingIndex;
        Guid rootId = gameObject.identity.persistentId;
        Guid? selectedBefore = GetSelectionId();
        try
        {
            if (!scene.DestroyObject(gameObject))
                return false;
            var data = new SceneSubtreeHistoryData(
                scene.identity.persistentId,
                rootId,
                parentId,
                siblingIndex,
                existsBefore: true,
                existsAfter: false,
                subtree,
                incoming,
                selectedBefore,
                selectedAfter: null);
            Record(historyName, SceneHistoryKinds.Subtree, data.Encode());
        }
        catch (Exception exception)
        {
            if (Inno.Core.Identity.IdentityManager.Get<GameObject>(rootId) is not null)
            {
                ExceptionDispatchInfo.Capture(exception).Throw();
            }
            RollbackAndRethrow(exception, () =>
            {
                Transform? parent = parentId is Guid id
                    ? Inno.Core.Identity.IdentityManager.Get<GameObject>(id)?.transform
                    : null;
                _ = SceneSubtreeSerialization.Restore(scene, subtree, parent, siblingIndex);
                RequireReferenceRestore(SceneReferenceIndex.RestoreIncoming(incoming));
            });
        }
        return true;
    }

    /// <summary>
    /// Adds one component and records its identity, stable type, index, and persistent properties.
    /// </summary>
    /// <param name="owner">The live GameObject receiving the component.</param>
    /// <param name="componentType">The concrete component type to create.</param>
    /// <param name="historyName">An optional user-facing history entry name.</param>
    /// <returns>The newly attached component.</returns>
    public GameComponent AddComponent(
        GameObject owner,
        Type componentType,
        string? historyName = null)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(componentType);
        GameComponent component = owner.AddComponent(componentType);
        try
        {
            Guid stableTypeId = GetStableTypeId(componentType);
            byte[] state = ScenePropertySerialization.CaptureProperties(component);
            RecordElement(
                historyName ?? $"Add {componentType.Name}",
                new SceneElementHistoryData(
                    SceneElementKind.Component,
                    owner.scene.identity.persistentId,
                    owner.identity.persistentId,
                    component.identity.persistentId,
                    stableTypeId,
                    beforeIndex: -1,
                    afterIndex: owner.GetComponentIndex(component),
                    existsBefore: false,
                    existsAfter: true,
                    beforeState: [],
                    afterState: state,
                    incomingReferences: []));
            return component;
        }
        catch (Exception exception)
        {
            RollbackAndRethrow(exception, () =>
            {
                if (!component.isDestroyed && !owner.RemoveComponent(component))
                    throw new InvalidOperationException("The unrecorded component could not be removed.");
            });
            throw;
        }
    }

    /// <summary>
    /// Removes one component and records the state required to recreate the same logical instance.
    /// </summary>
    /// <param name="component">The attached non-Transform component to remove.</param>
    /// <param name="historyName">An optional user-facing history entry name.</param>
    /// <returns><see langword="true"/> when the component was removed and recorded.</returns>
    public bool RemoveComponent(GameComponent component, string? historyName = null)
    {
        ArgumentNullException.ThrowIfNull(component);
        if (component.isDestroyed)
            return false;
        GameObject owner = component.gameObject;
        GameScene scene = owner.scene;
        Guid stableTypeId = GetStableTypeId(component.GetType());
        byte[] state = ScenePropertySerialization.CaptureProperties(component);
        SceneIncomingReferenceState[] incoming = SceneReferenceIndex.CaptureIncoming(component, scene);
        int index = owner.GetComponentIndex(component);
        Guid componentId = component.identity.persistentId;
        Type componentType = component.GetType();
        try
        {
            if (!owner.RemoveComponent(component))
                return false;
            RecordElement(
                historyName ?? $"Remove {componentType.Name}",
                new SceneElementHistoryData(
                    SceneElementKind.Component,
                    scene.identity.persistentId,
                    owner.identity.persistentId,
                    componentId,
                    stableTypeId,
                    beforeIndex: index,
                    afterIndex: -1,
                    existsBefore: true,
                    existsAfter: false,
                    beforeState: state,
                    afterState: [],
                    incoming));
        }
        catch (Exception exception)
        {
            if (Inno.Core.Identity.IdentityManager.Get<GameComponent>(componentId) is not null)
            {
                ExceptionDispatchInfo.Capture(exception).Throw();
            }
            RollbackAndRethrow(exception, () =>
            {
                GameComponent restored = SceneElementSerialization.RestoreComponent(
                    owner,
                    stableTypeId,
                    componentId,
                    index,
                    state);
                RequirePropertyRestore(restored, state);
                RequireReferenceRestore(SceneReferenceIndex.RestoreIncoming(incoming));
            });
        }
        return true;
    }

    /// <summary>
    /// Resets one component and records its compact property state before and after Reset.
    /// </summary>
    /// <param name="component">The attached component to reset.</param>
    /// <param name="historyName">An optional user-facing history entry name.</param>
    public void ResetComponent(GameComponent component, string? historyName = null)
    {
        ArgumentNullException.ThrowIfNull(component);
        GameObject owner = component.gameObject;
        byte[] before = ScenePropertySerialization.CaptureProperties(component);
        byte[] after;
        try
        {
            owner.ResetComponent(component);
            after = ScenePropertySerialization.CaptureProperties(component);
        }
        catch (Exception exception)
        {
            RollbackAndRethrow(exception, () => RequirePropertyRestore(component, before));
            throw;
        }
        if (before.AsSpan().SequenceEqual(after))
            return;
        int index = owner.GetComponentIndex(component);
        RecordWithRollback(
            () => RecordElement(
                historyName ?? $"Reset {component.GetType().Name}",
                new SceneElementHistoryData(
                SceneElementKind.Component,
                owner.scene.identity.persistentId,
                owner.identity.persistentId,
                component.identity.persistentId,
                GetStableTypeId(component.GetType()),
                index,
                index,
                existsBefore: true,
                existsAfter: true,
                before,
                after,
                incomingReferences: [])),
            () => RequirePropertyRestore(component, before));
    }

    /// <summary>
    /// Moves an attached component and records only its two attachment indices.
    /// </summary>
    /// <param name="component">The attached non-Transform component to move.</param>
    /// <param name="componentIndex">The requested attachment index.</param>
    /// <param name="historyName">The user-facing history entry name.</param>
    public void SetComponentIndex(
        GameComponent component,
        int componentIndex,
        string historyName = "Move Component")
    {
        ArgumentNullException.ThrowIfNull(component);
        ArgumentException.ThrowIfNullOrWhiteSpace(historyName);
        GameObject owner = component.gameObject;
        int beforeIndex = owner.GetComponentIndex(component);
        int afterIndex;
        try
        {
            owner.SetComponentIndex(component, componentIndex);
            afterIndex = owner.GetComponentIndex(component);
        }
        catch (Exception exception)
        {
            RollbackAndRethrow(exception, () => owner.SetComponentIndex(component, beforeIndex));
            throw;
        }
        if (beforeIndex == afterIndex)
            return;
        RecordWithRollback(
            () => RecordElement(
                historyName,
                new SceneElementHistoryData(
                SceneElementKind.Component,
                owner.scene.identity.persistentId,
                owner.identity.persistentId,
                component.identity.persistentId,
                GetStableTypeId(component.GetType()),
                beforeIndex,
                afterIndex,
                existsBefore: true,
                existsAfter: true,
                beforeState: [],
                afterState: [],
                incomingReferences: [])),
            () => owner.SetComponentIndex(component, beforeIndex));
    }

    /// <summary>
    /// Adds one scene system and records its identity, stable type, index, and persistent properties.
    /// </summary>
    /// <param name="scene">The loaded scene receiving the system.</param>
    /// <param name="systemType">The concrete system type to create.</param>
    /// <param name="historyName">An optional user-facing history entry name.</param>
    /// <returns>The newly registered system.</returns>
    public GameSystem AddSystem(GameScene scene, Type systemType, string? historyName = null)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(systemType);
        GameSystem system = scene.AddSystem(systemType);
        try
        {
            RecordElement(
                historyName ?? $"Add {systemType.Name}",
                new SceneElementHistoryData(
                    SceneElementKind.System,
                    scene.identity.persistentId,
                    Guid.Empty,
                    system.identity.persistentId,
                    GetStableTypeId(systemType),
                    beforeIndex: -1,
                    afterIndex: scene.GetSystemIndex(system),
                    existsBefore: false,
                    existsAfter: true,
                    beforeState: [],
                    afterState: ScenePropertySerialization.CaptureProperties(system),
                    incomingReferences: []));
            return system;
        }
        catch (Exception exception)
        {
            RollbackAndRethrow(exception, () =>
            {
                if (!system.isDestroyed && !scene.RemoveSystem(system))
                    throw new InvalidOperationException("The unrecorded system could not be removed.");
            });
            throw;
        }
    }

    /// <summary>
    /// Removes one scene system and records the state required to recreate the same logical instance.
    /// </summary>
    /// <param name="scene">The scene currently owning the system.</param>
    /// <param name="system">The registered system to remove.</param>
    /// <param name="historyName">An optional user-facing history entry name.</param>
    /// <returns><see langword="true"/> when the system was removed and recorded.</returns>
    public bool RemoveSystem(GameScene scene, GameSystem system, string? historyName = null)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(system);
        if (system.isDestroyed)
            return false;
        byte[] state = ScenePropertySerialization.CaptureProperties(system);
        SceneIncomingReferenceState[] incoming = SceneReferenceIndex.CaptureIncoming(system, scene);
        int index = scene.GetSystemIndex(system);
        Guid systemId = system.identity.persistentId;
        Guid stableTypeId = GetStableTypeId(system.GetType());
        Type systemType = system.GetType();
        try
        {
            if (!scene.RemoveSystem(system))
                return false;
            RecordElement(
                historyName ?? $"Remove {systemType.Name}",
                new SceneElementHistoryData(
                SceneElementKind.System,
                scene.identity.persistentId,
                Guid.Empty,
                systemId,
                stableTypeId,
                beforeIndex: index,
                afterIndex: -1,
                existsBefore: true,
                existsAfter: false,
                beforeState: state,
                    afterState: [],
                    incoming));
        }
        catch (Exception exception)
        {
            if (Inno.Core.Identity.IdentityManager.Get<GameSystem>(systemId) is not null)
            {
                ExceptionDispatchInfo.Capture(exception).Throw();
            }
            RollbackAndRethrow(exception, () =>
            {
                GameSystem restored = SceneElementSerialization.RestoreSystem(
                    scene,
                    stableTypeId,
                    systemId,
                    index,
                    state);
                RequirePropertyRestore(restored, state);
                RequireReferenceRestore(SceneReferenceIndex.RestoreIncoming(incoming));
            });
        }
        return true;
    }

    /// <summary>
    /// Resets one scene system and records its compact property state before and after Reset.
    /// </summary>
    /// <param name="scene">The scene currently owning the system.</param>
    /// <param name="system">The registered system to reset.</param>
    /// <param name="historyName">An optional user-facing history entry name.</param>
    public void ResetSystem(GameScene scene, GameSystem system, string? historyName = null)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(system);
        byte[] before = ScenePropertySerialization.CaptureProperties(system);
        byte[] after;
        try
        {
            scene.ResetSystem(system);
            after = ScenePropertySerialization.CaptureProperties(system);
        }
        catch (Exception exception)
        {
            RollbackAndRethrow(exception, () => RequirePropertyRestore(system, before));
            throw;
        }
        if (before.AsSpan().SequenceEqual(after))
            return;
        int index = scene.GetSystemIndex(system);
        RecordWithRollback(
            () => RecordElement(
                historyName ?? $"Reset {system.GetType().Name}",
                new SceneElementHistoryData(
                SceneElementKind.System,
                scene.identity.persistentId,
                Guid.Empty,
                system.identity.persistentId,
                GetStableTypeId(system.GetType()),
                index,
                index,
                existsBefore: true,
                existsAfter: true,
                before,
                after,
                incomingReferences: [])),
            () => RequirePropertyRestore(system, before));
    }

    /// <summary>
    /// Moves a registered system and records only its two display indices.
    /// </summary>
    /// <param name="scene">The scene currently owning the system.</param>
    /// <param name="system">The registered system to move.</param>
    /// <param name="systemIndex">The requested display index.</param>
    /// <param name="historyName">The user-facing history entry name.</param>
    public void SetSystemIndex(
        GameScene scene,
        GameSystem system,
        int systemIndex,
        string historyName = "Move System")
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(system);
        ArgumentException.ThrowIfNullOrWhiteSpace(historyName);
        int beforeIndex = scene.GetSystemIndex(system);
        int afterIndex;
        try
        {
            scene.SetSystemIndex(system, systemIndex);
            afterIndex = scene.GetSystemIndex(system);
        }
        catch (Exception exception)
        {
            RollbackAndRethrow(exception, () => scene.SetSystemIndex(system, beforeIndex));
            throw;
        }
        if (beforeIndex == afterIndex)
            return;
        RecordWithRollback(
            () => RecordElement(
                historyName,
                new SceneElementHistoryData(
                SceneElementKind.System,
                scene.identity.persistentId,
                Guid.Empty,
                system.identity.persistentId,
                GetStableTypeId(system.GetType()),
                beforeIndex,
                afterIndex,
                existsBefore: true,
                existsAfter: true,
                beforeState: [],
                afterState: [],
                incomingReferences: [])),
            () => scene.SetSystemIndex(system, beforeIndex));
    }

    /// <summary>
    /// Closes one loaded scene without deleting its source asset and records a reversible document change.
    /// </summary>
    /// <param name="scene">The loaded scene to close.</param>
    /// <param name="historyName">The user-facing history entry name.</param>
    /// <returns><see langword="true"/> when the scene was closed and recorded.</returns>
    public bool CloseScene(GameScene scene, string historyName = "Close Scene")
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentException.ThrowIfNullOrWhiteSpace(historyName);
        EditorSceneWorkspace.SceneDocumentSnapshot snapshot = m_workspace.CaptureDocumentSnapshot(scene);
        Guid? activeBefore = GetActiveSceneId();
        Guid? selectedBefore = GetSelectionId();
        if (!m_workspace.CloseScene(scene))
            return false;
        RecordWithRollback(
            () => RecordDocument(
                historyName,
                existsBefore: true,
                existsAfter: false,
                snapshot,
                activeBefore,
                GetActiveSceneId(),
                selectedBefore,
                GetSelectionId()),
            () =>
            {
                _ = m_workspace.RestoreDocumentSnapshot(snapshot);
                m_workspace.RestoreActiveScene(activeBefore);
            });
        return true;
    }

    /// <summary>
    /// Moves a loaded scene to a hierarchy index and records the two integer positions.
    /// </summary>
    /// <param name="scene">The loaded scene to reorder.</param>
    /// <param name="sceneIndex">The requested hierarchy index.</param>
    /// <param name="historyName">The user-facing history entry name.</param>
    public void SetSceneIndex(GameScene scene, int sceneIndex, string historyName = "Reorder Scene")
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentException.ThrowIfNullOrWhiteSpace(historyName);
        int beforeIndex = SceneManager.GetSceneIndex(scene);
        int afterIndex;
        try
        {
            SceneManager.SetSceneIndex(scene, sceneIndex);
            afterIndex = SceneManager.GetSceneIndex(scene);
        }
        catch (Exception exception)
        {
            RollbackAndRethrow(exception, () => SceneManager.SetSceneIndex(scene, beforeIndex));
            throw;
        }
        if (beforeIndex == afterIndex)
            return;
        var data = new SceneOrderHistoryData(scene.identity.persistentId, beforeIndex, afterIndex);
        RecordWithRollback(
            () => m_interactions.history.RecordApplied(
                historyName,
                new EditorHistoryChange(
                    SceneHistoryKinds.Order,
                    EditorHistoryPayload.FromBytes(data.Encode()))),
            () => SceneManager.SetSceneIndex(scene, beforeIndex));
    }

    /// <summary>
    /// Renames a loaded scene and records the two display strings.
    /// </summary>
    /// <param name="scene">The loaded scene to rename.</param>
    /// <param name="name">The new display name.</param>
    /// <param name="historyName">The user-facing history entry name.</param>
    public void RenameScene(GameScene scene, string name, string historyName = "Rename Scene")
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(name);
        ChangeScalar(
            scene,
            SceneScalarKind.SceneName,
            scene.name,
            name,
            value => scene.name = value,
            historyName,
            $"scene-name:{scene.identity.persistentId:N}");
    }

    /// <summary>
    /// Renames a live GameObject and records the two display strings.
    /// </summary>
    /// <param name="gameObject">The live GameObject to rename.</param>
    /// <param name="name">The new display name.</param>
    /// <param name="historyName">The user-facing history entry name.</param>
    public void RenameGameObject(
        GameObject gameObject,
        string name,
        string historyName = "Rename GameObject")
    {
        ArgumentNullException.ThrowIfNull(gameObject);
        ArgumentNullException.ThrowIfNull(name);
        ChangeScalar(
            gameObject,
            SceneScalarKind.GameObjectName,
            gameObject.name,
            name,
            value => gameObject.name = value,
            historyName,
            $"game-object-name:{gameObject.identity.persistentId:N}");
    }

    /// <summary>
    /// Changes the explicit active state of a GameObject and records the two Boolean values.
    /// </summary>
    /// <param name="gameObject">The live GameObject whose active state should change.</param>
    /// <param name="active">The requested explicit active state.</param>
    /// <param name="historyName">An optional user-facing history entry name.</param>
    public void SetGameObjectActive(
        GameObject gameObject,
        bool active,
        string? historyName = null)
    {
        ArgumentNullException.ThrowIfNull(gameObject);
        string before = gameObject.activeSelf ? "1" : "0";
        string after = active ? "1" : "0";
        ChangeScalar(
            gameObject,
            SceneScalarKind.GameObjectActive,
            before,
            after,
            value => gameObject.SetActive(string.Equals(value, "1", StringComparison.Ordinal)),
            historyName ?? (active ? "Activate GameObject" : "Deactivate GameObject"),
            mergeKey: null);
    }

    /// <summary>
    /// Changes the tag of a live GameObject and records the two ordinal tag strings.
    /// </summary>
    /// <param name="gameObject">The live GameObject whose tag should change.</param>
    /// <param name="tag">The requested non-empty tag.</param>
    /// <param name="historyName">The user-facing history entry name.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="gameObject"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="tag"/> or <paramref name="historyName"/> is empty.
    /// </exception>
    public void SetGameObjectTag(
        GameObject gameObject,
        string tag,
        string historyName = "Set GameObject Tag")
    {
        ArgumentNullException.ThrowIfNull(gameObject);
        ArgumentException.ThrowIfNullOrWhiteSpace(tag);
        ArgumentException.ThrowIfNullOrWhiteSpace(historyName);
        string requestedTag = tag.Trim();
        ChangeScalar(
            gameObject,
            SceneScalarKind.GameObjectTag,
            gameObject.tag,
            requestedTag,
            value => gameObject.tag = value,
            historyName,
            $"game-object-tag:{gameObject.identity.persistentId:N}");
    }

    /// <summary>
    /// Changes the layer of a live GameObject and records the two stable numeric layer slots.
    /// </summary>
    /// <param name="gameObject">The live GameObject whose layer should change.</param>
    /// <param name="layer">The requested project layer slot.</param>
    /// <param name="historyName">The user-facing history entry name.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="gameObject"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="historyName"/> is empty.
    /// </exception>
    public void SetGameObjectLayer(
        GameObject gameObject,
        GameLayer layer,
        string historyName = "Set GameObject Layer")
    {
        ArgumentNullException.ThrowIfNull(gameObject);
        ArgumentException.ThrowIfNullOrWhiteSpace(historyName);
        ChangeScalar(
            gameObject,
            SceneScalarKind.GameObjectLayer,
            gameObject.layer.index.ToString(System.Globalization.CultureInfo.InvariantCulture),
            layer.index.ToString(System.Globalization.CultureInfo.InvariantCulture),
            value => gameObject.layer = new GameLayer(
                int.Parse(value, System.Globalization.CultureInfo.InvariantCulture)),
            historyName,
            $"game-object-layer:{gameObject.identity.persistentId:N}");
    }

    /// <summary>
    /// Applies a mutation to one serializable scene property and records only its before and after values.
    /// </summary>
    /// <param name="target">The live scene object containing the root serialized property.</param>
    /// <param name="propertyName">The exact root serialized member key.</param>
    /// <param name="mutation">The mutation that assigns the new value.</param>
    /// <param name="historyName">The user-facing history entry name.</param>
    /// <param name="mergeKey">An optional stable key for coalescing adjacent continuous edits.</param>
    /// <returns><see langword="true"/> when the property value changed and a history entry was recorded.</returns>
    public bool ChangeProperty(
        EngineObject target,
        string propertyName,
        Action mutation,
        string historyName,
        string? mergeKey = null)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);
        ArgumentNullException.ThrowIfNull(mutation);
        ArgumentException.ThrowIfNullOrWhiteSpace(historyName);
        byte[] before = ScenePropertySerialization.CaptureProperty(target, propertyName);
        byte[] after;
        try
        {
            mutation();
            after = ScenePropertySerialization.CaptureProperty(target, propertyName);
        }
        catch (Exception exception)
        {
            RollbackAndRethrow(exception, () => RequirePropertyRestore(target, before));
            throw;
        }
        if (before.AsSpan().SequenceEqual(after))
            return false;
        ScenePropertyHistoryData data = ScenePropertyHistoryData.Create(
            target.identity.persistentId,
            propertyName,
            before,
            after);
        RecordWithRollback(
            () => m_interactions.history.RecordApplied(
                historyName,
                new EditorHistoryChange(
                    SceneHistoryKinds.Property,
                    EditorHistoryPayload.FromBytes(data.Encode()),
                    mergeKey)),
            () => RequirePropertyRestore(target, before));
        return true;
    }

    /// <summary>
    /// Applies a hierarchy mutation and records only the affected parent and sibling-index tuples.
    /// </summary>
    /// <param name="gameObject">The primary GameObject being moved.</param>
    /// <param name="mutation">The hierarchy mutation to execute.</param>
    /// <param name="historyName">The user-facing history entry name.</param>
    /// <param name="relatedObjects">
    /// Additional objects whose placements the mutation may change, such as promoted children.
    /// </param>
    /// <returns><see langword="true"/> when at least one placement changed.</returns>
    public bool ChangeHierarchy(
        GameObject gameObject,
        Action mutation,
        string historyName = "Move GameObject",
        IReadOnlyCollection<GameObject>? relatedObjects = null)
    {
        ArgumentNullException.ThrowIfNull(gameObject);
        ArgumentNullException.ThrowIfNull(mutation);
        ArgumentException.ThrowIfNullOrWhiteSpace(historyName);
        GameObject[] affected = (relatedObjects ?? Array.Empty<GameObject>())
            .Prepend(gameObject)
            .DistinctBy(static candidate => candidate.identity.persistentId)
            .ToArray();
        SceneObjectPlacement[] before = CapturePlacements(affected);
        SceneObjectPlacement[] after;
        try
        {
            mutation();
            after = CapturePlacements(affected);
        }
        catch (Exception exception)
        {
            RollbackAndRethrow(exception, () => RestorePlacements(before));
            throw;
        }
        if (before.SequenceEqual(after))
            return false;
        var data = new SceneHierarchyHistoryData(
            before,
            after,
            gameObject.identity.persistentId);
        RecordWithRollback(
            () => Record(historyName, SceneHistoryKinds.Hierarchy, data.Encode()),
            () => RestorePlacements(before));
        return true;
    }

    private void RecordDocument(
        string name,
        bool existsBefore,
        bool existsAfter,
        EditorSceneWorkspace.SceneDocumentSnapshot snapshot,
        Guid? activeBefore,
        Guid? activeAfter,
        Guid? selectedBefore,
        Guid? selectedAfter)
    {
        var data = new SceneDocumentHistoryData(
            existsBefore,
            existsAfter,
            snapshot,
            activeBefore,
            activeAfter,
            selectedBefore,
            selectedAfter);
        m_interactions.history.RecordApplied(
            name,
            new EditorHistoryChange(
                SceneHistoryKinds.Document,
                EditorHistoryPayload.FromBytes(data.Encode())));
    }

    private void RecordSubtree(
        string name,
        GameObject root,
        bool existsBefore,
        bool existsAfter,
        byte[] subtree,
        SceneIncomingReferenceState[] incoming,
        Guid? selectedBefore,
        Guid? selectedAfter)
    {
        var data = new SceneSubtreeHistoryData(
            root.scene.identity.persistentId,
            root.identity.persistentId,
            root.transform.parent?.gameObject.identity.persistentId,
            root.transform.siblingIndex,
            existsBefore,
            existsAfter,
            subtree,
            incoming,
            selectedBefore,
            selectedAfter);
        Record(name, SceneHistoryKinds.Subtree, data.Encode());
    }

    private void Record(string name, string kind, byte[] data, string? mergeKey = null)
        => m_interactions.history.RecordApplied(
            name,
            new EditorHistoryChange(
                kind,
                EditorHistoryPayload.FromBytes(data),
                mergeKey));

    private void RecordElement(string name, SceneElementHistoryData data)
        => Record(name, SceneHistoryKinds.Element, data.Encode());

    private void ChangeScalar(
        EngineObject target,
        SceneScalarKind scalarKind,
        string before,
        string after,
        Action<string> setter,
        string historyName,
        string? mergeKey)
    {
        if (string.Equals(before, after, StringComparison.Ordinal))
            return;
        RecordWithRollback(
            () =>
            {
                setter(after);
                SceneScalarHistoryData data = SceneScalarHistoryData.Create(
                    target.identity.persistentId,
                    scalarKind,
                    before,
                    after);
                Record(historyName, SceneHistoryKinds.Scalar, data.Encode(), mergeKey);
            },
            () => setter(before));
    }

    private Guid? GetActiveSceneId()
        => SceneManager.activeScene is { isDestroyed: false } scene
            ? scene.identity.persistentId
            : null;

    private Guid? GetSelectionId()
        => m_interactions.selection.selectedTarget is EngineObject { isDestroyed: false } target
            ? target.identity.persistentId
            : null;

    private static SceneObjectPlacement[] CapturePlacements(IReadOnlyList<GameObject> gameObjects)
    {
        var placements = new SceneObjectPlacement[gameObjects.Count];
        for (int i = 0; i < gameObjects.Count; i++)
        {
            GameObject gameObject = gameObjects[i];
            placements[i] = new SceneObjectPlacement(
                gameObject.scene.identity.persistentId,
                gameObject.identity.persistentId,
                gameObject.transform.parent?.gameObject.identity.persistentId,
                gameObject.transform.siblingIndex);
        }
        return placements;
    }

    private static void RestorePlacements(IReadOnlyList<SceneObjectPlacement> placements)
    {
        for (int i = 0; i < placements.Count; i++)
        {
            SceneObjectPlacement placement = placements[i];
            GameObject gameObject = Inno.Core.Identity.IdentityManager.Get<GameObject>(placement.objectId)
                ?? throw new InvalidOperationException($"GameObject '{placement.objectId}' is unavailable.");
            GameScene destination = Inno.Core.Identity.IdentityManager.Get<GameScene>(placement.sceneId)
                ?? throw new InvalidOperationException($"Scene '{placement.sceneId}' is unavailable.");
            if (!ReferenceEquals(gameObject.scene, destination))
                SceneManager.MoveGameObjectToScene(gameObject, destination);
        }
        for (int i = 0; i < placements.Count; i++)
        {
            SceneObjectPlacement placement = placements[i];
            GameObject gameObject = Inno.Core.Identity.IdentityManager.Get<GameObject>(placement.objectId)!;
            Transform? parent = placement.parentId is Guid parentId
                ? Inno.Core.Identity.IdentityManager.Get<GameObject>(parentId)?.transform
                : null;
            gameObject.transform.SetParent(parent);
        }
        foreach (SceneObjectPlacement placement in placements.OrderBy(static value => value.siblingIndex))
        {
            Inno.Core.Identity.IdentityManager.Get<GameObject>(placement.objectId)!
                .transform.SetSiblingIndex(placement.siblingIndex);
        }
    }

    private static void RequirePropertyRestore(EngineObject target, ReadOnlySpan<byte> data)
    {
        SerializationPropertyRestoreResult result = ScenePropertySerialization.RestoreProperties(target, data);
        if (!result.success || result.ignoredCount != 0 || result.restoredCount == 0)
            throw new InvalidOperationException("Scene property compensation was incomplete.");
    }

    private static void RequireReferenceRestore(SceneReferenceRestoreResult result)
    {
        if (!result.succeeded)
            throw new InvalidOperationException(result.message);
    }

    private static void RecordWithRollback(Action record, Action rollback)
    {
        try
        {
            record();
        }
        catch (Exception exception)
        {
            RollbackAndRethrow(exception, rollback);
        }
    }

    private static void RollbackAndRethrow(Exception failure, Action rollback)
    {
        try
        {
            rollback();
        }
        catch (Exception rollbackException)
        {
            throw new AggregateException(
                "An editor mutation could not be recorded and its compensation also failed.",
                failure,
                rollbackException);
        }
        ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static Guid GetStableTypeId(Type type)
        => TypeCacheManager.TryGetStableTypeId(type, out Guid stableTypeId)
            ? stableTypeId
            : throw new InvalidOperationException(
                $"Scene element type '{type.FullName}' does not have an active StableTypeId.");
}
