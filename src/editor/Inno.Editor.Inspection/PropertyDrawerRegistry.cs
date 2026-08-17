using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using Inno.Core.Assemblies;
using Inno.Core.Reflection;

namespace Inno.Editor.Inspection;

/// <summary>
/// Discovers and resolves serialized property drawers through the active type catalog.
/// </summary>
public static class PropertyDrawerRegistry
{
    private static readonly PropertyTypeRegistry S_REGISTRY = new();

    /// <summary>
    /// Resolves the most specific drawer for a declared property type.
    /// </summary>
    public static IPropertyDrawer Resolve(Type propertyType)
    {
        ArgumentNullException.ThrowIfNull(propertyType);
        return S_REGISTRY.Resolve(propertyType);
    }

    private sealed class PropertyTypeRegistry : TypeRegistry<Registration[]>
    {
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
                    : CreateExtension<IPropertyDrawer>(drawerType);
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
