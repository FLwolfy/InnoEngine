using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using Inno.Extensibility.Types;

namespace Inno.Assets.Pipeline;

internal sealed class AssetBuildProcessorRegistry
    : TypeRegistry<AssetBuildProcessorRegistry.Snapshot>
{
    private readonly TypeCatalog m_types;

    internal AssetBuildProcessorRegistry(TypeCatalog types)
        : base(types)
    {
        m_types = types;
    }

    internal AssetBuildProcessor? Find(Type definitionType)
        => current.byDefinitionType.GetValueOrDefault(definitionType);

    internal long snapshotVersion
    {
        get
        {
            _ = current;
            return m_types.current.version;
        }
    }

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
        Type[] discovered = types.GetTypesWithAttribute<AssetBuildProcessorAttribute>()
            .Select(typeRef => typeRef.Resolve(types))
            .OrderBy(static value => value.FullName, StringComparer.Ordinal)
            .ToArray();
        (Type type, string id)[] registrations = discovered.Select(static type =>
        {
            if (type.IsAbstract || !typeof(AssetBuildProcessor).IsAssignableFrom(type))
            {
                throw new InvalidOperationException(
                    $"Asset build processor metadata on '{type.FullName}' requires a concrete {nameof(AssetBuildProcessor)} subtype.");
            }
            return (type, type.GetCustomAttribute<AssetBuildProcessorAttribute>(inherit: false)!.id);
        }).ToArray();
        string? duplicateId = registrations
            .GroupBy(static registration => registration.id, StringComparer.Ordinal)
            .FirstOrDefault(static group => group.Count() > 1)?.Key;
        if (duplicateId is not null)
            throw new InvalidOperationException($"Asset build processor ID '{duplicateId}' is registered more than once.");

        var processors = new Dictionary<Type, AssetBuildProcessor>();
        foreach ((Type type, string processorId) in registrations)
        {
            AssetBuildProcessor processor = CreateExtension<AssetBuildProcessor>(type);
            processor.BindProcessorId(processorId);
            if (!processors.TryAdd(processor.definitionType, processor))
            {
                throw new InvalidOperationException(
                    $"Asset build definition '{processor.definitionType.FullName}' has multiple processors.");
            }
        }
        return new Snapshot(processors.ToFrozenDictionary());
    }

    /// <summary>
    /// Releases the generation lease retained by an immutable registry snapshot.
    /// </summary>
    /// <param name="snapshot">
    /// The immutable state snapshot consumed by this operation.
    /// </param>
    protected override void DisposeSnapshot(Snapshot snapshot)
        => DisposeExtensions(snapshot.byDefinitionType.Values);

    internal sealed record Snapshot(FrozenDictionary<Type, AssetBuildProcessor> byDefinitionType);
}
