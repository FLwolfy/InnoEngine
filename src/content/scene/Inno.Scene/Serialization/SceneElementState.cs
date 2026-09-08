using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Inno.Assets;
using Inno.Core.Serialization;

namespace Inno.Scene;

internal sealed class SceneElementState : ISerializable
{
    [SerializableProperty] internal Guid typeId { get; set; }
    [SerializableProperty] internal string typeName { get; set; } = string.Empty;
    [SerializableProperty] internal byte[] properties { get; set; } = [];
    [SerializableProperty] internal Guid[] dependencyIds { get; set; } = [];
    [SerializableProperty] internal Guid[] dependencyTypes { get; set; } = [];
    [SerializableProperty] internal string[] dependencyPaths { get; set; } = [];
    [SerializableProperty] internal Guid[] aliasIds { get; set; } = [];
    [SerializableProperty] internal Guid[] aliasTargets { get; set; } = [];

    internal void SetDependencies(IEnumerable<AssetDependency> dependencies)
    {
        AssetDependency[] snapshot = dependencies.OrderBy(static value => value.persistentId).ToArray();
        dependencyIds = snapshot.Select(static value => value.persistentId).ToArray();
        dependencyTypes = snapshot.Select(static value => value.type.stableId).ToArray();
        dependencyPaths = snapshot.Select(static value => value.lastKnownPath).ToArray();
    }

    internal void SetAliases(IReadOnlyDictionary<Guid, Guid> aliases)
    {
        KeyValuePair<Guid, Guid>[] snapshot = aliases.OrderBy(static pair => pair.Key).ToArray();
        aliasIds = snapshot.Select(static pair => pair.Key).ToArray();
        aliasTargets = snapshot.Select(static pair => pair.Value).ToArray();
    }

    internal AssetDependency[] GetDependencies()
        => dependencyIds.Select((id, index) => new AssetDependency(id,
            new Inno.Extensibility.Types.TypeRef(dependencyTypes[index]), dependencyPaths[index])).ToArray();

    internal IReadOnlyDictionary<Guid, Guid> GetAliases()
        => aliasIds.Select((id, index) => new KeyValuePair<Guid, Guid>(id, aliasTargets[index])).ToDictionary();

    internal void Validate()
    {
        if (typeId == Guid.Empty || string.IsNullOrWhiteSpace(typeName) || properties is null ||
            dependencyIds is null || dependencyTypes is null || dependencyPaths is null ||
            aliasIds is null || aliasTargets is null ||
            dependencyIds.Length != dependencyTypes.Length || dependencyIds.Length != dependencyPaths.Length ||
            aliasIds.Length != aliasTargets.Length ||
            dependencyIds.Any(static id => id == Guid.Empty) ||
            dependencyIds.Distinct().Count() != dependencyIds.Length ||
            aliasIds.Any(static id => id == Guid.Empty) || aliasTargets.Any(static id => id == Guid.Empty) ||
            aliasIds.Distinct().Count() != aliasIds.Length)
            throw new InvalidDataException("Scene element state contains invalid type, dependency or alias metadata.");
    }
}
