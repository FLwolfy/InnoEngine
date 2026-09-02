using System;
using System.Collections.Generic;
using System.Linq;

using Inno.Extensibility.Types;

namespace Inno.Editor.Settings;

internal sealed class EditorSettingsCatalog : TypeRegistry<EditorSettingsCatalog.Snapshot>
{
    internal EditorSettingsCatalog(TypeCatalog types)
        : base(types)
    {
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
        EditorSetting[] definitions = types.GetTypesWithAttribute<EditorSettingPathAttribute>()
            .Select(typeRef => typeRef.Resolve(types))
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
            {
                try
                {
                    disposable.Dispose();
                }
                catch (Exception exception)
                {
                    OnCleanupFailed(
                        $"disposing editor setting '{snapshot.definitions[i].GetType().FullName}'",
                        exception);
                }
            }
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
