using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using Inno.Core.Reflection;
using Inno.Editor.Core;
using Inno.Editor.Interactions;

namespace Inno.Editor.Inspection;

/// <summary>
/// Discovers and resolves inspection drawers through the active type catalog.
/// </summary>
public sealed class InspectionDrawerRegistry : IDisposable
{
    private readonly InspectionTypeRegistry m_registry;

    /// <summary>
    /// Creates a generation-aware inspection drawer registry.
    /// </summary>
    /// <param name="interactions">The active editor interaction entry point exposed to draw contexts.</param>
    /// <param name="factory">
    /// The composition-root factory used to construct discovered drawer types and resolve their
    /// module-specific dependencies.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="interactions"/> or <paramref name="factory"/> is
    /// <see langword="null"/>.
    /// </exception>
    public InspectionDrawerRegistry(
        EditorInteractions interactions,
        InspectionDrawerFactory factory)
    {
        m_registry = new InspectionTypeRegistry(interactions, factory);
    }

    /// <summary>
    /// Resolves the most specific registered drawer and creates its drawing context.
    /// </summary>
    /// <param name="editorContext">The shared editor context exposed to the selected drawer.</param>
    /// <param name="target">The selected object whose runtime type determines the drawer.</param>
    /// <param name="renderer">The serialized property renderer exposed to the selected drawer.</param>
    /// <param name="drawer">The resolved drawer when the method succeeds.</param>
    /// <param name="context">The target-specific drawing context when the method succeeds.</param>
    /// <returns><see langword="true"/> when a matching drawer was resolved.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="editorContext"/>, <paramref name="target"/>, or
    /// <paramref name="renderer"/> is <see langword="null"/>.
    /// </exception>
    public bool TryResolve(
        EditorContext editorContext,
        object target,
        SerializedPropertyRenderer renderer,
        out IInspectionDrawer? drawer,
        out InspectionDrawContext? context)
    {
        ArgumentNullException.ThrowIfNull(editorContext);
        ArgumentNullException.ThrowIfNull(target);
        drawer = m_registry.Resolve(target.GetType());
        if (drawer is null)
        {
            context = null;
            return false;
        }

        context = new InspectionDrawContext(
            editorContext,
            m_registry.interactions,
            target,
            renderer);
        return true;
    }

    /// <summary>
    /// Resolves only a drawer explicitly registered for the target's exact runtime type.
    /// </summary>
    /// <param name="editorContext">The shared editor context exposed to the selected drawer.</param>
    /// <param name="target">The selected object whose exact runtime type determines the drawer.</param>
    /// <param name="renderer">The serialized property renderer exposed to the selected drawer.</param>
    /// <param name="drawer">The exact drawer when the method succeeds.</param>
    /// <param name="context">The target-specific drawing context when the method succeeds.</param>
    /// <returns>
    /// <see langword="true"/> when an exact registration exists; inherited and fallback drawers are ignored.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="editorContext"/>, <paramref name="target"/>, or
    /// <paramref name="renderer"/> is <see langword="null"/>.
    /// </exception>
    public bool TryResolveExact(
        EditorContext editorContext,
        object target,
        SerializedPropertyRenderer renderer,
        out IInspectionDrawer? drawer,
        out InspectionDrawContext? context)
    {
        ArgumentNullException.ThrowIfNull(editorContext);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(renderer);
        drawer = m_registry.ResolveExact(target.GetType());
        if (drawer is null)
        {
            context = null;
            return false;
        }
        context = new InspectionDrawContext(editorContext, m_registry.interactions, target, renderer);
        return true;
    }

    /// <summary>
    /// Releases every active drawer snapshot and unregisters the registry from type refreshes.
    /// </summary>
    public void Dispose() => m_registry.Dispose();

    private sealed class InspectionTypeRegistry : TypeRegistry<Registration[]>
    {
        private readonly InspectionDrawerFactory m_factory;

        internal EditorInteractions interactions { get; }

        internal InspectionTypeRegistry(
            EditorInteractions interactions,
            InspectionDrawerFactory factory)
        {
            this.interactions = interactions ?? throw new ArgumentNullException(nameof(interactions));
            m_factory = factory ?? throw new ArgumentNullException(nameof(factory));
        }

        internal IInspectionDrawer? Resolve(Type targetType)
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

        internal IInspectionDrawer? ResolveExact(Type targetType)
            => current
                .Where(registration => registration.targetType == targetType)
                .OrderByDescending(static registration => registration.priority)
                .Select(static registration => registration.drawer)
                .FirstOrDefault();

        protected override Registration[] Build(TypeCacheSnapshot types)
        {
            var drawers = new Dictionary<Type, IInspectionDrawer>();
            var registrations = new List<Registration>();
            foreach (Type drawerType in types.GetTypesWithAttribute<InspectionDrawerAttribute>()
                         .OrderBy(static type => type.FullName, StringComparer.Ordinal))
            {
                IInspectionDrawer drawer = drawers.TryGetValue(drawerType, out IInspectionDrawer? existing)
                    ? existing
                    : m_factory(drawerType);
                if (!drawerType.IsInstanceOfType(drawer))
                {
                    throw new InvalidOperationException(
                        $"Inspection drawer factory returned '{drawer.GetType().FullName}' for " +
                        $"'{drawerType.FullName}'.");
                }
                drawers[drawerType] = drawer;

                foreach (InspectionDrawerAttribute attribute in
                         drawerType.GetCustomAttributes<InspectionDrawerAttribute>(false))
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
            foreach (IInspectionDrawer drawer in snapshot.Select(static registration => registration.drawer))
            {
                if (disposed.Add(drawer) && drawer is IDisposable disposable)
                    disposable.Dispose();
            }
        }
    }

    private static void EnsureNoConflict(
        IReadOnlyList<Registration> registrations,
        InspectionDrawerAttribute attribute,
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
        IInspectionDrawer drawer);
}
