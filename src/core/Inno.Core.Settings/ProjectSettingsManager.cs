using System;
using System.Collections.Generic;
using System.Linq;
using Inno.Core.Serialization;
using Inno.Core.Scripting;

namespace Inno.Core.Settings;

/// <summary>Provides the single project-wide access point for effective native settings.</summary>
public static class ProjectSettingsManager
{
    private static readonly object S_SYNC = new();
    private static IReadOnlyList<ProjectSettingsContributor> s_contributors = [];
    private static ProjectSettings? s_current;
    private static long s_revision;

    /// <summary>Gets whether project settings are initialized.</summary>
    public static bool isInitialized
    {
        get
        {
            lock (S_SYNC)
                return s_current is not null;
        }
    }

    /// <summary>
    /// Gets the monotonic revision of the active effective settings snapshot.
    /// Runtime extensions may compare this value without registering reload-unsafe static delegates.
    /// </summary>
    public static long revision
    {
        get
        {
            lock (S_SYNC)
                return s_revision;
        }
    }

    /// <summary>Gets an isolated effective setting snapshot from the active extension generation.</summary>
    /// <typeparam name="TSetting">Expected setting contract.</typeparam>
    /// <param name="id">Stable setting protocol identity.</param>
    /// <returns>An independently owned effective setting snapshot.</returns>
    public static TSetting Get<TSetting>(ProjectSettingId id)
        where TSetting : class, ISerializable
    {
        lock (S_SYNC)
            return RequireCurrent().Get<TSetting>(id);
    }

    /// <summary>Tries to get an isolated effective setting snapshot from the active extension generation.</summary>
    /// <typeparam name="TSetting">Expected setting contract.</typeparam>
    /// <param name="id">Stable setting protocol identity.</param>
    /// <param name="setting">Receives an independently owned effective snapshot when available.</param>
    /// <returns><see langword="true"/> when a compatible setting exists.</returns>
    public static bool TryGet<TSetting>(ProjectSettingId id, out TSetting? setting)
        where TSetting : class, ISerializable
    {
        lock (S_SYNC)
        {
            if (s_current is null)
            {
                setting = null;
                return false;
            }
            return s_current.TryGet(id, out setting);
        }
    }

    /// <summary>Gets whether the active project document explicitly overrides one setting protocol.</summary>
    /// <param name="id">Stable setting protocol identity.</param>
    /// <returns><see langword="true"/> when the project owns an override record.</returns>
    [ScriptingApiIgnore]
    public static bool HasProjectOverride(ProjectSettingId id)
    {
        lock (S_SYNC)
            return RequireCurrent().HasProjectOverride(id);
    }

