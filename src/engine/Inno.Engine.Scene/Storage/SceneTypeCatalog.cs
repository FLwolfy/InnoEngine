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
        return TypeCacheManager.TryGetTypeRef(type, out TypeRef typeRef) &&
               S_REGISTRY.snapshot.componentsByRuntimeId.TryGetValue(typeRef.runtimeId, out descriptor);
    }

    internal static SceneComponentTypeDescriptor GetComponent(Type type)
    {
        if (TryGetComponent(type, out SceneComponentTypeDescriptor? descriptor))
            return descriptor!;
        throw new InvalidOperationException(
            $"GameComponent type '{type.FullName}' is not part of the active TypeCache generation.");
    }

    internal static SceneComponentTypeDescriptor GetComponent(int runtimeTypeId)
    {
        if (S_REGISTRY.snapshot.componentsByRuntimeId.TryGetValue(
                runtimeTypeId,
                out SceneComponentTypeDescriptor? descriptor))
        {
            return descriptor;
        }
        throw new InvalidOperationException(
            $"Component runtime type ID '{runtimeTypeId}' is not part of the active TypeCache generation.");
    }

    internal static bool TryGetSystem(Type type, out SceneSystemTypeDescriptor? descriptor)
    {
        ArgumentNullException.ThrowIfNull(type);
        descriptor = null;
        return TypeCacheManager.TryGetTypeRef(type, out TypeRef typeRef) &&
               S_REGISTRY.snapshot.systemsByRuntimeId.TryGetValue(typeRef.runtimeId, out descriptor);
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
                .Select(typeRef => typeRef.Resolve(types))
                .Where(static type => typeof(GameComponent).IsAssignableFrom(type))
                .ToArray();
            Type[] concreteComponentTypes = componentTypes
                .Where(static type => type.IsClass && !type.IsAbstract)
                .ToArray();

            var componentsByRuntimeId = new Dictionary<int, SceneComponentTypeDescriptor>(componentTypes.Length);
            foreach (Type componentType in componentTypes)
            {
                TypeRef componentTypeRef = types.GetTypeRef(componentType);
                int[] assignableConcreteRuntimeTypeIds = concreteComponentTypes
                    .Where(componentType.IsAssignableFrom)
                    .OrderBy(static type => type.FullName, StringComparer.Ordinal)
                    .Select(types.GetTypeRef)
                    .Select(static typeRef => typeRef.runtimeId)
                    .ToArray();
                var descriptor = new SceneComponentTypeDescriptor(
                    componentTypeRef.runtimeId,
                    componentType.FullName ?? componentType.Name,
                    componentType.IsClass && !componentType.IsAbstract,
                    componentType.IsDefined(typeof(AllowMultipleComponentAttribute), inherit: true),
                    assignableConcreteRuntimeTypeIds,
                    assignableConcreteRuntimeTypeIds.ToFrozenSet());
                componentsByRuntimeId.Add(descriptor.runtimeTypeId, descriptor);
            }

            Type[] systemTypes = types.types
                .Select(typeRef => typeRef.Resolve(types))
                .Where(static type => typeof(GameSystem).IsAssignableFrom(type))
                .ToArray();
            var systemsByRuntimeId = new Dictionary<int, SceneSystemTypeDescriptor>(systemTypes.Length);
            foreach (Type systemType in systemTypes)
            {
                TypeRef systemTypeRef = types.GetTypeRef(systemType);
                var descriptor = new SceneSystemTypeDescriptor(
                    systemTypeRef.runtimeId,
                    systemType.FullName ?? systemType.Name,
                    systemType.IsClass && !systemType.IsAbstract,
                    systemType.IsDefined(typeof(AllowMultipleSystemAttribute), inherit: false));
                systemsByRuntimeId.Add(descriptor.runtimeTypeId, descriptor);
            }

            return new SceneTypeSnapshot(
                types.version,
                componentsByRuntimeId.ToFrozenDictionary(),
                systemsByRuntimeId.ToFrozenDictionary());
        }

        protected override void OnActivating(SceneTypeSnapshot? previous, SceneTypeSnapshot candidate)
            => SceneStore.InvalidateAllTypeCaches();

        protected override void OnActivationRolledBack(SceneTypeSnapshot? previous, SceneTypeSnapshot candidate)
            => SceneStore.InvalidateAllTypeCaches();

    }
}

internal sealed record SceneTypeSnapshot(
    long generation,
    FrozenDictionary<int, SceneComponentTypeDescriptor> componentsByRuntimeId,
    FrozenDictionary<int, SceneSystemTypeDescriptor> systemsByRuntimeId);

internal sealed record SceneComponentTypeDescriptor(
    int runtimeTypeId,
    string displayName,
    bool isConcrete,
    bool allowsMultiple,
    int[] assignableConcreteRuntimeTypeIds,
    FrozenSet<int> assignableConcreteRuntimeTypeIdSet)
{
    internal bool IsAssignableFrom(int concreteRuntimeTypeId)
        => assignableConcreteRuntimeTypeIdSet.Contains(concreteRuntimeTypeId);
}

internal sealed record SceneSystemTypeDescriptor(
    int runtimeTypeId,
    string displayName,
    bool isConcrete,
    bool allowsMultiple);
