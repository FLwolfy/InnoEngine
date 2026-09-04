using System;
using System.Collections.Generic;
using System.IO;

using Inno.Core.Serialization;
using Inno.Core.Settings;
using Inno.Extensibility.Types;
using Inno.Editor.Core;
using Inno.Editor.Interactions;

namespace Inno.Editor.Settings;

/// <summary>
/// Owns the reloadable Editor presentations and history-aware project override workflow for runtime settings.
/// </summary>
[EditorModule("project-settings-editor", order: int.MinValue + 1)]
public sealed class ProjectSettingsEditor : EditorModule
{
    private readonly ProjectSettingEditorCatalog m_catalog;
    private readonly IEditorHistory m_history;
    private readonly ProjectSettingsStore m_settings;

    /// <summary>
    /// Creates the project settings Editor service over the shared Editor history.
    /// </summary>
    /// <param name="interactions">
    /// The Editor interaction service that owns Undo and Redo.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="interactions"/> is <see langword="null"/>.
    /// </exception>
    /// <param name="types">
    /// The host-owned type catalog used to discover setting presentations.
    /// </param>
    /// <param name="settings">
    /// The project settings store edited by this module.
    /// </param>
    /// <param name="serialization">
    /// The serialization registry used to compare staged setting values.
    /// </param>
    internal ProjectSettingsEditor(
        EditorInteractions interactions,
        TypeCatalog types,
        ProjectSettingsStore settings,
        SerializationRegistry serialization)
    {
        ArgumentNullException.ThrowIfNull(interactions);
        ArgumentNullException.ThrowIfNull(types);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(serialization);
        m_history = interactions.history;
        m_settings = settings;
        m_catalog = new ProjectSettingEditorCatalog(types, serialization);
    }

    /// <summary>
    /// Gets the active project setting Editor catalog revision.
    /// </summary>
    public long catalogRevision => m_catalog.snapshot.revision;

    /// <summary>
    /// Gets all active strongly typed project setting presentations.
    /// </summary>
    public IReadOnlyList<ProjectSettingEditor> definitions => m_catalog.snapshot.definitions;

    /// <summary>
    /// Creates an isolated editable snapshot for one registered presentation.
    /// </summary>
    /// <param name="definition">
    /// The active presentation whose runtime protocol is requested.
    /// </param>
    /// <returns>
    /// An isolated current-generation value.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="definition"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the runtime setting definition is unavailable.
    /// </exception>
    public ISerializable Get(ProjectSettingEditor definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (!m_settings.TryClone(definition.settingId, out ISerializable? setting) || setting is null)
        {
            throw new InvalidOperationException(
                $"Project setting '{definition.settingId}' has no active runtime definition.");
        }
        definition.ValidateValue(setting);
        return setting;
    }

    /// <summary>
    /// Creates the composed host and Plugin default without the project override.
    /// </summary>
    /// <param name="definition">
    /// The active presentation whose default is requested.
    /// </param>
    /// <returns>
    /// An isolated composed default value.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="definition"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the runtime setting definition is unavailable.
    /// </exception>
    public ISerializable GetComposedDefault(ProjectSettingEditor definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (!m_settings.TryCloneComposedDefault(
                definition.settingId,
                out ISerializable? setting) || setting is null)
        {
            throw new InvalidOperationException(
                $"Project setting '{definition.settingId}' has no active runtime definition.");
        }
        definition.ValidateValue(setting);
        return setting;
    }

    /// <summary>
    /// Atomically applies project-authored overrides and records one stable history entry.
    /// </summary>
    /// <param name="values">
    /// Staged exact-type values keyed by stable setting identity.
    /// </param>
    /// <param name="resets">
    /// Setting identities whose project overrides should be removed.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the project override document changed.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="values"/> is <see langword="null"/>.
    /// </exception>
    public bool Apply(
        IReadOnlyDictionary<ProjectSettingId, ISerializable> values,
        IReadOnlySet<ProjectSettingId>? resets = null)
    {
        ArgumentNullException.ThrowIfNull(values);
        byte[] before = m_settings.CaptureDocument();
        if (!m_settings.ApplyProjectOverrides(values, resets))
            return false;
        byte[] after = m_settings.CaptureDocument();
        try
        {
            using EditorHistoryChange change = ProjectSettingsHistory.CreateChange(before, after);
            m_history.RecordApplied("Apply Project Settings", change);
        }
        catch
        {
            m_settings.RestoreDocument(before);
            throw;
        }
        return true;
    }

    /// <summary>
    /// Releases resources retained by this feature after it has stopped.
    /// </summary>
    protected override void OnDispose()
        => m_catalog.Dispose();

    internal byte[] CaptureDocument()
        => m_settings.CaptureDocument();

    internal void ValidateDocument(ReadOnlySpan<byte> document)
        => m_settings.ValidateDocument(document);

    internal void RestoreFromHistory(ReadOnlySpan<byte> document)
        => m_settings.RestoreDocument(document);

}
