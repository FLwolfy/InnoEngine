using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Inno.Core.Reflection;

namespace Inno.Assets.Loader;

internal sealed class AssetImporterRegistry
{
    private readonly object m_sync = new();
    private Dictionary<string, AssetImporter> m_byExtension = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, AssetImporter> m_byId = new(StringComparer.Ordinal);
    private Type[] m_registrationTypes = [];

    internal AssetImporter? FindByPath(string relativePath)
    {
        EnsureFresh();
        string extension = Path.GetExtension(relativePath).ToLowerInvariant();
        lock (m_sync)
            return m_byExtension.GetValueOrDefault(extension);
    }

    internal AssetImporter? FindById(string importerId)
    {
        EnsureFresh();
        lock (m_sync)
            return m_byId.GetValueOrDefault(importerId);
    }

    internal void EnsureFresh()
    {
        Type[] discovered = TypeCache.GetTypesWithAttribute<AssetImporterExtensionAttribute>()
            .OrderBy(static value => value.FullName, StringComparer.Ordinal)
            .ToArray();
        lock (m_sync)
        {
            if (m_registrationTypes.SequenceEqual(discovered))
                return;

            var byExtension = new Dictionary<string, AssetImporter>(StringComparer.OrdinalIgnoreCase);
            var byId = new Dictionary<string, AssetImporter>(StringComparer.Ordinal);
            foreach (Type type in discovered)
            {
                if (type.IsAbstract || !typeof(AssetImporter).IsAssignableFrom(type))
                {
                    throw new InvalidOperationException(
                        $"Asset importer extension '{type.FullName}' must be a non-abstract AssetImporter.");
                }

                AssetImporter importer;
                try
                {
                    importer = (AssetImporter)(Activator.CreateInstance(type, nonPublic: true)
                        ?? throw new InvalidOperationException("Activator returned null."));
                }
                catch (Exception exception)
                {
                    throw new InvalidOperationException(
                        $"Asset importer '{type.FullName}' requires a parameterless constructor.",
                        exception);
                }

                if (string.IsNullOrWhiteSpace(importer.importerId))
                    throw new InvalidOperationException($"Asset importer '{type.FullName}' has an empty importer id.");
                if (!byId.TryAdd(importer.importerId, importer))
                {
                    throw new InvalidOperationException(
                        $"Asset importer id '{importer.importerId}' is registered by multiple importers.");
                }

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

            m_byExtension = byExtension;
            m_byId = byId;
            m_registrationTypes = discovered;
        }
    }

    private static string NormalizeExtension(string extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
            throw new InvalidOperationException("Asset importer extensions cannot be empty.");
        string normalized = extension.Trim().ToLowerInvariant();
        return normalized[0] == '.' ? normalized : "." + normalized;
    }
}
