using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using Inno.Core.Reflection;
using Inno.Editor.Core;
using Inno.Editor.Scene.Workspace;

namespace Inno.Editor.Scene.Inspection;

/// <summary>
/// Discovers and resolves inspector drawers through the active type catalog.
/// </summary>
internal sealed class InspectorDrawerRegistry : IDisposable
{
    private readonly InspectorTypeRegistry m_registry;

    internal InspectorDrawerRegistry(EditorSceneWorkspace workspace)
    {
        m_registry = new InspectorTypeRegistry(workspace);
    }

    /// <summary>
    /// Draws a selected target using the most specific registered drawer.
    /// </summary>
    internal bool Draw(
        EditorContext editorContext,
        object target,
        SerializedPropertyRenderer renderer)
    {
        ArgumentNullException.ThrowIfNull(editorContext);
        ArgumentNullException.ThrowIfNull(target);
        IInspectorDrawer? drawer = m_registry.Resolve(target.GetType());
        if (drawer is null)
            return false;

        drawer.Draw(new InspectorDrawContext(
            editorContext,
            target,
            renderer));
        return true;
    }

    public void Dispose() => m_registry.Dispose();

    private sealed class InspectorTypeRegistry : TypeRegistry<Registration[]>
    {
        private readonly EditorSceneWorkspace m_workspace;

        internal InspectorTypeRegistry(EditorSceneWorkspace workspace)
        {
            m_workspace = workspace;
        }

        internal IInspectorDrawer? Resolve(Type targetType)
        {
            Registration? best = null;
            int bestDistance = int.MaxValue;
            foreach (Registration registration in current)
            {
                if (!DrawerTypeUtility.TryGetDistance(
                        targetType,
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

        protected override Registration[] Build(TypeCacheSnapshot types)
        {
            var drawers = new Dictionary<Type, IInspectorDrawer>();
            var registrations = new List<Registration>();
            foreach (Type drawerType in types.GetTypesWithAttribute<InspectorDrawerAttribute>()
                         .OrderBy(static type => type.FullName, StringComparer.Ordinal))
            {
                IInspectorDrawer drawer = drawers.TryGetValue(drawerType, out IInspectorDrawer? existing)
                    ? existing
                    : CreateDrawer(drawerType, m_workspace);
                drawers[drawerType] = drawer;

                foreach (InspectorDrawerAttribute attribute in
                         drawerType.GetCustomAttributes<InspectorDrawerAttribute>(false))
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
            foreach (IInspectorDrawer drawer in snapshot.Select(static registration => registration.drawer))
            {
                if (disposed.Add(drawer) && drawer is IDisposable disposable)
                    disposable.Dispose();
            }
        }
    }

    private static IInspectorDrawer CreateDrawer(Type type, EditorSceneWorkspace workspace)
    {
        ConstructorInfo[] constructors = type.GetConstructors(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (constructors.Length != 1)
            throw new InvalidOperationException($"Inspector drawer '{type.FullName}' must declare exactly one constructor.");
        ParameterInfo[] parameters = constructors[0].GetParameters();
        object?[] arguments = parameters.Length switch
        {
            0 => [],
            1 when parameters[0].ParameterType == typeof(EditorSceneWorkspace) => [workspace],
            _ => throw new InvalidOperationException(
                $"Inspector drawer '{type.FullName}' may only depend on EditorSceneWorkspace.")
        };
        return (IInspectorDrawer)constructors[0].Invoke(arguments);
    }

    private static void EnsureNoConflict(
        IReadOnlyList<Registration> registrations,
        InspectorDrawerAttribute attribute,
        Type drawerType)
    {
        foreach (Registration existing in registrations)
        {
            if (existing.targetType == attribute.targetType && existing.priority == attribute.priority)
            {
                throw new InvalidOperationException(
                    $"Inspector drawers '{existing.drawerType.FullName}' and '{drawerType.FullName}' " +
                    $"conflict for '{attribute.targetType.FullName}'.");
            }
        }
    }

    private sealed record Registration(
        Type targetType,
        bool useForChildren,
        int priority,
        Type drawerType,
        IInspectorDrawer drawer);
}
