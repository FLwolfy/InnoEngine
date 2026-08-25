using System;
using System.Collections.Generic;
using System.Collections.Frozen;
using System.IO;
using System.Linq;

using Inno.Core.Reflection;

namespace Inno.Assets.Loader;

internal sealed class AssetImporterRegistry
    : TypeRegistry<AssetImporterRegistry.Snapshot>
{
    private readonly object m_generationSync = new();
    private readonly Dictionary<string, long> m_generations = new(StringComparer.Ordinal);
    private Dictionary<string, long>? m_generationRollback;

    internal AssetImporter? FindByPath(string relativePath)
    {
        string extension = Path.GetExtension(relativePath).ToLowerInvariant();
        return current.byExtension.GetValueOrDefault(extension);
    }

    internal AssetImporter? FindById(string importerId)
        => current.byId.GetValueOrDefault(importerId);

    internal long GetGeneration(string importerId)
    {
        _ = current;
        lock (m_generationSync)
            return m_generations.GetValueOrDefault(importerId);
    }

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
        Type[] discovered = types.GetTypesWithAttribute<AssetImporterExtensionAttribute>()
            .OrderBy(static value => value.FullName, StringComparer.Ordinal)
            .ToArray();
        var byExtension = new Dictionary<string, AssetImporter>(StringComparer.OrdinalIgnoreCase);
        var byId = new Dictionary<string, AssetImporter>(StringComparer.Ordinal);
        var typesById = new Dictionary<string, Type>(StringComparer.Ordinal);
        foreach (Type type in discovered)
        {
            AssetImporter importer = CreateExtension<AssetImporter>(type);
            if (string.IsNullOrWhiteSpace(importer.importerId))
                throw new InvalidOperationException($"Asset importer '{type.FullName}' has an empty importer id.");
            if (!byId.TryAdd(importer.importerId, importer))
            {
                throw new InvalidOperationException(
                    $"Asset importer id '{importer.importerId}' is registered by multiple importers.");
            }
            typesById.Add(importer.importerId, type);

            foreach (string declaredExtension in importer.supportedExtensions)
            {
                string extension = NormalizeExtension(declaredExtension);
                if (!byExtension.TryAdd(extension, importer))
                {
                    throw new InvalidOperationException(
                        $"Asset extension '{extension}' is registered by both " +
                        $"'{byExtension[extension].GetType().FullName}' and '{type.FullName}'.");
                }
            }
        }

        return new Snapshot(
            byExtension.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase),
            byId.ToFrozenDictionary(StringComparer.Ordinal),
            typesById.ToFrozenDictionary(StringComparer.Ordinal));
    }

    protected override void OnActivating(Snapshot? previous, Snapshot candidate)
    {
        lock (m_generationSync)
        {
            m_generationRollback = new Dictionary<string, long>(m_generations, StringComparer.Ordinal);
            foreach ((string importerId, Type importerType) in candidate.typesById)
            {
                if (previous is null ||
                    !previous.typesById.TryGetValue(importerId, out Type? previousType) ||
                    previousType != importerType)
                {
                    m_generations[importerId] = m_generations.GetValueOrDefault(importerId) + 1;
                }
            }
        }
    }

    protected override void OnActivationRolledBack(Snapshot? previous, Snapshot candidate)
    {
        lock (m_generationSync)
        {
            if (m_generationRollback is null)
                return;
            m_generations.Clear();
            foreach ((string importerId, long generation) in m_generationRollback)
                m_generations.Add(importerId, generation);
            m_generationRollback = null;
        }
    }

    protected override void OnActivationCompleted(Snapshot? previous, Snapshot currentSnapshot)
    {
        lock (m_generationSync)
            m_generationRollback = null;
    }

    protected override void DisposeSnapshot(Snapshot snapshot)
    {
        var disposed = new HashSet<object>(ReferenceEqualityComparer.Instance);
        foreach (AssetImporter importer in snapshot.byId.Values)
        {
            if (disposed.Add(importer) && importer is IDisposable disposable)
                disposable.Dispose();
        }
    }

    private static string NormalizeExtension(string extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
            throw new InvalidOperationException("Asset importer extensions cannot be empty.");
        string normalized = extension.Trim().ToLowerInvariant();
        return normalized[0] == '.' ? normalized : "." + normalized;
    }

    internal sealed record Snapshot(
        FrozenDictionary<string, AssetImporter> byExtension,
        FrozenDictionary<string, AssetImporter> byId,
        FrozenDictionary<string, Type> typesById);
}