    /// <summary>Captures one normalized Plugin setting contribution from the project-authored delta.</summary>
    /// <param name="id">Stable setting protocol identity.</param>
    /// <param name="contributorId">Stable identity of the Plugin being exported.</param>
    /// <param name="declaredDependencies">Direct dependency Plugin IDs declared by the exported Plugin.</param>
    /// <param name="declaredOverrides">Dependency Plugin IDs whose owned values may be replaced.</param>
    /// <param name="record">Receives the normalized semantic contribution payload.</param>
    /// <returns><see langword="true"/> when the project document contains an effective semantic delta.</returns>
    [ScriptingApiIgnore]
    public static bool TryCapture(
        ProjectSettingId id,
        string contributorId,
        IReadOnlySet<string> declaredDependencies,
        IReadOnlySet<string> declaredOverrides,
        out ProjectSettingRecord record)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contributorId);
        ArgumentNullException.ThrowIfNull(declaredDependencies);
        ArgumentNullException.ThrowIfNull(declaredOverrides);
        lock (S_SYNC)
        {
            return RequireCurrent().TryCapture(
                id,
                contributorId,
                declaredDependencies,
                declaredOverrides,
                s_contributors,
                out record);
        }
    }

    /// <summary>Creates an isolated editable copy of one effective setting.</summary>
    /// <param name="id">Stable setting protocol identity.</param>
    /// <param name="setting">Receives an isolated native setting object.</param>
    /// <returns><see langword="true"/> when the setting is defined.</returns>
    [ScriptingApiIgnore]
    public static bool TryClone(ProjectSettingId id, out ISerializable? setting)
    {
        lock (S_SYNC)
        {
            if (s_current is null)
            {
                setting = null;
                return false;
            }
            return s_current.TryClone(id, out setting);
        }
    }

    /// <summary>Creates an isolated setting value without the project-authored override.</summary>
    /// <param name="id">Stable setting protocol identity.</param>
    /// <param name="setting">Receives the composed host and Plugin default value.</param>
    /// <returns><see langword="true"/> when the setting is defined.</returns>
    [ScriptingApiIgnore]
    public static bool TryCloneComposedDefault(ProjectSettingId id, out ISerializable? setting)
    {
        lock (S_SYNC)
        {
            if (s_current is null)
            {
                setting = null;
                return false;
            }
            return s_current.TryCloneComposedDefault(id, s_contributors, out setting);
        }
    }

    /// <summary>Captures the current native project override document.</summary>
    /// <returns>A newly owned native document payload.</returns>
    [ScriptingApiIgnore]
    public static byte[] CaptureDocument()
    {
        lock (S_SYNC)
            return RequireCurrent().CaptureDocument();
    }

    /// <summary>Initializes the host-owned settings service.</summary>
    /// <param name="documentPath">Absolute ProjectSettings.inno path.</param>
    [ScriptingApiIgnore]
    public static void Initialize(string documentPath)
    {
        lock (S_SYNC)
        {
            if (s_current is not null)
                throw new InvalidOperationException("ProjectSettingsManager is already initialized.");
            s_current = new ProjectSettings(documentPath);
            s_revision++;
        }
    }

    /// <summary>Rebuilds settings for one dependency-ordered extension generation.</summary>
    /// <param name="contributors">Host and Plugin default contributions.</param>
    [ScriptingApiIgnore]
    public static void Rebuild(IReadOnlyList<ProjectSettingsContributor> contributors)
    {
        ArgumentNullException.ThrowIfNull(contributors);
        lock (S_SYNC)
        {
            RequireCurrent().Rebuild(contributors);
            s_contributors = contributors.ToArray();
            s_revision++;
        }
    }

    /// <summary>
    /// Publishes dependency-ordered default contributors for the next current-generation rebuild without
    /// constructing setting instances from types that may still be awaiting assembly activation.
    /// </summary>
    /// <param name="contributors">Complete dependency-ordered contributor snapshot.</param>
    [ScriptingApiIgnore]
    public static void SetContributors(IReadOnlyList<ProjectSettingsContributor> contributors)
    {
        ArgumentNullException.ThrowIfNull(contributors);
        ProjectSettingsContributor[] snapshot = contributors.ToArray();
        ProjectSettings.ValidateContributorOrder(snapshot);
        lock (S_SYNC)
        {
            _ = RequireCurrent();
            s_contributors = snapshot;
        }
    }

    /// <summary>Rebuilds effective settings after the active type catalog changes.</summary>
    /// <param name="allowUnresolvedContributions">Whether Plugin contributions awaiting type activation are skipped.</param>
    [ScriptingApiIgnore]
    public static void RebuildCurrent(bool allowUnresolvedContributions = false)
    {
        lock (S_SYNC)
        {
            RequireCurrent().Rebuild(s_contributors, allowUnresolvedContributions);
            s_revision++;
        }
    }

    /// <summary>Persists one project-authored override through the native settings document.</summary>
    /// <param name="id">Stable setting protocol identity.</param>
    /// <param name="value">Current generation setting value.</param>
    /// <param name="contributors">Current dependency-ordered default contributions.</param>
    [ScriptingApiIgnore]
    public static void SetProjectOverride(
        ProjectSettingId id,
        ISerializable value,
        IReadOnlyList<ProjectSettingsContributor> contributors)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(contributors);
        lock (S_SYNC)
        {
            RequireCurrent().SetProjectOverride(id, value, contributors);
            s_revision++;
        }
    }

    /// <summary>Applies a native batch of project-authored overrides and removals.</summary>
    /// <param name="values">Setting values keyed by stable protocol identity.</param>
    /// <param name="resets">Setting identities whose project overrides are removed.</param>
    /// <returns><see langword="true"/> when the native document changed.</returns>
    [ScriptingApiIgnore]
    public static bool ApplyProjectOverrides(
        IReadOnlyDictionary<ProjectSettingId, ISerializable> values,
        IReadOnlySet<ProjectSettingId>? resets = null)
    {
        ArgumentNullException.ThrowIfNull(values);
        lock (S_SYNC)
        {
            bool changed = RequireCurrent().ApplyProjectOverrides(values, resets, s_contributors);
            if (changed)
                s_revision++;
            return changed;
        }
    }

    /// <summary>Restores a previously captured native project settings document.</summary>
    /// <param name="document">Native project settings bytes.</param>
    [ScriptingApiIgnore]
    public static void RestoreDocument(ReadOnlySpan<byte> document)
    {
        lock (S_SYNC)
        {
            RequireCurrent().RestoreDocument(document, s_contributors);
            s_revision++;
        }
    }

    /// <summary>Validates one native project settings document without changing active state.</summary>
    /// <param name="document">Native project settings bytes.</param>
    [ScriptingApiIgnore]
    public static void ValidateDocument(ReadOnlySpan<byte> document)
        => _ = SerializationManager.Deserialize<ProjectSettingsDocument>(document);

    /// <summary>Shuts down settings and releases generation-scoped values.</summary>
    [ScriptingApiIgnore]
    public static void Shutdown()
    {
        lock (S_SYNC)
        {
            s_current?.Dispose();
            s_current = null;
            s_contributors = [];
            s_revision++;
        }
    }

    private static ProjectSettings RequireCurrent()
        => s_current ?? throw new InvalidOperationException("ProjectSettingsManager is not initialized.");
}
