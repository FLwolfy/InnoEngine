using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using Inno.Extensibility.Types;

namespace Inno.Editor.Rendering;

internal sealed class EditorViewportProviderRegistry
    : TypeRegistry<EditorViewportProviderRegistry.Snapshot>
{
    internal EditorViewportProviderRegistry(TypeCatalog types)
        : base(types)
    {
    }

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
        Registration[] registrations = types
            .GetTypesWithAttribute<EditorViewportProviderExtensionAttribute>()
            .Select(typeRef => typeRef.Resolve(types))
            .OrderBy(static type => type.FullName, StringComparer.Ordinal)
            .Select(CreateRegistration)
            .OrderByDescending(static registration => registration.attribute.priority)
            .ThenBy(static registration => registration.attribute.id, StringComparer.Ordinal)
            .ToArray();
        string? duplicateId = registrations
            .GroupBy(static registration => registration.attribute.id, StringComparer.Ordinal)
            .FirstOrDefault(static group => group.Count() > 1)?.Key;
        if (duplicateId is not null)
            throw new InvalidOperationException($"Editor viewport provider ID '{duplicateId}' is duplicated.");
        var selected = registrations
            .GroupBy(static registration => registration.attribute.kind)
            .ToDictionary(static group => group.Key, static group => group.First());
        return new Snapshot(types.version, registrations, selected);
    }

    /// <summary>
    /// Releases the generation lease retained by an immutable registry snapshot.
    /// </summary>
    /// <param name="snapshot">
    /// The immutable state snapshot consumed by this operation.
    /// </param>
    protected override void DisposeSnapshot(Snapshot snapshot)
    {
        for (int i = snapshot.registrations.Length - 1; i >= 0; i--)
        {
            if (snapshot.registrations[i].provider is IDisposable disposable)
                disposable.Dispose();
        }
    }

    private static Registration CreateRegistration(Type type)
        => new(
            type.GetCustomAttribute<EditorViewportProviderExtensionAttribute>(inherit: false)!,
            CreateExtension<EditorViewportProvider>(type));

    internal sealed record Snapshot(
        long revision,
        Registration[] registrations,
        IReadOnlyDictionary<EditorViewportKindId, Registration> byKind);

    internal sealed record Registration(
        EditorViewportProviderExtensionAttribute attribute,
        EditorViewportProvider provider);
}
