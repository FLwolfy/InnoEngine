using System;
using System.Collections.Generic;
using System.Linq;

using Inno.Extensibility.Types;
using Inno.Core.Serialization;
using Inno.Core.Settings;

namespace Inno.Editor.Settings;

internal sealed class ProjectSettingEditorCatalog : TypeRegistry<ProjectSettingEditorCatalog.Snapshot>
{
    private readonly SerializationRegistry m_serialization;

    internal ProjectSettingEditorCatalog(
        TypeCatalog types,
        SerializationRegistry serialization)
        : base(types)
    {
        ArgumentNullException.ThrowIfNull(serialization);
        m_serialization = serialization;
    }

    internal Snapshot snapshot => current;

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
        ProjectSettingEditor[] definitions = types.GetTypesWithAttribute<ProjectSettingPathAttribute>()
            .Select(typeRef => typeRef.Resolve(types))
            .OrderBy(static type => type.FullName, StringComparer.Ordinal)
            .Select(CreateDefinition)
            .OrderBy(static definition => definition.path, StringComparer.Ordinal)
            .ThenBy(static definition => definition.order)
            .ThenBy(static definition => definition.GetType().FullName, StringComparer.Ordinal)
            .ToArray();

        string? duplicatePath = definitions
            .GroupBy(static definition => definition.path, StringComparer.Ordinal)
            .FirstOrDefault(static group => group.Count() > 1)?.Key;
        if (duplicatePath is not null)
            throw new InvalidOperationException($"Project setting path '{duplicatePath}' is registered more than once.");

        IGrouping<ProjectSettingId, ProjectSettingEditor>? incompatiblePresentations = definitions
            .GroupBy(static definition => definition.settingId)
            .FirstOrDefault(static group => group
                .Select(static definition => definition.valueType)
                .Distinct()
                .Skip(1)
                .Any());
        if (incompatiblePresentations is not null)
        {
            throw new InvalidOperationException(
                $"Project setting ID '{incompatiblePresentations.Key}' has Editor presentations for more than one value type.");
        }

        return new Snapshot(types.version, definitions);
    }

    /// <summary>
    /// Releases the generation lease retained by an immutable registry snapshot.
    /// </summary>
    /// <param name="snapshot">
    /// The immutable state snapshot consumed by this operation.
    /// </param>
    protected override void DisposeSnapshot(Snapshot snapshot)
    {
        for (int i = snapshot.definitions.Length - 1; i >= 0; i--)
        {
            if (snapshot.definitions[i] is IDisposable disposable)
                disposable.Dispose();
        }
    }

    private ProjectSettingEditor CreateDefinition(Type type)
    {
        ProjectSettingPathAttribute attribute = type
            .GetCustomAttributes(typeof(ProjectSettingPathAttribute), inherit: false)
            .Cast<ProjectSettingPathAttribute>()
            .Single();
        ProjectSettingEditor definition = CreateExtension<ProjectSettingEditor>(type);
        definition.BindSerialization(m_serialization);
        definition.BindPlacement(attribute.path, attribute.order);
        return definition;
    }

    internal sealed record Snapshot(long revision, ProjectSettingEditor[] definitions);
}
