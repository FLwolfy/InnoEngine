using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;

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
        Type[] discovered = types.GetTypesWithAttribute<AssetBuildProcessorExtensionAttribute>()
            .Select(typeRef => typeRef.Resolve(types))
            .OrderBy(static value => value.FullName, StringComparer.Ordinal)
            .ToArray();
        var processors = new Dictionary<Type, AssetBuildProcessor>();
        foreach (Type type in discovered)
        {
            AssetBuildProcessor processor = CreateExtension<AssetBuildProcessor>(type);
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
    {
        foreach (AssetBuildProcessor processor in snapshot.byDefinitionType.Values)
        {
            if (processor is IDisposable disposable)
            {
                try
                {
                    disposable.Dispose();
                }
                catch (Exception exception)
                {
                    OnCleanupFailed(
                        $"disposing asset build processor '{processor.GetType().FullName}'",
                        exception);
                }
            }
        }
    }

    internal sealed record Snapshot(FrozenDictionary<Type, AssetBuildProcessor> byDefinitionType);
}
