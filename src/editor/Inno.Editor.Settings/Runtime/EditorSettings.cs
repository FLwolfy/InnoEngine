using System;
using System.Collections.Generic;
using System.IO;

using Inno.Core.Logging;
using Inno.Core.Serialization;
using Inno.Extensibility.Types;
using Inno.Editor.Core;
using Inno.Editor.Interactions;

namespace Inno.Editor.Settings;

/// <summary>
/// Owns the discovered Settings catalog and the project-root Settings document.
/// </summary>
[EditorModule("editor-settings", order: int.MinValue)]
public sealed class EditorSettings : EditorModule
{
    private readonly EditorSettingsCatalog m_catalog;
    private readonly IEditorHistory m_history;
    private readonly Logger m_log;
    private readonly object m_sync = new();
    private readonly EditorSettingsStore m_store;

    /// <summary>
    /// Creates the Settings service for one project and its shared editor history.
    /// </summary>
    /// <param name="context">
    /// The context that supplies the project root.
    /// </param>
    /// <param name="interactions">
    /// The runtime that owns the shared Undo and Redo history.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="context"/> or <paramref name="interactions"/> is
    /// <see langword="null"/>.
    /// </exception>
    /// <exception cref="InvalidDataException">
    /// Thrown when the project-root Settings document is malformed.
    /// </exception>
    /// <param name="types">
    /// The host-owned type catalog used to discover setting presentations.
    /// </param>
    /// <param name="logs">
    /// The host-owned logging router used for observer-failure diagnostics.
    /// </param>
    /// <param name="serialization">
    /// The host-owned current-generation serialization registry used for the project document.
    /// </param>
    internal EditorSettings(
        EditorContext context,
        EditorInteractions interactions,
        TypeCatalog types,
        LogRouter logs,
        SerializationRegistry serialization)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(interactions);
        ArgumentNullException.ThrowIfNull(types);
        ArgumentNullException.ThrowIfNull(logs);
        ArgumentNullException.ThrowIfNull(serialization);
        m_catalog = new EditorSettingsCatalog(types);
        m_store = new EditorSettingsStore(context.projectDirectory, serialization);
        m_history = interactions.history;
        m_log = logs.CreateLogger<EditorSettings>();
    }

    /// <summary>
    /// Occurs after a complete Settings Apply, Undo, or Redo changes the effective document.
    /// The committed <see cref="EditorSettings"/> service is the event's only argument.
    /// </summary>
    public event Action<EditorSettings>? changed;

    /// <summary>
    /// Gets the current discovered type-catalog revision.
    /// </summary>
    public long catalogRevision => m_catalog.snapshot.revision;

    /// <summary>
    /// Gets all discovered path definitions in deterministic path order.
    /// </summary>
    public IReadOnlyList<EditorSetting> definitions => m_catalog.snapshot.definitions;

    /// <summary>
    /// Reads an isolated effective object from one complete Settings path.
    /// </summary>
    /// <param name="path">
    /// The slash-delimited Settings field path.
    /// </param>
    /// <returns>
    /// The stored object, or an isolated copy of the field's default object.
    /// </returns>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="path"/> is invalid, missing, or describes a page.
    /// </exception>
    public EditorSettingObject Get(string path)
    {
        EditorSetting definition = ResolveField(path);
        lock (m_sync)
        {
            return m_store.TryGet(definition.path, out EditorSettingObject? value) && value is not null
                ? value
                : definition.CreateDefault();
        }
    }

    /// <summary>
    /// Atomically applies staged field objects as one shared Undo and Redo history entry.
    /// </summary>
    /// <param name="values">
    /// The complete or partial path-addressed objects to apply from the active catalog.
    /// </param>
    /// <param name="resets">
    /// Optional paths whose persisted overrides are removed during the same Apply.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the persisted document changed and one history entry was recorded.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="values"/> is <see langword="null"/> or contains a null object.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Thrown when a supplied path is invalid, absent, or describes a page.
    /// </exception>
    /// <exception cref="IOException">
    /// Thrown when the project-root document or its history payload cannot be written.
    /// </exception>
    /// <exception cref="UnauthorizedAccessException">
    /// Thrown when the project-root document is inaccessible.
    /// </exception>
    public bool Apply(
        IReadOnlyDictionary<string, EditorSettingObject> values,
        IReadOnlySet<string>? resets = null)
    {
        ArgumentNullException.ThrowIfNull(values);
        var normalizedValues = new Dictionary<string, EditorSettingObject>(
            values.Count,
            StringComparer.Ordinal);
        foreach ((string path, EditorSettingObject value) in values)
        {
            ArgumentNullException.ThrowIfNull(value);
            EditorSetting definition = ResolveField(path);
            normalizedValues.Add(definition.path, value.Copy());
        }

        HashSet<string>? normalizedResets = null;
        if (resets is not null)
        {
            normalizedResets = new HashSet<string>(StringComparer.Ordinal);
            foreach (string path in resets)
                _ = normalizedResets.Add(ResolveField(path).path);
        }

        byte[] before;
        byte[] after;
        lock (m_sync)
        {
            Dictionary<string, EditorSettingObject> replacement = m_store.GetSnapshot();
            bool storageChanged = false;
            if (normalizedResets is not null)
            {
                foreach (string path in normalizedResets)
                    storageChanged |= replacement.Remove(path);
            }
            foreach ((string path, EditorSettingObject candidate) in normalizedValues)
            {
                if (normalizedResets?.Contains(path) == true)
                    continue;
                if (replacement.TryGetValue(path, out EditorSettingObject? current) &&
                    current.ValueEquals(candidate))
                {
                    continue;
                }
                if (!replacement.ContainsKey(path) &&
                    ResolveField(path).CreateDefault().ValueEquals(candidate))
                {
                    continue;
                }
                replacement[path] = candidate.Copy();
                storageChanged = true;
            }
            if (!storageChanged)
                return false;

            before = m_store.GetDocument();
            m_store.Replace(replacement);
            after = m_store.GetDocument();
        }

        try
        {
            using EditorHistoryChange change = EditorSettingsHistory.CreateChange(before, after);
            m_history.RecordApplied("Apply Settings", change);
        }
        catch
        {
            lock (m_sync)
                m_store.ReplaceDocument(before);
            throw;
        }

        NotifyChanged();
        return true;
    }

    /// <summary>
    /// Releases resources retained by this feature after it has stopped.
    /// </summary>
    protected override void OnDispose()
        => m_catalog.Dispose();

    internal void RestoreFromHistory(ReadOnlySpan<byte> document)
    {
        lock (m_sync)
            m_store.ReplaceDocument(document);
    }

    internal byte[] CaptureDocument()
    {
        lock (m_sync)
            return m_store.GetDocument();
    }

    internal void ValidateDocument(ReadOnlySpan<byte> document)
    {
        lock (m_sync)
            m_store.ValidateDocument(document);
    }

    internal void NotifyChanged()
    {
        Action<EditorSettings>? handlers = changed;
        if (handlers is null)
            return;
        foreach (Delegate subscription in handlers.GetInvocationList())
        {
            try
            {
                ((Action<EditorSettings>)subscription)(this);
            }
            catch (Exception exception)
            {
                m_log.Write(
                    LogLevel.Error,
                    "Editor Settings changed subscriber failed: {0}",
                    [exception]);
            }
        }
    }

    private EditorSetting ResolveField(string path)
    {
        string normalized = EditorSettingsPathUtility.Normalize(path);
        if (!m_catalog.snapshot.byPath.TryGetValue(normalized, out EditorSetting? setting))
            throw new ArgumentException($"No editor setting is registered at '{normalized}'.", nameof(path));
        if (!setting.hasValue)
            throw new ArgumentException($"Editor setting '{normalized}' describes a page.", nameof(path));
        return setting;
    }
}
