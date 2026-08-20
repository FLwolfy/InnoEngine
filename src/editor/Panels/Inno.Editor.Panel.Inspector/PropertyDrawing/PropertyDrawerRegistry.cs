using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using Inno.Core.Reflection;
using Inno.Editor.Interactions;

namespace Inno.Editor.Panel.Inspector;

/// <summary>
/// Discovers and resolves serialized property drawers through the active type catalog.
/// </summary>
internal sealed class PropertyDrawerRegistry : IDisposable
{
    private readonly PropertyTypeRegistry m_registry;

    internal PropertyDrawerRegistry(EditorInteractions interactions)
    {
        m_registry = new PropertyTypeRegistry(interactions);
    }

    /// <summary>
    /// Resolves the most specific drawer for a declared property type.
    /// </summary>
    internal IPropertyDrawer Resolve(Type propertyType)
    {
        ArgumentNullException.ThrowIfNull(propertyType);
        return m_registry.Resolve(propertyType);
    }

    public void Dispose() => m_registry.Dispose();

    private sealed class PropertyTypeRegistry : TypeRegistry<Registration[]>
    {
        private readonly EditorInteractions m_interactions;

        internal PropertyTypeRegistry(EditorInteractions interactions)
        {
            m_interactions = interactions ?? throw new ArgumentNullException(nameof(interactions));
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

        protected override Registration[] Build(TypeCacheSnapshot types)
        {
            var drawers = new Dictionary<Type, IPropertyDrawer>();
            var registrations = new List<Registration>();
            foreach (Type drawerType in types.GetTypesWithAttribute<PropertyDrawerAttribute>()
                         .OrderBy(static type => type.FullName, StringComparer.Ordinal))
            {
                IPropertyDrawer drawer = drawers.TryGetValue(drawerType, out IPropertyDrawer? existing)
                    ? existing
                    : CreateDrawer(drawerType, m_interactions);
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

        protected override void DisposeSnapshot(Registration[] snapshot)
        {
            var disposed = new HashSet<object>(ReferenceEqualityComparer.Instance);
            foreach (IPropertyDrawer drawer in snapshot.Select(static registration => registration.drawer))
            {
                if (disposed.Add(drawer) && drawer is IDisposable disposable)
                    disposable.Dispose();
            }
        }
    }

    private static IPropertyDrawer CreateDrawer(Type type, EditorInteractions interactions)
    {
        ConstructorInfo[] constructors = type.GetConstructors(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (constructors.Length != 1)
            throw new InvalidOperationException($"Property drawer '{type.FullName}' must declare exactly one constructor.");
        ParameterInfo[] parameters = constructors[0].GetParameters();
        object?[] arguments = parameters.Length switch
        {
            0 => [],
            1 when parameters[0].ParameterType == typeof(EditorInteractions) => [interactions],
            _ => throw new InvalidOperationException(
                $"Property drawer '{type.FullName}' may only depend on EditorInteractions.")
        };
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
