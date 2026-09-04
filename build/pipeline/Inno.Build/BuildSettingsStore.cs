using System;
using System.IO;

using Inno.Core.Serialization;
using Inno.Core.Settings;

namespace Inno.Build;

/// <summary>
/// Persists project-owned export defaults independently from temporary export requests.
/// </summary>
public sealed class BuildSettingsStore
{
    private readonly BuildSettings m_defaultSettings;
    private readonly SettingsDocumentStore<BuildSettings> m_documents;
    private readonly object m_sync = new();

    /// <summary>
    /// Creates a store for one project's current build settings document.
    /// </summary>
    /// <param name="path">
    /// The project-owned <c>Settings.Build.inno</c> path.
    /// </param>
    /// <param name="serialization">
    /// The active engine serialization registry.
    /// </param>
    /// <param name="defaultSettings">
    /// Canonical project defaults used while no document exists.
    /// </param>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="path"/> is empty.
    /// </exception>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="serialization"/> or <paramref name="defaultSettings"/> is null.
    /// </exception>
    public BuildSettingsStore(
        string path,
        SerializationRegistry serialization,
        BuildSettings defaultSettings)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(serialization);
        ArgumentNullException.ThrowIfNull(defaultSettings);
        defaultSettings.ValidateDocument();
        m_documents = new SettingsDocumentStore<BuildSettings>(
            path,
            serialization,
            defaultSettings.Copy,
            static value => value.ValidateDocument());
        m_defaultSettings = defaultSettings.Copy();
    }

    /// <summary>
    /// Gets whether a project-owned build settings document exists.
    /// </summary>
    public bool exists
    {
        get
        {
            lock (m_sync)
                return m_documents.exists;
        }
    }

    /// <summary>
    /// Gets an isolated copy of the canonical project defaults.
    /// </summary>
    public BuildSettings defaultSettings
    {
        get
        {
            lock (m_sync)
                return m_defaultSettings.Copy();
        }
    }

    /// <summary>
    /// Loads the saved settings or returns an isolated copy of the canonical defaults.
    /// </summary>
    /// <returns>
    /// A newly owned settings object.
    /// </returns>
    /// <exception cref="InvalidDataException">
    /// Thrown when the saved document is malformed.
    /// </exception>
    public BuildSettings Load()
    {
        lock (m_sync)
            return LoadLocked();
    }

    /// <summary>
    /// Atomically replaces the project-owned build defaults.
    /// </summary>
    /// <param name="settings">
    /// The complete defaults to persist.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="settings"/> is null.
    /// </exception>
    /// <exception cref="InvalidDataException">
    /// Thrown when the settings document is malformed.
    /// </exception>
    /// <exception cref="IOException">
    /// Thrown when the candidate cannot be committed.
    /// </exception>
    public void Save(BuildSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.ValidateDocument();
        lock (m_sync)
            m_documents.Save(settings.Copy());
    }

    /// <summary>
    /// Captures the current effective build defaults as a native document.
    /// </summary>
    /// <returns>
    /// A newly owned current-format document payload.
    /// </returns>
    public byte[] CaptureDocument()
    {
        lock (m_sync)
            return m_documents.Capture(LoadLocked());
    }

    /// <summary>
    /// Validates and atomically restores a native build settings document.
    /// </summary>
    /// <param name="document">
    /// The complete document payload.
    /// </param>
    /// <exception cref="InvalidDataException">
    /// Thrown when the document is malformed.
    /// </exception>
    /// <exception cref="IOException">
    /// Thrown when the document cannot be committed.
    /// </exception>
    public void RestoreDocument(ReadOnlySpan<byte> document)
    {
        BuildSettings settings = Deserialize(document);
        lock (m_sync)
            m_documents.Save(settings);
    }

    /// <summary>
    /// Validates a native build settings document without changing the active file.
    /// </summary>
    /// <param name="document">
    /// The document payload to inspect.
    /// </param>
    /// <exception cref="InvalidDataException">
    /// Thrown when the document is malformed.
    /// </exception>
    public void ValidateDocument(ReadOnlySpan<byte> document)
        => _ = Deserialize(document);

    private BuildSettings LoadLocked()
    {
        return m_documents.exists
            ? m_documents.LoadRequired()
            : m_defaultSettings.Copy();
    }

    private BuildSettings Deserialize(ReadOnlySpan<byte> document)
    {
        return m_documents.Deserialize(document);
    }

}
