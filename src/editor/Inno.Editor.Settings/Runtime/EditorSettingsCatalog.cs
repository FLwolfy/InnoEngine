using System;
using System.Collections.Generic;
using System.Linq;

using Inno.Core.Reflection;

namespace Inno.Editor.Settings;

internal sealed class EditorSettingsCatalog : TypeRegistry<EditorSettingsCatalog.Snapshot>
{
    internal Snapshot snapshot => current;

    protected override Snapshot Build(TypeCacheSnapshot types)
    {
        EditorSetting[] definitions = types.GetTypesWithAttribute<EditorSettingPathAttribute>()
            .OrderBy(static type => type.FullName, StringComparer.Ordinal)
            .Select(type => CreateDefinition(type))
            .OrderBy(static setting => setting.path, StringComparer.Ordinal)
            .ThenBy(static setting => setting.order)
            .ThenBy(static setting => setting.GetType().FullName, StringComparer.Ordinal)
            .ToArray();

        string? duplicatePath = definitions
            .GroupBy(static setting => setting.path, StringComparer.Ordinal)
            .FirstOrDefault(static group => group.Count() > 1)?.Key;
        if (duplicatePath is not null)
        {
            throw new InvalidOperationException(
                $"Editor setting path '{duplicatePath}' is registered more than once.");
        }

        var byPath = definitions.ToDictionary(
            static setting => setting.path,
            StringComparer.Ordinal);
        return new Snapshot(types.version, definitions, byPath);
    }

    protected override void DisposeSnapshot(Snapshot snapshot)
    {
        for (int i = snapshot.definitions.Length - 1; i >= 0; i--)
        {
            if (snapshot.definitions[i] is IDisposable disposable)
                disposable.Dispose();
        }
    }

    private EditorSetting CreateDefinition(Type type)
    {
        EditorSettingPathAttribute attribute = type
            .GetCustomAttributes(typeof(EditorSettingPathAttribute), inherit: false)
            .Cast<EditorSettingPathAttribute>()
            .Single();
        EditorSetting setting = CreateExtension<EditorSetting>(type);
        setting.BindPlacement(attribute.path, attribute.order);
        return setting;
    }

    internal sealed record Snapshot(
        long revision,
        EditorSetting[] definitions,
        IReadOnlyDictionary<string, EditorSetting> byPath);
}
