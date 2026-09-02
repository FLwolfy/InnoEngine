using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;

using Inno.Extensibility.Types;

namespace Inno.Scene;

internal sealed class SceneTypeCatalog : IDisposable
{
    private readonly TypeCatalog m_types;
    private readonly SceneTypeRegistry m_registry;

    internal SceneTypeCatalog(TypeCatalog types)
    {
        ArgumentNullException.ThrowIfNull(types);
        m_types = types;
        m_registry = new SceneTypeRegistry(types);
    }

    internal bool TryGetComponent(Type type, out SceneComponentTypeDescriptor? descriptor)
    {
        ArgumentNullException.ThrowIfNull(type);
        descriptor = null;
        return m_types.TryGetTypeRef(type, out TypeRef typeRef) &&
               m_registry.snapshot.componentsByRuntimeId.TryGetValue(typeRef.runtimeId, out descriptor);
    }

    internal SceneComponentTypeDescriptor GetComponent(Type type)
    {
        if (TryGetComponent(type, out SceneComponentTypeDescriptor? descriptor))
            return descriptor!;
        throw new InvalidOperationException(
            $"GameComponent type '{type.FullName}' is not part of the active TypeCache generation.");
    }

    internal SceneComponentTypeDescriptor GetComponent(int runtimeTypeId)
    {
        if (m_registry.snapshot.componentsByRuntimeId.TryGetValue(
                runtimeTypeId,
                out SceneComponentTypeDescriptor? descriptor))
        {
            return descriptor;
        }
        throw new InvalidOperationException(
            $"Component runtime type ID '{runtimeTypeId}' is not part of the active TypeCache generation.");
    }

    internal bool TryGetSystem(Type type, out SceneSystemTypeDescriptor? descriptor)
    {
        ArgumentNullException.ThrowIfNull(type);
        descriptor = null;
        return m_types.TryGetTypeRef(type, out TypeRef typeRef) &&
               m_registry.snapshot.systemsByRuntimeId.TryGetValue(typeRef.runtimeId, out descriptor);
    }

    internal SceneSystemTypeDescriptor GetSystem(Type type)
    {
        if (TryGetSystem(type, out SceneSystemTypeDescriptor? descriptor))
            return descriptor!;
        throw new InvalidOperationException(
            $"GameSystem type '{type.FullName}' is not part of the active TypeCache generation.");
    }

    internal Type Resolve(TypeRef typeRef) => m_types.Resolve(typeRef);

    /// <summary>
    /// Releases the resources owned by this instance.
    /// </summary>
    public void Dispose() => m_registry.Dispose();

    private sealed class SceneTypeRegistry : TypeRegistry<SceneTypeSnapshot>
    {
        internal SceneTypeRegistry(TypeCatalog types)
            : base(types)
        {
        }

        internal SceneTypeSnapshot snapshot => current;

        /// <summary>
        /// Builds a validated result from the current immutable input snapshot.
        /// </summary>
        /// <param name="types">
        /// The active type catalog generation used for extension resolution.
        /// </param>
        /// <returns>
        /// The validated scene type snapshot that represents the completed operation.
        /// </returns>
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

        /// <summary>
        /// Validates and prepares a candidate snapshot before it can become active.
        /// </summary>
        /// <param name="previous">
        /// The previous consumed by on activating; ownership remains with the caller unless explicitly stated otherwise.
        /// </param>
        /// <param name="candidate">
        /// The candidate consumed by on activating; ownership remains with the caller unless explicitly stated otherwise.
        /// </param>
        protected override void OnActivating(SceneTypeSnapshot? previous, SceneTypeSnapshot candidate)
            => SceneStore.InvalidateAllTypeCaches();

        /// <summary>
        /// Restores state retained for the previous snapshot after candidate activation fails.
        /// </summary>
        /// <param name="previous">
        /// The previous consumed by on activation rolled back; ownership remains with the caller unless explicitly stated otherwise.
        /// </param>
        /// <param name="candidate">
        /// The candidate consumed by on activation rolled back; ownership remains with the caller unless explicitly stated otherwise.
        /// </param>
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
