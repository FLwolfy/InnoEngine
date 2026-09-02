using System;
using System.Collections.Generic;
using System.Collections.Frozen;
using System.Reflection;
using Inno.Extensibility.Types;
using Inno.Core.Serialization;

namespace Inno.Core.Settings;

internal sealed class ProjectSettingsRegistry : TypeRegistry<ProjectSettingsRegistry.Snapshot>
{
    internal ProjectSettingsRegistry(TypeCatalog types)
        : base(types)
    {
    }

    internal Snapshot settings => current;

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
        var settingTypes = new Dictionary<ProjectSettingId, (Guid stableTypeId, Type runtimeType)>();
        foreach (TypeRef typeRef in types.GetTypesWithAttribute<ProjectSettingDefinitionAttribute>())
        {
            Type type = typeRef.Resolve(types);
            if (type.IsAbstract || !typeof(ISerializable).IsAssignableFrom(type))
            {
                throw new InvalidOperationException(
                    $"Project setting type '{type.FullName}' must be a non-abstract ISerializable class.");
            }
            if (type.GetConstructor(
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    binder: null,
                    Type.EmptyTypes,
                    modifiers: null) is null)
            {
                throw new InvalidOperationException(
                    $"Project setting type '{type.FullName}' requires a parameterless constructor.");
            }
            if (!types.TryGetTypeRef(type, out TypeRef stableType))
                throw new InvalidOperationException($"Project setting type '{type.FullName}' requires StableTypeId.");
            ProjectSettingDefinitionAttribute attribute =
                type.GetCustomAttribute<ProjectSettingDefinitionAttribute>(inherit: false)!;
            var id = new ProjectSettingId(attribute.id);
            if (!settingTypes.TryAdd(id, (stableType.stableId, type)))
                throw new InvalidOperationException($"Project setting ID '{id}' is declared more than once.");
        }

        var composers = new Dictionary<ProjectSettingId, ProjectSettingComposer>();
        foreach (TypeRef typeRef in types.GetTypesWithAttribute<ProjectSettingComposerAttribute>())
        {
            Type type = typeRef.Resolve(types);
            if (type.IsAbstract || !typeof(ProjectSettingComposer).IsAssignableFrom(type))
            {
                throw new InvalidOperationException(
                    $"Project setting composer '{type.FullName}' must be a non-abstract ProjectSettingComposer.");
            }
            if (type.GetConstructor(
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    binder: null,
                    Type.EmptyTypes,
                    modifiers: null) is null)
            {
                throw new InvalidOperationException(
                    $"Project setting composer '{type.FullName}' requires a parameterless constructor.");
            }
            ProjectSettingComposerAttribute attribute =
                type.GetCustomAttribute<ProjectSettingComposerAttribute>(inherit: false)!;
            if (!settingTypes.TryGetValue(attribute.settingId, out var setting))
            {
                throw new InvalidOperationException(
                    $"Project setting composer '{type.FullName}' targets undefined setting '{attribute.settingId}'.");
            }
            var composer = (ProjectSettingComposer)(Activator.CreateInstance(type, nonPublic: true)
                ?? throw new InvalidOperationException(
                    $"Project setting composer '{type.FullName}' could not be created."));
            if (composer.settingType != setting.runtimeType)
            {
                throw new InvalidOperationException(
                    $"Project setting composer '{type.FullName}' expects '{composer.settingType.FullName}', " +
                    $"but '{attribute.settingId}' is '{setting.runtimeType.FullName}'.");
            }
            if (!composers.TryAdd(attribute.settingId, composer))
            {
                throw new InvalidOperationException(
                    $"Project setting '{attribute.settingId}' declares more than one composer.");
            }
        }

        var definitions = new Dictionary<ProjectSettingId, Definition>();
        foreach ((ProjectSettingId id, (Guid stableTypeId, Type runtimeType)) in settingTypes)
        {
            composers.TryGetValue(id, out ProjectSettingComposer? composer);
            definitions.Add(id, new Definition(stableTypeId, runtimeType, composer));
        }
        return new Snapshot(types.version, definitions.ToFrozenDictionary());
    }

    internal sealed class Snapshot
    {
        private readonly FrozenDictionary<ProjectSettingId, Definition> m_definitions;

        internal Snapshot(long typeCacheVersion, FrozenDictionary<ProjectSettingId, Definition> definitions)
        {
            this.typeCacheVersion = typeCacheVersion;
            m_definitions = definitions;
        }

        internal long typeCacheVersion { get; }

        internal IEnumerable<KeyValuePair<ProjectSettingId, Definition>> definitions => m_definitions;

        internal bool TryGet(ProjectSettingId id, out Definition definition)
            => m_definitions.TryGetValue(id, out definition!);
    }

    internal sealed record Definition(
        Guid stableTypeId,
        Type runtimeType,
        ProjectSettingComposer? composer)
    {
        internal ISerializable Create()
            => (ISerializable)(Activator.CreateInstance(runtimeType, nonPublic: true)
                ?? throw new InvalidOperationException(
                    $"Project setting type '{runtimeType.FullName}' could not be created."));
    }
}
