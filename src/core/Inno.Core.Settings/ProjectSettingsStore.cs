using System;
using System.Collections.Generic;
using System.Linq;
using Inno.Extensibility.Types;
using Inno.Core.Serialization;
using Inno.Scripting.Api;

namespace Inno.Core.Settings;

/// <summary>
/// Owns one project's effective settings, contributors, persistence, and generation revision.
/// </summary>
public sealed class ProjectSettingsStore : IDisposable, IProjectSettingsLookup
{
    private readonly object m_sync = new();
    private readonly SerializationRegistry m_serialization;
    private IReadOnlyList<ProjectSettingsContributor> m_contributors = [];
    private ProjectSettings? m_current;
    private long m_revision;

    /// <summary>
    /// Creates a project settings store from one type and serialization generation owner.
    /// </summary>
    /// <param name="documentPath">
    /// The absolute path of the current project settings document.
    /// </param>
    /// <param name="types">
    /// The type catalog that owns setting definitions and composers.
    /// </param>
    /// <param name="serialization">
    /// The serialization registry used for documents and contribution payloads.
    /// </param>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="documentPath"/> is empty.
    /// </exception>
    /// <exception cref="ArgumentNullException">
    /// Thrown when a service dependency is null.
    /// </exception>
    public ProjectSettingsStore(
        string documentPath,
        TypeCatalog types,
        SerializationRegistry serialization)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentPath);
        ArgumentNullException.ThrowIfNull(types);
        ArgumentNullException.ThrowIfNull(serialization);
        m_serialization = serialization;
        m_current = new ProjectSettings(documentPath, types, serialization);
        m_revision = 1;
    }

    /// <summary>
    /// Gets whether project settings are initialized.
    /// </summary>
    public bool isInitialized
    {
        get
        {
            lock (m_sync)
                return m_current is not null;
        }
    }

    /// <summary>
    /// Gets the monotonic revision of the active effective settings snapshot.
    /// Runtime extensions may compare this value without registering reload-unsafe static delegates.
    /// </summary>
    public long revision
    {
        get
        {
            lock (m_sync)
                return m_revision;
        }
    }

    /// <summary>
    /// Binds this settings store to the current asynchronous script execution context.
    /// </summary>
    /// <returns>
    /// A strict last-in-first-out execution scope owned by the caller.
    /// </returns>
    /// <exception cref="ObjectDisposedException">
    /// Thrown after this settings store has been disposed.
    /// </exception>
    public IDisposable EnterExecutionScope()
    {
        lock (m_sync)
        {
            _ = RequireCurrent();
            return ProjectSettingsExecutionContext.EnterScope(this);
        }
    }

    /// <summary>
    /// Gets an isolated effective setting snapshot from the active extension generation.
    /// </summary>
    /// <typeparam name="TSetting">
    /// Expected setting contract.
    /// </typeparam>
    /// <param name="id">
    /// Stable setting protocol identity.
    /// </param>
    /// <returns>
    /// An independently owned effective setting snapshot.
    /// </returns>
    public TSetting Get<TSetting>(ProjectSettingId id)
        where TSetting : class, ISerializable
    {
        lock (m_sync)
            return RequireCurrent().Get<TSetting>(id);
    }

    /// <summary>
    /// Tries to get an isolated effective setting snapshot from the active extension generation.
    /// </summary>
    /// <typeparam name="TSetting">
    /// Expected setting contract.
    /// </typeparam>
    /// <param name="id">
    /// Stable setting protocol identity.
    /// </param>
    /// <param name="setting">
    /// Receives an independently owned effective snapshot when available.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when a compatible setting exists.
    /// </returns>
    public bool TryGet<TSetting>(ProjectSettingId id, out TSetting? setting)
        where TSetting : class, ISerializable
    {
        lock (m_sync)
        {
            if (m_current is null)
            {
                setting = null;
                return false;
            }
            return m_current.TryGet(id, out setting);
        }
    }

    /// <summary>
    /// Gets whether the active project document explicitly overrides one setting protocol.
    /// </summary>
    /// <param name="id">
    /// Stable setting protocol identity.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the project owns an override record.
    /// </returns>
    [ScriptingApiIgnore]
    public bool HasProjectOverride(ProjectSettingId id)
    {
        lock (m_sync)
            return RequireCurrent().HasProjectOverride(id);
    }

    /// <summary>
    /// Captures one normalized Plugin setting contribution from the project-authored delta.
    /// </summary>
    /// <param name="id">
    /// Stable setting protocol identity.
    /// </param>
    /// <param name="contributorId">
    /// Stable identity of the Plugin being exported.
    /// </param>
    /// <param name="declaredDependencies">
    /// Direct dependency Plugin IDs declared by the exported Plugin.
    /// </param>
    /// <param name="declaredOverrides">
    /// Dependency Plugin IDs whose owned values may be replaced.
    /// </param>
    /// <param name="record">
    /// Receives the normalized semantic contribution payload.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the project document contains an effective semantic delta.
    /// </returns>
    [ScriptingApiIgnore]
    public bool TryCapture(
        ProjectSettingId id,
        string contributorId,
        IReadOnlySet<string> declaredDependencies,
        IReadOnlySet<string> declaredOverrides,
        out ProjectSettingRecord record)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contributorId);
        ArgumentNullException.ThrowIfNull(declaredDependencies);
        ArgumentNullException.ThrowIfNull(declaredOverrides);
        lock (m_sync)
        {
            return RequireCurrent().TryCapture(
                id,
                contributorId,
                declaredDependencies,
                declaredOverrides,
                m_contributors,
                out record);
        }
    }

    /// <summary>
    /// Creates an isolated editable copy of one effective setting.
    /// </summary>
    /// <param name="id">
    /// Stable setting protocol identity.
    /// </param>
    /// <param name="setting">
    /// Receives an isolated native setting object.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the setting is defined.
    /// </returns>
    [ScriptingApiIgnore]
    public bool TryClone(ProjectSettingId id, out ISerializable? setting)
    {
        lock (m_sync)
        {
            if (m_current is null)
            {
                setting = null;
                return false;
            }
            return m_current.TryClone(id, out setting);
        }
    }

    /// <summary>
    /// Creates an isolated setting value without the project-authored override.
    /// </summary>
    /// <param name="id">
    /// Stable setting protocol identity.
    /// </param>
    /// <param name="setting">
    /// Receives the composed host and Plugin default value.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the setting is defined.
    /// </returns>
    [ScriptingApiIgnore]
    public bool TryCloneComposedDefault(ProjectSettingId id, out ISerializable? setting)
    {
        lock (m_sync)
        {
            if (m_current is null)
            {
                setting = null;
                return false;
            }
            return m_current.TryCloneComposedDefault(id, m_contributors, out setting);
        }
    }

    /// <summary>
    /// Captures the current native project override document.
    /// </summary>
    /// <returns>
    /// A newly owned native document payload.
    /// </returns>
    [ScriptingApiIgnore]
    public byte[] CaptureDocument()
    {
        lock (m_sync)
            return RequireCurrent().CaptureDocument();
    }

    /// <summary>
    /// Rebuilds settings for one dependency-ordered extension generation.
    /// </summary>
    /// <param name="contributors">
    /// Host and Plugin default contributions.
    /// </param>
    [ScriptingApiIgnore]
    public void Rebuild(IReadOnlyList<ProjectSettingsContributor> contributors)
    {
        ArgumentNullException.ThrowIfNull(contributors);
        lock (m_sync)
        {
            RequireCurrent().Rebuild(contributors);
            m_contributors = contributors.ToArray();
            m_revision++;
        }
    }

    /// <summary>
    /// Publishes dependency-ordered default contributors for the next current-generation rebuild without
    /// constructing setting instances from types that may still be awaiting assembly activation.
    /// </summary>
    /// <param name="contributors">
    /// Complete dependency-ordered contributor snapshot.
    /// </param>
    [ScriptingApiIgnore]
    public void SetContributors(IReadOnlyList<ProjectSettingsContributor> contributors)
    {
        ArgumentNullException.ThrowIfNull(contributors);
        ProjectSettingsContributor[] snapshot = contributors.ToArray();
        ProjectSettings.ValidateContributorOrder(snapshot);
        lock (m_sync)
        {
            _ = RequireCurrent();
            m_contributors = snapshot;
        }
    }

    /// <summary>
    /// Rebuilds effective settings after the active type catalog changes.
    /// </summary>
    /// <param name="allowUnresolvedContributions">
    /// Whether Plugin contributions awaiting type activation are skipped.
    /// </param>
    [ScriptingApiIgnore]
    public void RebuildCurrent(bool allowUnresolvedContributions = false)
    {
        lock (m_sync)
        {
            RequireCurrent().Rebuild(m_contributors, allowUnresolvedContributions);
            m_revision++;
        }
    }

    /// <summary>
    /// Persists one project-authored override through the native settings document.
    /// </summary>
    /// <param name="id">
    /// Stable setting protocol identity.
    /// </param>
    /// <param name="value">
    /// Current generation setting value.
    /// </param>
    /// <param name="contributors">
    /// Current dependency-ordered default contributions.
    /// </param>
    [ScriptingApiIgnore]
    public void SetProjectOverride(
        ProjectSettingId id,
        ISerializable value,
        IReadOnlyList<ProjectSettingsContributor> contributors)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(contributors);
        lock (m_sync)
        {
            RequireCurrent().SetProjectOverride(id, value, contributors);
            m_revision++;
        }
    }

    /// <summary>
    /// Applies a native batch of project-authored overrides and removals.
    /// </summary>
    /// <param name="values">
    /// Setting values keyed by stable protocol identity.
    /// </param>
    /// <param name="resets">
    /// Setting identities whose project overrides are removed.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the native document changed.
    /// </returns>
    [ScriptingApiIgnore]
    public bool ApplyProjectOverrides(
        IReadOnlyDictionary<ProjectSettingId, ISerializable> values,
        IReadOnlySet<ProjectSettingId>? resets = null)
    {
        ArgumentNullException.ThrowIfNull(values);
        lock (m_sync)
        {
            bool changed = RequireCurrent().ApplyProjectOverrides(values, resets, m_contributors);
            if (changed)
                m_revision++;
            return changed;
        }
    }

    /// <summary>
    /// Restores a previously captured native project settings document.
    /// </summary>
    /// <param name="document">
    /// Native project settings bytes.
    /// </param>
    [ScriptingApiIgnore]
    public void RestoreDocument(ReadOnlySpan<byte> document)
    {
        lock (m_sync)
        {
            RequireCurrent().RestoreDocument(document, m_contributors);
            m_revision++;
        }
    }

    /// <summary>
    /// Validates one native project settings document without changing active state.
    /// </summary>
    /// <param name="document">
    /// Native project settings bytes.
    /// </param>
    [ScriptingApiIgnore]
    public void ValidateDocument(ReadOnlySpan<byte> document)
        => _ = m_serialization.Deserialize<ProjectSettingsDocument>(document);

    /// <summary>
    /// Shuts down settings and releases generation-scoped values.
    /// </summary>
    [ScriptingApiIgnore]
    public void Dispose()
    {
        lock (m_sync)
        {
            m_current?.Dispose();
            m_current = null;
            m_contributors = [];
            m_revision++;
        }
        GC.SuppressFinalize(this);
    }

    private ProjectSettings RequireCurrent()
        => m_current ?? throw new InvalidOperationException("ProjectSettingsStore is not initialized.");
}
