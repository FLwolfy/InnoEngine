using System;
using System.IO;

using Inno.Core.IO;
using Inno.Core.Serialization;

namespace Inno.Core.Settings;

/// <summary>
/// Provides validated current-format serialization and atomic persistence for one settings document type.
/// </summary>
/// <typeparam name="TDocument">
/// The complete serializable document type.
/// </typeparam>
public sealed class SettingsDocumentStore<TDocument>
    where TDocument : class, ISerializable
{
    private readonly Func<TDocument> m_createDefault;
    private readonly SerializationRegistry m_serialization;
    private readonly Action<TDocument> m_validate;

    /// <summary>
    /// Creates a type-safe settings document store.
    /// </summary>
    /// <param name="path">
    /// The absolute or project-relative document path.
    /// </param>
    /// <param name="serialization">
    /// The active serialization registry.
    /// </param>
    /// <param name="createDefault">
    /// Creates a newly owned value when the document does not exist.
    /// </param>
    /// <param name="validate">
    /// Validates one deserialized or candidate document.
    /// </param>
    public SettingsDocumentStore(
        string path,
        SerializationRegistry serialization,
        Func<TDocument> createDefault,
        Action<TDocument>? validate = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(serialization);
        ArgumentNullException.ThrowIfNull(createDefault);
        this.path = Path.GetFullPath(path);
        m_serialization = serialization;
        m_createDefault = createDefault;
        m_validate = validate ?? (static _ => { });
    }

    /// <summary>
    /// Gets the normalized document path.
    /// </summary>
    public string path { get; }

    /// <summary>
    /// Gets whether the document currently exists.
    /// </summary>
    public bool exists => File.Exists(path);

    /// <summary>
    /// Loads the saved value, or creates a validated default when absent.
    /// </summary>
    /// <returns>
    /// A newly owned current-format document.
    /// </returns>
    public TDocument Load()
    {
        if (!exists)
        {
            TDocument value = m_createDefault();
            m_validate(value);
            return Clone(value);
        }
        return Deserialize(File.ReadAllBytes(path));
    }

    /// <summary>
    /// Loads a required saved value.
    /// </summary>
    /// <returns>
    /// A newly owned current-format document.
    /// </returns>
    public TDocument LoadRequired()
    {
        if (!exists)
            throw new FileNotFoundException("The settings document does not exist.", path);
        return Deserialize(File.ReadAllBytes(path));
    }

    /// <summary>
    /// Validates and atomically replaces the complete document.
    /// </summary>
    /// <param name="document">
    /// The complete candidate document.
    /// </param>
    public void Save(TDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        m_validate(document);
        AtomicFile.WriteAllBytes(path, m_serialization.Serialize(document));
    }

    /// <summary>
    /// Serializes a validated document without changing the file.
    /// </summary>
    /// <param name="document">
    /// The document to capture.
    /// </param>
    /// <returns>
    /// A newly owned native payload.
    /// </returns>
    public byte[] Capture(TDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        m_validate(document);
        return m_serialization.Serialize(document);
    }

    /// <summary>
    /// Deserializes and validates a native payload without changing the file.
    /// </summary>
    /// <param name="data">
    /// The native payload.
    /// </param>
    /// <returns>
    /// A newly owned current-format document.
    /// </returns>
    public TDocument Deserialize(ReadOnlySpan<byte> data)
    {
        try
        {
            TDocument document = m_serialization.Deserialize<TDocument>(data);
            m_validate(document);
            return document;
        }
        catch (Exception exception) when (exception is ArgumentException
            or InvalidDataException
            or InvalidOperationException
            or NotSupportedException)
        {
            throw new InvalidDataException(
                $"Settings document '{path}' is not a valid current-format {typeof(TDocument).Name}.",
                exception);
        }
    }

    /// <summary>
    /// Validates and atomically restores a native document payload.
    /// </summary>
    /// <param name="data">
    /// The native payload.
    /// </param>
    public void Restore(ReadOnlySpan<byte> data)
        => Save(Deserialize(data));

    private TDocument Clone(TDocument document)
        => Deserialize(m_serialization.Serialize(document));
}
