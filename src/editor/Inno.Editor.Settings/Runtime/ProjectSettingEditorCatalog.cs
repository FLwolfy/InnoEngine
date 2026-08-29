using System;
using System.Collections.Generic;
using System.Linq;

using Inno.Core.Reflection;
using Inno.Core.Settings;

namespace Inno.Editor.Settings;

internal sealed class ProjectSettingEditorCatalog : TypeRegistry<ProjectSettingEditorCatalog.Snapshot>
{
    internal Snapshot snapshot => current;

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

        ProjectSettingId? duplicateId = definitions
            .GroupBy(static definition => definition.settingId)
            .FirstOrDefault(static group => group.Count() > 1)?.Key;
        if (duplicateId is ProjectSettingId id)
            throw new InvalidOperationException($"Project setting ID '{id}' has more than one Editor presentation.");

        return new Snapshot(types.version, definitions);
    }

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
        definition.BindPlacement(attribute.path, attribute.order);
        return definition;
    }

    internal sealed record Snapshot(long revision, ProjectSettingEditor[] definitions);
}
