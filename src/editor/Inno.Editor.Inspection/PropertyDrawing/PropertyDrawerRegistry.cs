using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using Inno.Extensibility.Types;
using Inno.Core.Serialization;
using Inno.Editor.Interactions;

namespace Inno.Editor.Inspection;

/// <summary>
/// Discovers and resolves serialized property drawers through the active type catalog.
/// </summary>
public sealed class PropertyDrawerRegistry : IDisposable
{
    private readonly PropertyTypeRegistry m_registry;

    /// <summary>
    /// Creates a generation-aware property drawer registry.
    /// </summary>
    /// <param name="interactions">
    /// The active editor interaction entry point available to property drawer constructors.
    /// </param>
    /// <param name="types">
    /// The host-owned type catalog that coordinates drawer generations.
    /// </param>
    /// <param name="serialization">
    /// The serialization registry available to structured-object drawers.
    /// </param>
    /// <param name="drawerServices">
    /// Additional generation-bound services available to feature-specific drawer constructors.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="interactions"/> is <see langword="null"/>.
    /// </exception>
    public PropertyDrawerRegistry(
        EditorInteractions interactions,
        TypeCatalog types,
        SerializationRegistry serialization,
        IEnumerable<object> drawerServices)
    {
        m_registry = new PropertyTypeRegistry(interactions, types, serialization, drawerServices);
    }

    /// <summary>
    /// Resolves the most specific drawer for a declared property type.
    /// </summary>
    /// <param name="propertyType">
    /// The declared serialized property type to resolve.
    /// </param>
    /// <returns>
    /// The most specific registered drawer, or the unsupported-value fallback.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="propertyType"/> is <see langword="null"/>.
    /// </exception>
    public IPropertyDrawer Resolve(Type propertyType)
    {
        ArgumentNullException.ThrowIfNull(propertyType);
        return m_registry.Resolve(propertyType);
    }

    /// <summary>
    /// Releases every active property drawer snapshot and unregisters the registry from type refreshes.
    /// </summary>
    public void Dispose() => m_registry.Dispose();

    private sealed class PropertyTypeRegistry : TypeRegistry<Registration[]>
    {
        private readonly EditorInteractions m_interactions;
        private readonly SerializationRegistry m_serialization;
        private readonly object[] m_drawerServices;

        internal PropertyTypeRegistry(
            EditorInteractions interactions,
            TypeCatalog types,
            SerializationRegistry serialization,
            IEnumerable<object> drawerServices)
            : base(types)
        {
            m_interactions = interactions ?? throw new ArgumentNullException(nameof(interactions));
            m_serialization = serialization ?? throw new ArgumentNullException(nameof(serialization));
            ArgumentNullException.ThrowIfNull(drawerServices);
            m_drawerServices = drawerServices.Select(static service =>
                service ?? throw new ArgumentException(
                    "Property drawer services cannot contain null.",
                    nameof(drawerServices))).ToArray();
        }

        internal IPropertyDrawer Resolve(Type propertyType)
        {
            Registration? best = null;
            int bestDistance = int.MaxValue;
            foreach (Registration registration in current)
            {
                if (!DrawerTypeUtility.TryGetDistance(
                        propertyType,
                        registration.targetType,
                        registration.useForChildren,
                        out int distance))
                    continue;
                if (best is null || distance < bestDistance ||
                    distance == bestDistance && registration.priority > best.priority)
                {
                    best = registration;
                    bestDistance = distance;
                }
            }
            return best?.drawer ?? UnsupportedPropertyDrawer.instance;
        }

