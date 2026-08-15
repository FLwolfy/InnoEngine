using System;
using System.Collections.Generic;
using System.Reflection;

using Inno.Core.Reflection;
using Inno.Editor.Core;

namespace Inno.Editor.Inspection;

/// <summary>
/// Discovers and resolves inspector drawers through TypeCache.
/// </summary>
public static class InspectorDrawerRegistry
{
    private sealed record Registration(
        Type targetType,
        bool useForChildren,
        int priority,
        Type drawerType,
        IInspectorDrawer drawer);

    private static readonly object C_SYNC = new();
    private static Registration[] s_registrations = [];
    private static bool s_initialized;

    /// <summary>
    /// Draws a selected target using the most specific registered drawer.
    /// </summary>
    /// <param name="editorContext">Shared editor context.</param>
    /// <param name="target">Selected target.</param>
    /// <returns><see langword="true"/> when a drawer was resolved.</returns>
    public static bool Draw(EditorContext editorContext, object target)
    {
        ArgumentNullException.ThrowIfNull(editorContext);
        ArgumentNullException.ThrowIfNull(target);
        EnsureInitialized();

        IInspectorDrawer? drawer = Resolve(target.GetType());
        if (drawer is null)
        {
            return false;
        }

        drawer.Draw(new InspectorDrawContext(
            editorContext,
            target,
            SerializedPropertyRenderer.shared));
        return true;
    }

    [TypeCacheInitialize("Inno.Editor.Inspection")]
    [TypeCacheRebuild("Inno.Editor.Inspection")]
    private static void Rebuild()
    {
        lock (C_SYNC)
        {
            var drawers = new Dictionary<Type, IInspectorDrawer>();
            var registrations = new List<Registration>();
            IReadOnlyList<Type> drawerTypes = TypeCache.GetTypesWithAttribute<InspectorDrawerAttribute>();
            for (int i = 0; i < drawerTypes.Count; i++)
            {
                Type drawerType = drawerTypes[i];
                if (!typeof(IInspectorDrawer).IsAssignableFrom(drawerType))
                {
                    throw new InvalidOperationException(
                        $"Inspector drawer '{drawerType.FullName}' must implement {nameof(IInspectorDrawer)}.");
                }

                IInspectorDrawer drawer = drawers.TryGetValue(drawerType, out IInspectorDrawer? existing)
                    ? existing
                    : (IInspectorDrawer)(Activator.CreateInstance(drawerType, nonPublic: true)
                        ?? throw new InvalidOperationException($"Could not create inspector drawer '{drawerType.FullName}'."));
                drawers[drawerType] = drawer;

                InspectorDrawerAttribute[] attributes = [.. drawerType.GetCustomAttributes<InspectorDrawerAttribute>(false)];
                for (int a = 0; a < attributes.Length; a++)
                {
                    InspectorDrawerAttribute attribute = attributes[a];
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
        if (s_initialized)
        {
            return;
        }

        Rebuild();
    }

    private static IInspectorDrawer? Resolve(Type targetType)
    {
        Registration? best = null;
        int bestDistance = int.MaxValue;
        for (int i = 0; i < s_registrations.Length; i++)
        {
            Registration registration = s_registrations[i];
            if (!DrawerTypeUtility.TryGetDistance(
                    targetType,
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

        return best?.drawer;
    }

    private static void EnsureNoConflict(
        List<Registration> registrations,
        InspectorDrawerAttribute attribute,
        Type drawerType)
    {
        for (int i = 0; i < registrations.Count; i++)
        {
            Registration existing = registrations[i];
            if (existing.targetType == attribute.targetType &&
                existing.priority == attribute.priority)
            {
                throw new InvalidOperationException(
                    $"Inspector drawers '{existing.drawerType.FullName}' and '{drawerType.FullName}' conflict for '{attribute.targetType.FullName}'.");
            }
        }
    }
}
