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
               S_REGISTRY.snapshot.components.TryGetValue(typeRef, out descriptor);
    }

    internal static SceneComponentTypeDescriptor GetComponent(Type type)
    {
        if (TryGetComponent(type, out SceneComponentTypeDescriptor? descriptor))
            return descriptor!;
        throw new InvalidOperationException(
            $"GameComponent type '{type.FullName}' is not part of the active TypeCache generation.");
    }

    internal static bool TryGetComponent(TypeRef typeRef, out SceneComponentTypeDescriptor? descriptor)
        => S_REGISTRY.snapshot.components.TryGetValue(typeRef, out descriptor);

    internal static SceneComponentTypeDescriptor GetComponent(TypeRef typeRef)
    {
        if (TryGetComponent(typeRef, out SceneComponentTypeDescriptor? descriptor))
            return descriptor!;
        throw new InvalidOperationException(
            $"Component type reference '{typeRef}' is not part of the active TypeCache generation.");
    }

    internal static bool TryGetSystem(Type type, out SceneSystemTypeDescriptor? descriptor)
    {
        ArgumentNullException.ThrowIfNull(type);
        descriptor = null;
        return TypeCacheManager.TryGetTypeRef(type, out TypeRef typeRef) &&
               S_REGISTRY.snapshot.systems.TryGetValue(typeRef, out descriptor);
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

            var components = new Dictionary<TypeRef, SceneComponentTypeDescriptor>(componentTypes.Length);
            foreach (Type componentType in componentTypes)
            {
                TypeRef componentTypeRef = types.GetTypeRef(componentType);
                TypeRef[] assignableConcreteTypes = concreteComponentTypes
                    .Where(componentType.IsAssignableFrom)
                    .OrderBy(static type => type.FullName, StringComparer.Ordinal)
                    .Select(types.GetTypeRef)
                    .ToArray();
                components.Add(componentTypeRef, new SceneComponentTypeDescriptor(
                    componentTypeRef,
                    componentType.FullName ?? componentType.Name,
                    componentType.IsClass && !componentType.IsAbstract,
                    componentType.IsDefined(typeof(AllowMultipleComponentAttribute), inherit: true),
                    assignableConcreteTypes,
                    assignableConcreteTypes.ToFrozenSet()));
            }

            Type[] systemTypes = types.types
                .Select(typeRef => typeRef.Resolve(types))
                .Where(static type => typeof(GameSystem).IsAssignableFrom(type))
                .ToArray();
            var systems = new Dictionary<TypeRef, SceneSystemTypeDescriptor>(systemTypes.Length);
            foreach (Type systemType in systemTypes)
            {
                TypeRef systemTypeRef = types.GetTypeRef(systemType);
                systems.Add(systemTypeRef, new SceneSystemTypeDescriptor(
                    systemTypeRef,
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

    }
}

internal sealed record SceneTypeSnapshot(
    long generation,
    FrozenDictionary<TypeRef, SceneComponentTypeDescriptor> components,
    FrozenDictionary<TypeRef, SceneSystemTypeDescriptor> systems);

internal sealed record SceneComponentTypeDescriptor(
    TypeRef typeRef,
    string displayName,
    bool isConcrete,
    bool allowsMultiple,
    TypeRef[] assignableConcreteTypes,
    FrozenSet<TypeRef> assignableConcreteTypeSet)
{
    internal bool IsAssignableFrom(TypeRef concreteType)
        => assignableConcreteTypeSet.Contains(concreteType);
}

internal sealed record SceneSystemTypeDescriptor(
    TypeRef typeRef,
    string displayName,
    bool isConcrete,
    bool allowsMultiple);
