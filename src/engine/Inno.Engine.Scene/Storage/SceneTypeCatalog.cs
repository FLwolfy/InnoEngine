using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;

using Inno.Core.Reflection;

namespace Inno.Engine.Scene;

internal static class SceneTypeCatalog
{
    private static readonly SceneTypeRegistry S_REGISTRY = new();

    internal static void EnsureRegistered()
    {
        _ = S_REGISTRY;
    }

    internal static bool TryGetComponent(Type type, out SceneComponentTypeDescriptor? descriptor)
    {
        ArgumentNullException.ThrowIfNull(type);
        descriptor = null;
        return TypeCacheManager.TryGetRuntimeTypeId(type, out int runtimeTypeId) &&
               S_REGISTRY.snapshot.components.TryGetValue(runtimeTypeId, out descriptor);
    }

    internal static SceneComponentTypeDescriptor GetComponent(Type type)
    {
        if (TryGetComponent(type, out SceneComponentTypeDescriptor? descriptor))
            return descriptor!;
        throw new InvalidOperationException(
            $"GameComponent type '{type.FullName}' is not part of the active TypeCache generation.");
    }

    internal static bool TryGetComponent(int runtimeTypeId, out SceneComponentTypeDescriptor? descriptor)
        => S_REGISTRY.snapshot.components.TryGetValue(runtimeTypeId, out descriptor);

    internal static SceneComponentTypeDescriptor GetComponent(int runtimeTypeId)
    {
        if (TryGetComponent(runtimeTypeId, out SceneComponentTypeDescriptor? descriptor))
            return descriptor!;
        throw new InvalidOperationException(
            $"Runtime component type id '{runtimeTypeId}' is not part of the active TypeCache generation.");
    }

    internal static bool TryGetSystem(Type type, out SceneSystemTypeDescriptor? descriptor)
    {
        ArgumentNullException.ThrowIfNull(type);
        descriptor = null;
        return TypeCacheManager.TryGetRuntimeTypeId(type, out int runtimeTypeId) &&
               S_REGISTRY.snapshot.systems.TryGetValue(runtimeTypeId, out descriptor);
    }

    internal static SceneSystemTypeDescriptor GetSystem(Type type)
    {
        if (TryGetSystem(type, out SceneSystemTypeDescriptor? descriptor))
            return descriptor!;
        throw new InvalidOperationException(
            $"GameSystem type '{type.FullName}' is not part of the active TypeCache generation.");
    }

    private sealed class SceneTypeRegistry : TypeRegistry<SceneTypeSnapshot>
    {
        internal SceneTypeSnapshot snapshot => current;

        protected override SceneTypeSnapshot Build(TypeCacheSnapshot types)
        {
            Type[] componentTypes = types.types
                .Where(static type => typeof(GameComponent).IsAssignableFrom(type))
                .ToArray();
            Type[] concreteComponentTypes = componentTypes
                .Where(static type => type.IsClass && !type.IsAbstract)
                .ToArray();

            var components = new Dictionary<int, SceneComponentTypeDescriptor>(componentTypes.Length);
            foreach (Type componentType in componentTypes)
            {
                int runtimeTypeId = GetRuntimeTypeId(types, componentType);
                int[] assignableConcreteTypeIds = concreteComponentTypes
                    .Where(componentType.IsAssignableFrom)
                    .OrderBy(static type => type.FullName, StringComparer.Ordinal)
                    .Select(type => GetRuntimeTypeId(types, type))
                    .ToArray();
                components.Add(runtimeTypeId, new SceneComponentTypeDescriptor(
                    runtimeTypeId,
                    GetStableTypeId(types, componentType),
                    componentType.FullName ?? componentType.Name,
                    componentType.IsClass && !componentType.IsAbstract,
                    componentType.IsDefined(typeof(AllowMultipleComponentAttribute), inherit: true),
                    assignableConcreteTypeIds,
                    assignableConcreteTypeIds.ToFrozenSet()));
            }

            Type[] systemTypes = types.types
                .Where(static type => typeof(GameSystem).IsAssignableFrom(type))
                .ToArray();
            var systems = new Dictionary<int, SceneSystemTypeDescriptor>(systemTypes.Length);
            foreach (Type systemType in systemTypes)
            {
                int runtimeTypeId = GetRuntimeTypeId(types, systemType);
                systems.Add(runtimeTypeId, new SceneSystemTypeDescriptor(
                    runtimeTypeId,
                    GetStableTypeId(types, systemType),
                    systemType.FullName ?? systemType.Name,
                    systemType.IsClass && !systemType.IsAbstract,
                    systemType.IsDefined(typeof(AllowMultipleSystemAttribute), inherit: false)));
            }

            return new SceneTypeSnapshot(
                types.version,
                components.ToFrozenDictionary(),
                systems.ToFrozenDictionary());
        }

        protected override void OnActivating(SceneTypeSnapshot? previous, SceneTypeSnapshot candidate)
            => SceneStore.InvalidateAllTypeCaches();

        protected override void OnActivationRolledBack(SceneTypeSnapshot? previous, SceneTypeSnapshot candidate)
            => SceneStore.InvalidateAllTypeCaches();

        private static int GetRuntimeTypeId(TypeCacheSnapshot types, Type type)
            => types.TryGetRuntimeTypeId(type, out int runtimeTypeId)
                ? runtimeTypeId
                : throw new InvalidOperationException(
                    $"Type '{type.FullName}' does not have a runtime identity in TypeCache generation '{types.version}'.");

        private static Guid GetStableTypeId(TypeCacheSnapshot types, Type type)
            => types.TryGetStableTypeId(type, out Guid stableTypeId)
                ? stableTypeId
                : throw new InvalidOperationException(
                    $"Type '{type.FullName}' does not have a stable identity in TypeCache generation '{types.version}'.");
    }
}

internal sealed record SceneTypeSnapshot(
    long generation,
    FrozenDictionary<int, SceneComponentTypeDescriptor> components,
    FrozenDictionary<int, SceneSystemTypeDescriptor> systems);

internal sealed record SceneComponentTypeDescriptor(
    int runtimeTypeId,
    Guid stableTypeId,
    string displayName,
    bool isConcrete,
    bool allowsMultiple,
    int[] assignableConcreteTypeIds,
    FrozenSet<int> assignableConcreteTypeIdSet)
{
    internal bool IsAssignableFrom(int concreteRuntimeTypeId)
        => assignableConcreteTypeIdSet.Contains(concreteRuntimeTypeId);
}

internal sealed record SceneSystemTypeDescriptor(
    int runtimeTypeId,
    Guid stableTypeId,
    string displayName,
    bool isConcrete,
    bool allowsMultiple);
