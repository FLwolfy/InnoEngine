using System;
using System.Collections.Generic;
using System.Linq;

using Inno.Assets.Core;
using Inno.Core.Diagnose;

namespace Inno.Assets.Loader;

internal sealed class AssetDiagnosticPublisher : IDisposable
{
    private const string C_BUILD_GROUP = "Asset Build";
    private const string C_CATALOG_GROUP = "Asset Catalog";
    private const string C_IMPORT_GROUP = "Asset Import";
    private const string C_REFERENCE_GROUP = "Asset Reference";

    private readonly Dictionary<Guid, string> m_buildStates = [];
    private readonly Dictionary<Guid, string> m_importStates = [];
    private readonly Dictionary<Guid, string> m_referenceStates = [];
    private string m_catalogState = string.Empty;

    internal void SynchronizeImports(IReadOnlyList<AssetMeta> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var currentIds = new HashSet<Guid>();
        for (int i = 0; i < entries.Count; i++)
        {
            AssetMeta entry = entries[i];
            if (entry.persistentId == Guid.Empty || entry.isDirectory)
                continue;

            if (entry.importStatus != (int)AssetImportStatus.Missing)
                ResolveReference(entry.persistentId);

            Diagnostic[] diagnostics = CreateImportDiagnostics(entry);
            if (diagnostics.Length == 0)
                continue;

            currentIds.Add(entry.persistentId);
            string state = CreateState(entry, diagnostics);
            if (m_importStates.TryGetValue(entry.persistentId, out string? previous) &&
                string.Equals(previous, state, StringComparison.Ordinal))
            {
                continue;
            }

            Diagnostics.Set(
                entry.persistentId,
                C_IMPORT_GROUP,
                diagnostics,
                entry.relativePath);
            m_importStates[entry.persistentId] = state;
        }

        Guid[] resolved = m_importStates.Keys
            .Where(id => !currentIds.Contains(id))
            .ToArray();
        for (int i = 0; i < resolved.Length; i++)
        {
            Diagnostics.Clear(resolved[i], C_IMPORT_GROUP);
            m_importStates.Remove(resolved[i]);
        }
    }

    internal void PublishBuild(
        Guid targetId,
        string displayName,
        IReadOnlyList<string> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);
        if (targetId == Guid.Empty)
            return;
        if (messages.Count == 0)
        {
            ResolveBuild(targetId);
            return;
        }

        Diagnostic[] diagnostics = messages
            .Where(static message => !string.IsNullOrWhiteSpace(message))
            .Select(static message => Diagnostic.Warning("ASSET-BUILD", message))
            .ToArray();
        if (diagnostics.Length == 0)
        {
            ResolveBuild(targetId);
            return;
        }

        string state = string.Join('\n', diagnostics.Select(static diagnostic => diagnostic.message));
        if (m_buildStates.TryGetValue(targetId, out string? previous) &&
            string.Equals(previous, state, StringComparison.Ordinal))
        {
            return;
        }
        Diagnostics.Set(targetId, C_BUILD_GROUP, diagnostics, displayName);
        m_buildStates[targetId] = state;
    }

    internal void PublishBuildFailure(Guid targetId, string displayName, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        if (targetId == Guid.Empty)
            return;
        string state = exception.ToString();
        if (m_buildStates.TryGetValue(targetId, out string? previous) &&
            string.Equals(previous, state, StringComparison.Ordinal))
        {
            return;
        }
        Diagnostics.Set(
            targetId,
            C_BUILD_GROUP,
            Diagnostic.Error("ASSET-BUILD", exception.Message),
            displayName);
        m_buildStates[targetId] = state;
    }

    internal bool PublishCatalogFailure(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        string state = exception.ToString();
        if (string.Equals(m_catalogState, state, StringComparison.Ordinal))
            return false;
        Diagnostics.Set(
            C_CATALOG_GROUP,
            Diagnostic.Error("ASSET-CATALOG", exception.Message));
        m_catalogState = state;
        return true;
    }

    internal void PublishMissingReference(
        Guid targetId,
        string displayName,
        Type expectedType)
    {
        if (targetId == Guid.Empty)
            return;
        ArgumentNullException.ThrowIfNull(expectedType);
        string resolvedDisplayName = string.IsNullOrWhiteSpace(displayName)
            ? targetId.ToString()
            : displayName;
        string state = $"{resolvedDisplayName}:{expectedType.FullName}";
        if (m_referenceStates.TryGetValue(targetId, out string? previous) &&
            string.Equals(previous, state, StringComparison.Ordinal))
        {
            return;
        }
        Diagnostics.Set(
            targetId,
            C_REFERENCE_GROUP,
            Diagnostic.Warning(
                "ASSET-REFERENCE",
                $"Asset '{resolvedDisplayName}' required as '{expectedType.FullName}' is missing."),
            resolvedDisplayName);
        m_referenceStates[targetId] = state;
    }

    internal void ResolveCatalog()
    {
        if (string.IsNullOrEmpty(m_catalogState))
            return;
        Diagnostics.Clear(C_CATALOG_GROUP);
        m_catalogState = string.Empty;
    }

    public void Dispose()
    {
        ResolveCatalog();
        foreach (Guid targetId in m_importStates.Keys)
            Diagnostics.Clear(targetId, C_IMPORT_GROUP);
        foreach (Guid targetId in m_buildStates.Keys)
            Diagnostics.Clear(targetId, C_BUILD_GROUP);
        foreach (Guid targetId in m_referenceStates.Keys)
            Diagnostics.Clear(targetId, C_REFERENCE_GROUP);
        m_importStates.Clear();
        m_buildStates.Clear();
        m_referenceStates.Clear();
    }

    private void ResolveBuild(Guid targetId)
    {
        if (!m_buildStates.Remove(targetId))
            return;
        Diagnostics.Clear(targetId, C_BUILD_GROUP);
    }

    private void ResolveReference(Guid targetId)
    {
        if (!m_referenceStates.Remove(targetId))
            return;
        Diagnostics.Clear(targetId, C_REFERENCE_GROUP);
    }

    private static Diagnostic[] CreateImportDiagnostics(AssetMeta entry)
    {
        AssetImportStatus status = Enum.IsDefined(typeof(AssetImportStatus), entry.importStatus)
            ? (AssetImportStatus)entry.importStatus
            : AssetImportStatus.Failed;
        DiagnosticLocation? location = string.IsNullOrWhiteSpace(entry.relativePath)
            ? null
            : new DiagnosticLocation(entry.relativePath);
        string[] messages = entry.diagnostics
            .Where(static message => !string.IsNullOrWhiteSpace(message))
            .ToArray();
        if (messages.Length == 0 || status == AssetImportStatus.Missing)
            return [];
        return messages.Select(message => status switch
        {
            AssetImportStatus.Failed => Diagnostic.Error("ASSET-IMPORT", message, location),
            AssetImportStatus.Conflict => Diagnostic.Error("ASSET-CONFLICT", message, location),
            _ => Diagnostic.Warning("ASSET-IMPORT", message, location)
        }).ToArray();
    }

    private static string CreateState(AssetMeta entry, IReadOnlyList<Diagnostic> diagnostics)
        => $"{entry.importStatus}:{entry.relativePath}:{string.Join('\n', diagnostics.Select(static value => value.message))}";
}
