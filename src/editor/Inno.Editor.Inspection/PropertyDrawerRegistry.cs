using System;
using System.Collections.Generic;
using System.Reflection;

using Inno.Core.Reflection;

namespace Inno.Editor.Inspection;

/// <summary>
/// Discovers and resolves serialized property drawers through TypeCache.
/// </summary>
public static class PropertyDrawerRegistry
{
    private sealed record Registration(
        Type targetType,
        bool useForChildren,
        int priority,
        Type drawerType,
        IPropertyDrawer drawer);

    private static readonly object C_SYNC = new();
    private static Registration[] s_registrations = [];
    private static bool s_initialized;

    /// <summary>
    /// Resolves the most specific drawer for a declared property type.
    /// </summary>
    /// <param name="propertyType">Declared property type.</param>
    /// <returns>The resolved property drawer.</returns>
    public static IPropertyDrawer Resolve(Type propertyType)
    {
        ArgumentNullException.ThrowIfNull(propertyType);
        EnsureInitialized();

        Registration? best = null;
        int bestDistance = int.MaxValue;
        for (int i = 0; i < s_registrations.Length; i++)
        {
            Registration registration = s_registrations[i];
            if (!DrawerTypeUtility.TryGetDistance(
                    propertyType,
                    registration.targetType,
                    registration.useForChildren,
                    out int distance))
            {
                continue;
            }

            if (best is null || distance < bestDistance ||
                (distance == bestDistance && registration.priority > best.priority))
            {
                best = registration;
                bestDistance = distance;
            }
        }

        return best?.drawer ?? UnsupportedPropertyDrawer.instance;
    }

    [TypeCacheInitialize("Inno.Editor.Inspection")]
    [TypeCacheRebuild("Inno.Editor.Inspection")]
    private static void Rebuild()
    {
        lock (C_SYNC)
        {
            var drawers = new Dictionary<Type, IPropertyDrawer>();
            var registrations = new List<Registration>();
            IReadOnlyList<Type> drawerTypes = TypeCache.GetTypesWithAttribute<PropertyDrawerAttribute>();
            for (int i = 0; i < drawerTypes.Count; i++)
            {
                Type drawerType = drawerTypes[i];
                if (!typeof(IPropertyDrawer).IsAssignableFrom(drawerType))
                {
                    throw new InvalidOperationException(
                        $"Property drawer '{drawerType.FullName}' must implement {nameof(IPropertyDrawer)}.");
                }

                IPropertyDrawer drawer = drawers.TryGetValue(drawerType, out IPropertyDrawer? existing)
                    ? existing
                    : (IPropertyDrawer)(Activator.CreateInstance(drawerType, nonPublic: true)
                        ?? throw new InvalidOperationException($"Could not create property drawer '{drawerType.FullName}'."));
                drawers[drawerType] = drawer;

                PropertyDrawerAttribute[] attributes = [.. drawerType.GetCustomAttributes<PropertyDrawerAttribute>(false)];
                for (int a = 0; a < attributes.Length; a++)
                {
                    PropertyDrawerAttribute attribute = attributes[a];
                    EnsureNoConflict(registrations, attribute, drawerType);
                    registrations.Add(new Registration(
                        attribute.targetType,
                        attribute.useForChildren,
                        attribute.priority,
                        drawerType,
                        drawer));
                }
            }

            s_registrations = registrations.ToArray();
            s_initialized = true;
        }
    }

    private static void EnsureInitialized()
    {
        if (!s_initialized)
        {
            Rebuild();
        }
    }

    private static void EnsureNoConflict(
        List<Registration> registrations,
        PropertyDrawerAttribute attribute,
        Type drawerType)
    {
        for (int i = 0; i < registrations.Count; i++)
        {
            Registration existing = registrations[i];
            if (existing.targetType == attribute.targetType &&
                existing.priority == attribute.priority)
            {
                throw new InvalidOperationException(
                    $"Property drawers '{existing.drawerType.FullName}' and '{drawerType.FullName}' conflict for '{attribute.targetType.FullName}'.");
            }
        }
    }
}
