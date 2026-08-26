using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;

using Inno.Core.Reflection;

namespace Inno.Assets.Loader;

internal sealed class AssetBuildProcessorRegistry
    : TypeRegistry<AssetBuildProcessorRegistry.Snapshot>
{
    internal AssetBuildProcessor? Find(Type definitionType)
        => current.byDefinitionType.GetValueOrDefault(definitionType);

    internal long snapshotVersion
    {
        get
        {
            _ = current;
            return TypeCacheManager.current.version;
        }
    }

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