        /// <summary>
        /// Builds a validated result from the current immutable input snapshot.
        /// </summary>
        /// <param name="types">
        /// The active type catalog generation used for extension resolution.
        /// </param>
        /// <returns>
        /// An immutable snapshot of the values selected by the operation.
        /// </returns>
        protected override Registration[] Build(TypeCacheSnapshot types)
        {
            var drawers = new Dictionary<Type, IPropertyDrawer>();
            var registrations = new List<Registration>();
            foreach (Type drawerType in types.GetTypesWithAttribute<PropertyDrawerAttribute>()
                         .Select(typeRef => typeRef.Resolve(types))
                         .OrderBy(static type => type.FullName, StringComparer.Ordinal))
            {
                IPropertyDrawer drawer = drawers.TryGetValue(drawerType, out IPropertyDrawer? existing)
                    ? existing
                    : CreateDrawer(
                        drawerType,
                        m_interactions,
                        types,
                        m_serialization,
                        m_drawerServices);
                drawers[drawerType] = drawer;

                foreach (PropertyDrawerAttribute attribute in
                         drawerType.GetCustomAttributes<PropertyDrawerAttribute>(false))
                {
                    EnsureNoConflict(registrations, attribute, drawerType);
                    registrations.Add(new Registration(
                        attribute.targetType,
                        attribute.useForChildren,
                        attribute.priority,
                        drawerType,
                        drawer));
                }
            }
            return registrations.ToArray();
        }

        /// <summary>
        /// Releases the generation lease retained by an immutable registry snapshot.
        /// </summary>
        /// <param name="snapshot">
        /// The immutable state snapshot consumed by this operation.
        /// </param>
        protected override void DisposeSnapshot(Registration[] snapshot)
        {
            var disposed = new HashSet<object>(ReferenceEqualityComparer.Instance);
            foreach (IPropertyDrawer drawer in snapshot.Select(static registration => registration.drawer))
            {
                if (disposed.Add(drawer) && drawer is IDisposable disposable)
                {
                    try
                    {
                        disposable.Dispose();
                    }
                    catch (Exception exception)
                    {
                        OnCleanupFailed(
                            $"disposing property drawer '{drawer.GetType().FullName}'",
                            exception);
                    }
                }
            }
        }
    }

    private static IPropertyDrawer CreateDrawer(
        Type type,
        EditorInteractions interactions,
        TypeCacheSnapshot types,
        SerializationRegistry serialization,
        IReadOnlyList<object> drawerServices)
    {
        ConstructorInfo[] constructors = type.GetConstructors(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (constructors.Length != 1)
            throw new InvalidOperationException($"Property drawer '{type.FullName}' must declare exactly one constructor.");
        ParameterInfo[] parameters = constructors[0].GetParameters();
        object?[] arguments = new object?[parameters.Length];
        for (int index = 0; index < parameters.Length; index++)
        {
            Type parameterType = parameters[index].ParameterType;
            object[] matches = drawerServices
                .Where(parameterType.IsInstanceOfType)
                .Prepend(parameterType == typeof(TypeCacheSnapshot) ? types : null)
                .Prepend(parameterType == typeof(SerializationRegistry) ? serialization : null)
                .Prepend(parameterType == typeof(EditorInteractions) ? interactions : null)
                .Where(static service => service is not null)
                .Cast<object>()
                .Distinct(ReferenceEqualityComparer.Instance)
                .ToArray();
            arguments[index] = matches.Length switch
            {
                1 => matches[0],
                0 => throw new InvalidOperationException(
                    $"Property drawer '{type.FullName}' requests unavailable service " +
                    $"'{parameterType.FullName}'."),
                _ => throw new InvalidOperationException(
                    $"Property drawer '{type.FullName}' requests ambiguous service " +
                    $"'{parameterType.FullName}'.")
            };
        }
        return (IPropertyDrawer)constructors[0].Invoke(arguments);
    }

    private static void EnsureNoConflict(
        IReadOnlyList<Registration> registrations,
        PropertyDrawerAttribute attribute,
        Type drawerType)
    {
        foreach (Registration existing in registrations)
        {
            if (existing.targetType == attribute.targetType && existing.priority == attribute.priority)
            {
                throw new InvalidOperationException(
                    $"Property drawers '{existing.drawerType.FullName}' and '{drawerType.FullName}' " +
                    $"conflict for '{attribute.targetType.FullName}'.");
            }
        }
    }

    private sealed record Registration(
        Type targetType,
        bool useForChildren,
        int priority,
        Type drawerType,
        IPropertyDrawer drawer);
}
