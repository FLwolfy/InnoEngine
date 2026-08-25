using System;
using System.IO;
using System.Runtime.ExceptionServices;

using Inno.Core.Identity;
using Inno.Core.Reflection;
using Inno.Core.Serialization;
using Inno.Engine.Scene;

namespace Inno.Engine.Scene.Assets;

/// <summary>
/// Recreates individual scene components and systems from stable type and object identities.
/// </summary>
public static class SceneElementSerialization
{
    /// <summary>
    /// Recreates one component without invoking Reset and restores its persistent properties.
    /// </summary>
    /// <param name="owner">The live GameObject that will own the component.</param>
    /// <param name="stableTypeId">The stable identity of the current component implementation.</param>
    /// <param name="persistentId">The component instance identity to preserve.</param>
    /// <param name="componentIndex">The requested attachment index.</param>
    /// <param name="propertyData">Neutral property bytes captured from the previous instance.</param>
    /// <returns>The recreated component.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="owner"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidDataException">Thrown when the stable type is missing or is not a concrete component.</exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the identity or component multiplicity conflicts, property restoration is incomplete,
    /// or a failed restoration cannot remove its partially created component.
    /// </exception>
    public static GameComponent RestoreComponent(
        GameObject owner,
        Guid stableTypeId,
        Guid persistentId,
        int componentIndex,
        ReadOnlySpan<byte> propertyData)
    {
        ArgumentNullException.ThrowIfNull(owner);
        Type componentType = ResolveType<GameComponent>(stableTypeId, "component");
        GameComponent component = owner.scene.AddComponent(
            owner,
            componentType,
            persistentId,
            invokeReset: false);
        try
        {
            owner.SetComponentIndex(component, componentIndex);
            RequireComplete(
                ScenePropertySerialization.RestoreProperties(component, propertyData),
                "component");
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
    /// <param name="scene">The live loaded scene that will own the system.</param>
    /// <param name="stableTypeId">The stable identity of the current system implementation.</param>
    /// <param name="persistentId">The system instance identity to preserve.</param>
    /// <param name="systemIndex">The requested display and serialization index.</param>
    /// <param name="propertyData">Neutral property bytes captured from the previous instance.</param>
    /// <returns>The recreated system.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="scene"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidDataException">Thrown when the stable type is missing or is not a concrete system.</exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the identity or system multiplicity conflicts, property restoration is incomplete,
    /// or a failed restoration cannot remove its partially created system.
    /// </exception>
    public static GameSystem RestoreSystem(
        GameScene scene,
        Guid stableTypeId,
        Guid persistentId,
        int systemIndex,
        ReadOnlySpan<byte> propertyData)
    {
        ArgumentNullException.ThrowIfNull(scene);
        Type systemType = ResolveType<GameSystem>(stableTypeId, "system");
        GameSystem system = scene.AddSystem(systemType, persistentId, invokeReset: false);
        try
        {
            scene.SetSystemIndex(system, systemIndex);
            RequireComplete(
                ScenePropertySerialization.RestoreProperties(system, propertyData),
                "system");
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
            IdentityManager.Get<EngineObject>(persistentId),
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

    private static Type ResolveType<TElement>(Guid stableTypeId, string kind)
        where TElement : EngineObject
    {
        if (!TypeCacheManager.TryResolveType(stableTypeId, out Type? type) || type is null)
            throw new SceneTypeResolutionException(stableTypeId, kind);
        if (!typeof(TElement).IsAssignableFrom(type) || type.IsAbstract)
        {
            throw new InvalidDataException(
                $"Stable type id '{stableTypeId}' resolves to invalid scene {kind} '{type.FullName}'.");
        }
        return type;
    }
}
