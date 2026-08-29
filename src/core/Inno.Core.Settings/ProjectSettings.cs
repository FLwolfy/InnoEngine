using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Inno.Core.Serialization;

namespace Inno.Core.Settings;

/// <summary>Composes setting defaults, Plugin contributions, and project overrides atomically.</summary>
public sealed class ProjectSettings : IDisposable
{
    private readonly string m_documentPath;
    private readonly ProjectSettingsRegistry m_registry = new();
    private readonly Dictionary<ProjectSettingId, ISerializable> m_effective = [];
    private ProjectSettingsDocument m_document;
    private bool m_disposed;

    /// <summary>Loads one project settings document and builds the initial effective snapshot.</summary>
    /// <param name="documentPath">ProjectSettings.inno path.</param>
    /// <param name="contributors">Dependency-ordered default contributors.</param>
    public ProjectSettings(
        string documentPath,
        IReadOnlyList<ProjectSettingsContributor>? contributors = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentPath);
        m_documentPath = Path.GetFullPath(documentPath);
        m_document = File.Exists(m_documentPath)
            ? SerializationManager.Deserialize<ProjectSettingsDocument>(File.ReadAllBytes(m_documentPath))
            : new ProjectSettingsDocument();
        Rebuild(contributors ?? [], allowUnresolvedContributions: true);
    }

    /// <summary>Gets an isolated snapshot of a current-generation setting.</summary>
    /// <typeparam name="TSetting">Expected settings contract.</typeparam>
    /// <param name="id">Stable setting identifier.</param>
    /// <returns>An independently owned snapshot of the effective current-generation value.</returns>
    /// <exception cref="KeyNotFoundException">Thrown when no definition exists.</exception>
    /// <exception cref="InvalidCastException">Thrown when the definition does not implement the requested contract.</exception>
    public TSetting Get<TSetting>(ProjectSettingId id) where TSetting : class, ISerializable
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
        if (!TryClone(id, out ISerializable? value) || value is null)
            throw new KeyNotFoundException($"Project setting '{id}' is not defined.");
        return value as TSetting ?? throw new InvalidCastException(
            $"Project setting '{id}' is '{value.GetType().FullName}', not '{typeof(TSetting).FullName}'.");
    }

    /// <summary>Tries to get an isolated snapshot of a current-generation setting.</summary>
    /// <typeparam name="TSetting">Expected settings contract.</typeparam>
    /// <param name="id">Stable setting identifier.</param>
    /// <param name="setting">Receives an independently owned effective snapshot.</param>
    /// <returns><see langword="true"/> when a compatible definition exists.</returns>
    public bool TryGet<TSetting>(ProjectSettingId id, out TSetting? setting)
        where TSetting : class, ISerializable
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
        if (!TryClone(id, out ISerializable? value))
        {
            setting = null;
            return false;
        }
        setting = value as TSetting;
        return setting is not null;
    }

    /// <summary>
    /// Captures and validates the project-authored delta as one prospective Plugin contribution.
    /// </summary>
    /// <param name="id">Stable setting identifier.</param>
    /// <param name="contributorId">Stable identity of the Plugin being exported.</param>
    /// <param name="declaredDependencies">Direct dependency Plugin IDs declared by the exported Plugin.</param>
    /// <param name="declaredOverrides">Dependency Plugin IDs whose owned values may be replaced.</param>
    /// <param name="contributors">All currently active dependency-ordered contributors.</param>
    /// <param name="record">Receives the normalized semantic Plugin contribution.</param>
    /// <returns><see langword="true"/> when the project document contains an effective semantic delta.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when a replacement setting depends on an undeclared contributor, or when the authored delta is not
    /// permitted by the declared dependency and override ownership.
    /// </exception>
    public bool TryCapture(
        ProjectSettingId id,
        string contributorId,
        IReadOnlySet<string> declaredDependencies,
        IReadOnlySet<string> declaredOverrides,
        IReadOnlyList<ProjectSettingsContributor> contributors,
        out ProjectSettingRecord record)
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(contributorId);
        ArgumentNullException.ThrowIfNull(declaredDependencies);
        ArgumentNullException.ThrowIfNull(declaredOverrides);
        ArgumentNullException.ThrowIfNull(contributors);
        if (!declaredOverrides.All(declaredDependencies.Contains))
            throw new ArgumentException("Every explicit override must identify a direct dependency.", nameof(declaredOverrides));
        ValidateContributorOrder(contributors);
        ProjectSettingsRegistry.Snapshot registry = m_registry.settings;
        if (!registry.TryGet(id, out ProjectSettingsRegistry.Definition? definition))
        {
            record = default;
            return false;
        }
        ProjectSettingRecord? projectRecord = FindRecord(m_document.overrides, id);
        if (projectRecord is not ProjectSettingRecord authored)
        {
            record = default;
            return false;
        }
        ValidateRecord(id, definition, authored);

        if (contributors.Any(contributor => string.Equals(contributor.id, contributorId, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException(
                $"Cannot export Plugin '{contributorId}' while a Plugin with the same ID is active.");
        }
        var prospective = new ProjectSettingsContributor(
            contributorId,
            declaredDependencies,
            declaredOverrides,
            [authored]);
        ProjectSettingsContributor[] validationContributors = [.. contributors, prospective];
        _ = ComposeSetting(id, definition, validationContributors, projectRecord: null);

        IReadOnlySet<string> closure = ExpandContributorClosure(declaredDependencies, contributors);
        ProjectSettingsContributor[] baselineContributors = contributors
            .Where(contributor => closure.Contains(contributor.id))
            .ToArray();
        ISerializable baseline = ComposeSetting(id, definition, baselineContributors, projectRecord: null);
        ProjectSettingsContributor[] candidateContributors =
        [
            .. baselineContributors,
            prospective
        ];
        ISerializable candidate = ComposeSetting(id, definition, candidateContributors, projectRecord: null);
        return TryCreateRecord(id, definition, baseline, candidate, out record);
    }

    /// <summary>Gets whether the project document explicitly contributes to one setting protocol.</summary>
    /// <param name="id">Stable setting identifier.</param>
    /// <returns><see langword="true"/> when a project contribution record exists.</returns>
    public bool HasProjectOverride(ProjectSettingId id)
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
        return m_document.overrides.Any(candidate => candidate.id == id);
    }

    /// <summary>Creates an isolated editable copy of one effective setting.</summary>
    /// <param name="id">Stable setting identifier.</param>
    /// <param name="setting">Receives a generation-local editable copy.</param>
    /// <returns><see langword="true"/> when the setting is defined.</returns>
    public bool TryClone(ProjectSettingId id, out ISerializable? setting)
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
        if (!m_effective.TryGetValue(id, out ISerializable? value)
            || !m_registry.settings.TryGet(id, out ProjectSettingsRegistry.Definition? definition))
        {
            setting = null;
            return false;
        }
        setting = Clone(definition, value);
        return true;
    }

    /// <summary>Creates an isolated copy composed without the project-authored contribution.</summary>
    /// <param name="id">Stable setting identifier.</param>
    /// <param name="contributors">Dependency-ordered Plugin default contributors.</param>
    /// <param name="setting">Receives the composed default value.</param>
    /// <returns><see langword="true"/> when the setting is defined.</returns>
    public bool TryCloneComposedDefault(
        ProjectSettingId id,
        IReadOnlyList<ProjectSettingsContributor> contributors,
        out ISerializable? setting)
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
        ArgumentNullException.ThrowIfNull(contributors);
        ValidateContributorOrder(contributors);
        if (!m_registry.settings.TryGet(id, out ProjectSettingsRegistry.Definition? definition))
        {
            setting = null;
            return false;
        }
        setting = ComposeSetting(id, definition, contributors, projectRecord: null);
        return true;
    }

    /// <summary>Captures the native project contribution document.</summary>
    /// <returns>A newly owned native document payload.</returns>
    public byte[] CaptureDocument()
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
        return SerializationManager.Serialize(m_document);
    }

    /// <summary>Atomically rebuilds effective settings for a new extension generation.</summary>
    /// <param name="contributors">Dependency-ordered default contributors.</param>
    /// <param name="allowUnresolvedContributions">Whether contributions awaiting Plugin type activation are retained but skipped.</param>
    public void Rebuild(
        IReadOnlyList<ProjectSettingsContributor> contributors,
        bool allowUnresolvedContributions = false)
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
        ArgumentNullException.ThrowIfNull(contributors);
        ValidateContributorOrder(contributors);
        ProjectSettingsRegistry.Snapshot registry = m_registry.settings;
        foreach (ProjectSettingsContributor contributor in contributors)
        {
            foreach (ProjectSettingRecord setting in contributor.settings)
            {
                if (!registry.TryGet(setting.id, out _) && !allowUnresolvedContributions)
                    throw new InvalidOperationException($"Project setting '{setting.id}' has no active definition.");
            }
        }

        var candidate = new Dictionary<ProjectSettingId, ISerializable>();
        foreach ((ProjectSettingId id, ProjectSettingsRegistry.Definition definition) in registry.definitions)
        {
            ProjectSettingRecord? projectRecord = FindRecord(m_document.overrides, id);
            candidate.Add(id, ComposeSetting(id, definition, contributors, projectRecord));
        }

        m_effective.Clear();
        foreach ((ProjectSettingId id, ISerializable value) in candidate)
            m_effective.Add(id, value);
    }

    /// <summary>Persists one project-authored semantic contribution and rebuilds the effective value.</summary>
    /// <param name="id">Stable setting identifier.</param>
    /// <param name="value">Current generation setting value.</param>
    /// <param name="contributors">Dependency-ordered defaults used to rebuild.</param>
    public void SetProjectOverride(
        ProjectSettingId id,
        ISerializable value,
        IReadOnlyList<ProjectSettingsContributor> contributors)
    {
        _ = ApplyProjectOverrides(
            new Dictionary<ProjectSettingId, ISerializable> { [id] = value },
            resets: null,
            contributors);
    }

    /// <summary>Applies multiple project contributions and removals as one atomic document update.</summary>
    /// <param name="values">Complete authored values keyed by stable setting identity.</param>
    /// <param name="resets">Setting identities whose project-authored contributions are removed.</param>
    /// <param name="contributors">Dependency-ordered defaults used to rebuild effective values.</param>
    /// <returns><see langword="true"/> when the project document changed.</returns>
    public bool ApplyProjectOverrides(
        IReadOnlyDictionary<ProjectSettingId, ISerializable> values,
        IReadOnlySet<ProjectSettingId>? resets,
        IReadOnlyList<ProjectSettingsContributor> contributors)
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(contributors);
        ValidateContributorOrder(contributors);

        ProjectSettingsRegistry.Snapshot registry = m_registry.settings;
        var records = m_document.overrides.ToDictionary(static record => record.id);
        bool changed = false;
        if (resets is not null)
        {
            foreach (ProjectSettingId id in resets)
                changed |= records.Remove(id);
        }

        foreach ((ProjectSettingId id, ISerializable value) in values)
        {
            ArgumentNullException.ThrowIfNull(value);
            if (resets?.Contains(id) == true)
                continue;
            if (!registry.TryGet(id, out ProjectSettingsRegistry.Definition? definition)
                || definition.runtimeType != value.GetType())
            {
                throw new ArgumentException(
                    $"Value type does not match project setting '{id}'.",
                    nameof(values));
            }
            ISerializable baseline = ComposeSetting(id, definition, contributors, projectRecord: null);
            if (!TryCreateRecord(id, definition, baseline, value, out ProjectSettingRecord candidate))
            {
                changed |= records.Remove(id);
                continue;
            }
            if (records.TryGetValue(id, out ProjectSettingRecord current) && RecordsEqual(current, candidate))
                continue;
            records[id] = candidate;
            changed = true;
        }

        if (!changed)
            return false;
        var candidateDocument = new ProjectSettingsDocument
        {
            overrides = records.Values
                .OrderBy(static record => record.id.value, StringComparer.Ordinal)
                .ToArray()
        };
        ReplaceDocument(candidateDocument, contributors);
        return true;
    }

    /// <summary>Atomically restores a native project settings document.</summary>
    /// <param name="document">Native document bytes.</param>
    /// <param name="contributors">Dependency-ordered defaults used to rebuild effective values.</param>
    public void RestoreDocument(
        ReadOnlySpan<byte> document,
        IReadOnlyList<ProjectSettingsContributor> contributors)
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
        ArgumentNullException.ThrowIfNull(contributors);
        ProjectSettingsDocument candidate = SerializationManager.Deserialize<ProjectSettingsDocument>(document);
        ReplaceDocument(candidate, contributors);
    }

    /// <summary>Releases registry snapshots and generation-local setting objects.</summary>
    public void Dispose()
    {
        if (m_disposed)
            return;
        m_effective.Clear();
        m_registry.Dispose();
        m_disposed = true;
    }

    private static ISerializable Clone(
        ProjectSettingsRegistry.Definition definition,
        ISerializable value)
    {
        ISerializable result = definition.Create();
        _ = SerializationManager.RestorePropertiesData(
            result,
            SerializationManager.CapturePropertiesData(value));
        return result;
    }

    private static ProjectSettingRecord? FindRecord(
        ProjectSettingsContributor contributor,
        ProjectSettingId id)
        => FindRecord(contributor.settings, id);

    private static ProjectSettingRecord? FindRecord(
        IEnumerable<ProjectSettingRecord> records,
        ProjectSettingId id)
    {
        ProjectSettingRecord? result = null;
        foreach (ProjectSettingRecord record in records)
        {
            if (record.id != id)
                continue;
            if (result is not null)
                throw new InvalidOperationException($"Project setting '{id}' is contributed more than once by one owner.");
            result = record;
        }
        return result;
    }

    private static IReadOnlySet<string> ExpandContributorClosure(
        IReadOnlySet<string> directDependencies,
        IReadOnlyList<ProjectSettingsContributor> contributors)
    {
        var byId = contributors.ToDictionary(static contributor => contributor.id, StringComparer.Ordinal);
        var result = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<string>(directDependencies);
        while (pending.Count > 0)
        {
            string id = pending.Pop();
            if (!result.Add(id))
                continue;
            if (!byId.TryGetValue(id, out ProjectSettingsContributor? contributor))
                throw new InvalidOperationException($"Declared Plugin dependency '{id}' is not active.");
            foreach (string dependency in contributor.dependencies)
                pending.Push(dependency);
        }
        return result;
    }

    private static void ValidateContributorOrder(IReadOnlyList<ProjectSettingsContributor> contributors)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (ProjectSettingsContributor contributor in contributors)
        {
            if (!seen.Add(contributor.id))
                throw new InvalidOperationException($"Settings contributor '{contributor.id}' is declared more than once.");
            if (!contributor.dependencies.All(seen.Contains))
                throw new InvalidOperationException($"Settings contributor '{contributor.id}' is not dependency-ordered.");
        }
    }

    private static bool RecordsEqual(ProjectSettingRecord left, ProjectSettingRecord right)
        => left.id == right.id
           && left.stableTypeId == right.stableTypeId
           && (left.propertyData ?? []).AsSpan().SequenceEqual(right.propertyData ?? []);

    private static void ValidateRecord(
        ProjectSettingId id,
        ProjectSettingsRegistry.Definition definition,
        ProjectSettingRecord record)
    {
        if (record.stableTypeId != definition.stableTypeId)
            throw new InvalidOperationException($"Project setting '{id}' has an incompatible stable type.");
        if (record.propertyData is null || record.propertyData.Length == 0)
            throw new InvalidOperationException($"Project setting '{id}' contains an empty contribution payload.");
    }

    private ISerializable ComposeSetting(
        ProjectSettingId id,
        ProjectSettingsRegistry.Definition definition,
        IReadOnlyList<ProjectSettingsContributor> contributors,
        ProjectSettingRecord? projectRecord)
    {
        if (definition.composer is null)
            return ComposeReplacement(id, definition, contributors, projectRecord);

        var entries = new List<ProjectSettingCompositionEntry>();
        foreach (ProjectSettingsContributor contributor in contributors)
        {
            ProjectSettingRecord? record = FindRecord(contributor, id);
            if (record is not ProjectSettingRecord contribution)
                continue;
            ValidateRecord(id, definition, contribution);
            entries.Add(new ProjectSettingCompositionEntry(
                new ProjectSettingContributionContext(
                    contributor.id,
                    ProjectSettingContributionSource.Plugin,
                    contributor.dependencies.ToHashSet(StringComparer.Ordinal),
                    contributor.overrides.ToHashSet(StringComparer.Ordinal)),
                contribution.propertyData));
        }
        if (projectRecord is ProjectSettingRecord project)
        {
            ValidateRecord(id, definition, project);
            entries.Add(new ProjectSettingCompositionEntry(
                new ProjectSettingContributionContext(
                    "project",
                    ProjectSettingContributionSource.Project,
                    new HashSet<string>(StringComparer.Ordinal),
                    new HashSet<string>(StringComparer.Ordinal)),
                project.propertyData));
        }
        return definition.composer.Compose(definition.Create(), entries);
    }

    private static ISerializable ComposeReplacement(
        ProjectSettingId id,
        ProjectSettingsRegistry.Definition definition,
        IReadOnlyList<ProjectSettingsContributor> contributors,
        ProjectSettingRecord? projectRecord)
    {
        string owner = "host";
        ProjectSettingRecord? selected = null;
        foreach (ProjectSettingsContributor contributor in contributors)
        {
            ProjectSettingRecord? record = FindRecord(contributor, id);
            if (record is not ProjectSettingRecord contribution)
                continue;
            ValidateRecord(id, definition, contribution);
            if (owner != "host"
                && (!contributor.dependencies.Contains(owner, StringComparer.Ordinal)
                    || !contributor.overrides.Contains(owner, StringComparer.Ordinal)))
            {
                throw new InvalidOperationException(
                    $"Settings contributors '{owner}' and '{contributor.id}' conflict on '{id}'.");
            }
            owner = contributor.id;
            selected = contribution;
        }
        if (projectRecord is ProjectSettingRecord project)
        {
            ValidateRecord(id, definition, project);
            selected = project;
        }

        ISerializable result = definition.Create();
        if (selected is ProjectSettingRecord value)
            _ = SerializationManager.RestorePropertiesData(result, value.propertyData);
        return result;
    }

    private static bool TryCreateRecord(
        ProjectSettingId id,
        ProjectSettingsRegistry.Definition definition,
        ISerializable baseline,
        ISerializable value,
        out ProjectSettingRecord record)
    {
        if (definition.runtimeType != baseline.GetType() || definition.runtimeType != value.GetType())
            throw new ArgumentException($"Value type does not match project setting '{id}'.", nameof(value));

        byte[] payload;
        if (definition.composer is null)
        {
            byte[] baselineData = SerializationManager.CapturePropertiesData(baseline);
            payload = SerializationManager.CapturePropertiesData(value);
            if (baselineData.AsSpan().SequenceEqual(payload))
            {
                record = default;
                return false;
            }
        }
        else if (!definition.composer.TryCapture(baseline, value, out payload))
        {
            record = default;
            return false;
        }
        record = new ProjectSettingRecord(id, definition.stableTypeId, payload);
        return true;
    }

    private static void WriteAtomic(string path, byte[] data)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporaryPath = path + ".tmp-" + Guid.NewGuid().ToString("N");
        File.WriteAllBytes(temporaryPath, data);
        File.Move(temporaryPath, path, overwrite: true);
    }

    private void ReplaceDocument(
        ProjectSettingsDocument candidate,
        IReadOnlyList<ProjectSettingsContributor> contributors)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ProjectSettingsDocument previous = m_document;
        byte[] previousBytes = SerializationManager.Serialize(previous);
        byte[] candidateBytes = SerializationManager.Serialize(candidate);
        WriteAtomic(m_documentPath, candidateBytes);
        m_document = candidate;
        try
        {
            Rebuild(contributors);
        }
        catch
        {
            m_document = previous;
            WriteAtomic(m_documentPath, previousBytes);
            Rebuild(contributors);
            throw;
        }
    }
}
