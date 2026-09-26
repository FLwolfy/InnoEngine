using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Inno.Extensibility.Types;

namespace Inno.Editor.Rendering;

internal sealed class EditorGizmoProviderRegistry : TypeRegistry<EditorGizmoProviderRegistry.Snapshot>
{
    internal EditorGizmoProviderRegistry(TypeCatalog types) : base(types) { }

    internal Snapshot providers => current;

    /// <summary>
    /// Builds a validated result from the current immutable input snapshot.
    /// </summary>
    /// <param name="types">
    /// The active type catalog generation used for extension resolution.
    /// </param>
    /// <returns>
    /// The validated snapshot that represents the completed operation.
    /// </returns>
protected override Snapshot Build(TypeCacheSnapshot types)
    {
        Registration[] providers = types.GetTypesWithAttribute<EditorGizmoProviderExtensionAttribute>()
            .Select(typeRef => typeRef.Resolve(types))
            .OrderBy(static type => type.FullName, StringComparer.Ordinal)
            .Select(type => new Registration(
                type.GetCustomAttribute<EditorGizmoProviderExtensionAttribute>(false)!.id,
                CreateExtension<EditorGizmoProvider>(type)))
            .OrderBy(static entry => entry.id, StringComparer.Ordinal)
            .ToArray();
        string? duplicate = providers.GroupBy(static entry => entry.id, StringComparer.Ordinal)
            .FirstOrDefault(static group => group.Count() > 1)?.Key;
        if (duplicate is not null)
            throw new InvalidOperationException($"Editor gizmo provider ID '{duplicate}' is duplicated.");
        return new Snapshot(providers);
    }

    /// <summary>
    /// Releases the generation lease retained by an immutable registry snapshot.
    /// </summary>
    /// <param name="snapshot">
    /// The immutable state snapshot consumed by this operation.
    /// </param>
protected override void DisposeSnapshot(Snapshot snapshot)
        => DisposeExtensions(snapshot.registrations.Select(static entry => entry.provider));

    internal sealed record Snapshot(Registration[] registrations);
    internal sealed record Registration(string id, EditorGizmoProvider provider);
}
