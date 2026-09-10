using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Inno.Core.Serialization;
using Inno.Editor.Interactions;
using Inno.Extensibility.Types;

namespace Inno.Editor.Inspection;

/// <summary>
/// Discovers generation-aware Inspector attribute drawers and applies them in declaration order.
/// </summary>
public sealed class InspectorAttributeDrawerRegistry : IDisposable
{
    private readonly AttributeTypeRegistry m_registry;

    /// <summary>
    /// Creates an Inspector attribute drawer registry.
    /// </summary>
    /// <param name="interactions">
    /// Active editor interaction entry point.
    /// </param>
    /// <param name="types">
    /// Host-owned type catalog.
    /// </param>
    /// <param name="serialization">
    /// Active serialization registry.
    /// </param>
    /// <param name="drawerServices">
    /// Additional constructor-injected drawer services.
    /// </param>
    public InspectorAttributeDrawerRegistry(
        EditorInteractions interactions,
        TypeCatalog types,
        SerializationRegistry serialization,
        IEnumerable<object> drawerServices)
    {
        m_registry = new AttributeTypeRegistry(interactions, types, serialization, drawerServices);
    }

    internal void Update(InspectorAttributeDrawContext context, IReadOnlyList<Attribute> attributes)
        => Apply(context, attributes, static (drawer, value) => drawer.Update(value));

    internal void DrawBefore(InspectorAttributeDrawContext context, IReadOnlyList<Attribute> attributes)
        => Apply(context, attributes, static (drawer, value) => drawer.DrawBefore(value));

    internal void DrawAfter(InspectorAttributeDrawContext context, IReadOnlyList<Attribute> attributes)
        => Apply(context, attributes, static (drawer, value) => drawer.DrawAfter(value));

    private void Apply(
        InspectorAttributeDrawContext context,
        IReadOnlyList<Attribute> attributes,
        Action<IInspectorAttributeDrawer, InspectorAttributeDrawContext> callback)
    {
        for (int index = 0; index < attributes.Count; index++)
        {
            Attribute attribute = attributes[index];
            IInspectorAttributeDrawer? drawer = m_registry.Resolve(attribute.GetType());
            if (drawer is null)
                continue;
            context.SelectAttribute(attribute);
            callback(drawer, context);
        }
    }

    /// <summary>
    /// Releases active drawer generations.
    /// </summary>
    public void Dispose() => m_registry.Dispose();

    private sealed class AttributeTypeRegistry : TypeRegistry<Registration[]>
    {
        private readonly EditorInteractions m_interactions;
        private readonly SerializationRegistry m_serialization;
        private readonly object[] m_services;

        internal AttributeTypeRegistry(
            EditorInteractions interactions,
            TypeCatalog types,
            SerializationRegistry serialization,
            IEnumerable<object> services)
            : base(types)
        {
            m_interactions = interactions ?? throw new ArgumentNullException(nameof(interactions));
            m_serialization = serialization ?? throw new ArgumentNullException(nameof(serialization));
            ArgumentNullException.ThrowIfNull(services);
            m_services = services.Select(static service => service ?? throw new ArgumentException(
                "Inspector attribute drawer services cannot contain null.", nameof(services))).ToArray();
        }

        internal IInspectorAttributeDrawer? Resolve(Type attributeType)
        {
            Registration? best = null;
            int bestDistance = int.MaxValue;
            foreach (Registration registration in current)
            {
                if (!DrawerTypeUtility.TryGetDistance(
                        attributeType,
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
            return best?.drawer;
        }

        /// <summary>
        /// Builds one immutable attribute-drawer generation from the active type snapshot.
        /// </summary>
        /// <param name="types">
        /// Type snapshot used to discover and construct drawers.
        /// </param>
        /// <returns>
        /// Deterministically ordered attribute-drawer registrations.
        /// </returns>
        protected override Registration[] Build(TypeCacheSnapshot types)
        {
            var drawers = new Dictionary<Type, IInspectorAttributeDrawer>();
            var registrations = new List<Registration>();
            foreach (Type drawerType in types.GetTypesWithAttribute<InspectorAttributeDrawerAttribute>()
                         .Select(typeRef => typeRef.Resolve(types))
                         .OrderBy(static type => type.FullName, StringComparer.Ordinal))
            {
                IInspectorAttributeDrawer drawer = drawers.TryGetValue(drawerType, out IInspectorAttributeDrawer? existing)
                    ? existing
                    : OwnCandidateExtension(CreateDrawer(drawerType, types));
                drawers[drawerType] = drawer;
                foreach (InspectorAttributeDrawerAttribute attribute in
                         drawerType.GetCustomAttributes<InspectorAttributeDrawerAttribute>(false))
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
        /// Releases extension instances owned by a retired drawer generation.
        /// </summary>
        /// <param name="snapshot">
        /// Retired attribute-drawer registrations.
        /// </param>
        protected override void DisposeSnapshot(Registration[] snapshot)
            => DisposeExtensions(snapshot.Select(static registration => registration.drawer));

        private IInspectorAttributeDrawer CreateDrawer(Type drawerType, TypeCacheSnapshot types)
        {
            ConstructorInfo[] constructors = drawerType.GetConstructors(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (constructors.Length != 1)
            {
                throw new InvalidOperationException(
                    $"Inspector attribute drawer '{drawerType.FullName}' must declare exactly one constructor.");
            }
            ParameterInfo[] parameters = constructors[0].GetParameters();
            var arguments = new object?[parameters.Length];
            for (int index = 0; index < parameters.Length; index++)
            {
                Type parameterType = parameters[index].ParameterType;
                object[] matches = m_services
                    .Where(parameterType.IsInstanceOfType)
                    .Prepend(parameterType == typeof(TypeCacheSnapshot) ? types : null)
                    .Prepend(parameterType == typeof(SerializationRegistry) ? m_serialization : null)
                    .Prepend(parameterType == typeof(EditorInteractions) ? m_interactions : null)
                    .Where(static service => service is not null)
                    .Cast<object>()
                    .Distinct(ReferenceEqualityComparer.Instance)
                    .ToArray();
                arguments[index] = matches.Length switch
                {
                    1 => matches[0],
                    0 => throw new InvalidOperationException(
                        $"Inspector attribute drawer '{drawerType.FullName}' requests unavailable service " +
                        $"'{parameterType.FullName}'."),
                    _ => throw new InvalidOperationException(
                        $"Inspector attribute drawer '{drawerType.FullName}' requests ambiguous service " +
                        $"'{parameterType.FullName}'.")
                };
            }
            return constructors[0].Invoke(arguments) as IInspectorAttributeDrawer
                   ?? throw new InvalidOperationException(
                       $"Inspector attribute drawer '{drawerType.FullName}' does not implement " +
                       $"'{typeof(IInspectorAttributeDrawer).FullName}'.");
        }
    }

    private static void EnsureNoConflict(
        IReadOnlyList<Registration> registrations,
        InspectorAttributeDrawerAttribute attribute,
        Type drawerType)
    {
        foreach (Registration existing in registrations)
        {
            if (existing.targetType == attribute.targetType && existing.priority == attribute.priority)
            {
                throw new InvalidOperationException(
                    $"Inspector attribute drawers '{existing.drawerType.FullName}' and '{drawerType.FullName}' " +
                    $"conflict for '{attribute.targetType.FullName}'.");
            }
        }
    }

    private sealed record Registration(
        Type targetType,
        bool useForChildren,
        int priority,
        Type drawerType,
        IInspectorAttributeDrawer drawer);
}
