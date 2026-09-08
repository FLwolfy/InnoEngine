using System;
using System.IO;
using System.Runtime.ExceptionServices;

using Inno.Assets;
using Inno.Core.Identity;
using Inno.Extensibility.Types;
using Inno.Core.Serialization;
using Inno.Scene;

namespace Inno.Scene;

/// <summary>
/// Recreates individual scene components and systems from stable type and object identities.
/// </summary>
public static class SceneElementSerialization
{
    /// <summary>
    /// Captures one element's logical type, neutral properties, asset dependencies and scene-reference aliases.
    /// </summary>
    /// <param name="target">
    /// The live component or system, including a missing placeholder.
    /// </param>
    /// <param name="serialization">
    /// The owner's current serializer generation.
    /// </param>
    /// <param name="assets">
    /// The owner's complete asset resolver used during capture.
    /// </param>
    /// <returns>
    /// Neutral element bytes suitable for History and restoration without retaining the current managed type.
    /// </returns>
    /// <exception cref="ArgumentException">
    /// The target is not a component or system.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The element is not live in a loaded scene.
    /// </exception>
    public static byte[] CaptureState(EngineObject target, SerializationRegistry serialization, IAssetReferenceResolver assets)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(serialization);
        ArgumentNullException.ThrowIfNull(assets);
        GameScene scene = ScenePropertySerialization.ResolveScene(target);
        var state = new SceneElementState();
        if (target is MissingGameComponent component)
        {
            state.typeId = component.missingType.stableId;
            state.typeName = component.missingTypeName;
            state.properties = component.CaptureSerializedState();
            state.SetDependencies(component.dependencies);
            state.SetAliases(component.referenceAliases);
        }
        else if (target is MissingGameSystem system)
        {
            state.typeId = system.missingType.stableId;
            state.typeName = system.missingTypeName;
            state.properties = system.CaptureSerializedState();
            state.SetDependencies(system.dependencies);
            state.SetAliases(system.referenceAliases);
        }
        else
        {
            if (target is not GameComponent and not GameSystem)
                throw new ArgumentException("Element state requires a component or system.", nameof(target));
            state.typeId = scene.typeCatalog.GetTypeRef(target.GetType()).stableId;
            state.typeName = target.GetType().FullName ?? target.GetType().Name;
            var dependencies = new AssetDependencyCollection();
            using (ScenePropertySerialization.CreateReferences(scene).Enter())
                state.properties = serialization.CapturePropertiesData((ISerializable)target,
                    AssetSerializationContext.Create(assets, dependencies));
            state.SetDependencies(dependencies.dependencies);
        }
        return serialization.Serialize(state, AssetSerializationContext.Create(assets));
    }

    /// <summary>
    /// Restores a captured element state into the same logical type, including neutral state on a missing placeholder.
    /// </summary>
    /// <param name="target">
    /// The attached element receiving the state.
    /// </param>
    /// <param name="stateData">
    /// Bytes produced by <see cref="CaptureState"/>.
    /// </param>
    /// <param name="serialization">
    /// The owner's current serializer generation.
    /// </param>
    /// <param name="assets">
    /// The owner's complete asset resolver.
    /// </param>
    /// <exception cref="InvalidDataException">
    /// The payload is invalid or represents a different logical type.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Property or reference restoration is incomplete.
    /// </exception>
    public static void RestoreState(EngineObject target, ReadOnlySpan<byte> stateData,
        SerializationRegistry serialization, IAssetReferenceResolver assets)
    {
        ArgumentNullException.ThrowIfNull(target);
        SceneElementState state = ReadState(stateData, serialization, assets);
        GameScene scene = ScenePropertySerialization.ResolveScene(target);
        Guid logicalType = target switch
        {
            MissingGameComponent missing => missing.missingType.stableId,
            MissingGameSystem missing => missing.missingType.stableId,
            _ => scene.typeCatalog.GetTypeRef(target.GetType()).stableId
        };
        RequireType(state, logicalType);
        if (target is MissingGameComponent component)
            component.RestoreState(state);
        else if (target is MissingGameSystem system)
            system.RestoreState(state);
        else
        {
            using (ScenePropertySerialization.CreateReferences(scene, state.GetAliases()).Enter())
                RequireComplete(serialization.RestorePropertiesData((ISerializable)target, state.properties,
                    SerializationPropertyRestoreMode.Strict, AssetSerializationContext.Create(assets)), "element");
        }
    }

    /// <summary>
    /// Recreates one component without invoking Reset and restores its persistent properties.
    /// </summary>
    /// <param name="owner">
    /// The live GameObject that will own the component.
    /// </param>
    /// <param name="type">
    /// The reload-safe identity of the current component implementation.
    /// </param>
    /// <param name="persistentId">
    /// The component instance identity to preserve.
    /// </param>
    /// <param name="componentIndex">
    /// The requested attachment index.
    /// </param>
    /// <param name="stateData">
    /// Complete neutral element bytes produced by CaptureState, including missing-state metadata.
    /// </param>
    /// <param name="serialization">
    /// The serialization registry that owns the active converter generation.
    /// </param>
    /// <param name="assets">
    /// The resolver used to materialize canonical asset references in restored properties.
    /// </param>
    /// <returns>
    /// The recreated component, or a preserved missing placeholder when its logical type is unavailable.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="owner"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="InvalidDataException">
    /// Thrown when the payload is invalid or the stable type resolves to an incompatible component.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the identity or component multiplicity conflicts, property restoration is incomplete,
    /// or a failed restoration cannot remove its partially created component.
    /// </exception>
    public static GameComponent RestoreComponent(
        GameObject owner,
        TypeRef type,
        Guid persistentId,
        int componentIndex,
        ReadOnlySpan<byte> stateData,
        SerializationRegistry serialization,
        IAssetReferenceResolver assets)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(serialization);
        ArgumentNullException.ThrowIfNull(assets);
        SceneElementState state = ReadState(stateData, serialization, assets);
        RequireType(state, type.stableId);
        Type? componentType = ResolveType<GameComponent>(owner.scene.typeCatalog, type, "component");
        GameComponent component = componentType is null
            ? owner.scene.AddMissingComponent(owner, new TypeRef(state.typeId), state.typeName,
                state.properties, persistentId, state.GetDependencies())
            : owner.scene.AddComponent(owner, componentType, persistentId, invokeReset: false);
        try
        {
            owner.SetComponentIndex(component, componentIndex);
            RestoreState(component, stateData, serialization, assets);
            return component;
        }
        catch (Exception exception)
        {
            RethrowAfterCleanup(
                exception,
                component,
                () => owner.RemoveComponent(component),
                "component");
            throw;
        }
    }

    /// <summary>
    /// Recreates one scene system without invoking Reset and restores its persistent properties.
    /// </summary>
    /// <param name="scene">
    /// The live loaded scene that will own the system.
    /// </param>
    /// <param name="type">
    /// The reload-safe identity of the current system implementation.
    /// </param>
    /// <param name="persistentId">
    /// The system instance identity to preserve.
    /// </param>
    /// <param name="systemIndex">
    /// The requested display and serialization index.
    /// </param>
    /// <param name="stateData">
    /// Complete neutral element bytes produced by CaptureState, including missing-state metadata.
    /// </param>
    /// <param name="serialization">
    /// The serialization registry that owns the active converter generation.
    /// </param>
    /// <param name="assets">
    /// The resolver used to materialize canonical asset references in restored properties.
    /// </param>
    /// <returns>
    /// The recreated system, or a preserved missing placeholder when its logical type is unavailable.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="scene"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="InvalidDataException">
    /// Thrown when the payload is invalid or the stable type resolves to an incompatible system.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the identity or system multiplicity conflicts, property restoration is incomplete,
    /// or a failed restoration cannot remove its partially created system.
    /// </exception>
    public static GameSystem RestoreSystem(
        GameScene scene,
        TypeRef type,
        Guid persistentId,
        int systemIndex,
        ReadOnlySpan<byte> stateData,
        SerializationRegistry serialization,
        IAssetReferenceResolver assets)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(serialization);
        ArgumentNullException.ThrowIfNull(assets);
        SceneElementState state = ReadState(stateData, serialization, assets);
        RequireType(state, type.stableId);
        Type? systemType = ResolveType<GameSystem>(scene.typeCatalog, type, "system");
        GameSystem system = systemType is null
            ? scene.AddMissingSystem(new TypeRef(state.typeId), state.typeName, state.properties, persistentId, state.GetDependencies())
            : scene.AddSystem(systemType, persistentId, invokeReset: false);
        try
        {
            scene.SetSystemIndex(system, systemIndex);
            RestoreState(system, stateData, serialization, assets);
            return system;
        }
        catch (Exception exception)
        {
            RethrowAfterCleanup(
                exception,
                system,
                () => scene.RemoveSystem(system),
                "system");
            throw;
        }
    }

    private static void RequireComplete(SerializationPropertyRestoreResult result, string kind)
    {
        if (result.success && result.ignoredCount == 0)
            return;
        throw new InvalidOperationException(
            $"Scene {kind} property restoration was incomplete: " +
            $"{result.restoredCount} restored, {result.ignoredCount} ignored, " +
            $"{result.failures.Count} failed.");
    }

    internal static void RethrowAfterCleanup(
        Exception restoreFailure,
        EngineObject element,
        Func<bool> remove,
        string kind)
    {
        Guid persistentId = element.identity.persistentId;
        Exception? cleanupFailure = null;
        bool reportedRemoved = false;
        if (!element.isDestroyed)
        {
            try
            {
                reportedRemoved = remove();
            }
            catch (Exception exception)
            {
                cleanupFailure = exception;
            }
        }
        bool remainsRegistered = ReferenceEquals(
            IdentityAllocator.current.Get<EngineObject>(persistentId),
            element);
        if ((!element.isDestroyed || remainsRegistered) && cleanupFailure is null)
        {
            cleanupFailure = new InvalidOperationException(
                reportedRemoved
                    ? $"The partially restored scene {kind} reported successful removal but did not reach the destroyed and unregistered postcondition."
                    : $"The partially restored scene {kind} could not be fully destroyed and unregistered.");
        }

        if (cleanupFailure is null)
            ExceptionDispatchInfo.Capture(restoreFailure).Throw();
        throw new InvalidOperationException(
            $"Scene {kind} restoration failed and its cleanup did not complete successfully.",
            new AggregateException(restoreFailure, cleanupFailure));
    }

    private static SceneElementState ReadState(ReadOnlySpan<byte> bytes,
        SerializationRegistry serialization, IAssetReferenceResolver assets)
    {
        ArgumentNullException.ThrowIfNull(serialization);
        ArgumentNullException.ThrowIfNull(assets);
        SceneElementState state = serialization.Deserialize<SceneElementState>(bytes, AssetSerializationContext.Create(assets))
            ?? throw new InvalidDataException("Scene element state is null.");
        state.Validate();
        return state;
    }

    private static void RequireType(SceneElementState state, Guid typeId)
    {
        if (state.typeId != typeId)
            throw new InvalidDataException($"Element state type '{state.typeId}' does not match logical type '{typeId}'.");
    }

    private static Type? ResolveType<TElement>(
        SceneTypeCatalog types,
        TypeRef typeRef,
        string kind)
        where TElement : EngineObject
    {
        Type type;
        try
        {
            type = types.Resolve(typeRef);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
        if (!typeof(TElement).IsAssignableFrom(type) || type.IsAbstract)
        {
            throw new InvalidDataException(
                $"Stable type id '{typeRef.stableId}' resolves to invalid scene {kind} '{type.FullName}'.");
        }
        return type;
    }
}
