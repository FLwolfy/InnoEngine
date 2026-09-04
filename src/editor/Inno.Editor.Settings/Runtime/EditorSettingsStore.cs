using System;
using System.Collections.Generic;
using System.IO;

using Inno.Core.Serialization;
using Inno.Core.Settings;

namespace Inno.Editor.Settings;

internal sealed class EditorSettingsStore
{
    internal const string C_FILE_NAME = SettingsFileNames.editor;

    private readonly SettingsDocumentStore<EditorSettingsDocument> m_documents;
    private Dictionary<string, EditorSettingObject> m_values;

    internal EditorSettingsStore(string projectDirectory, SerializationRegistry serialization)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectDirectory);
        ArgumentNullException.ThrowIfNull(serialization);
        string path = Path.Combine(Path.GetFullPath(projectDirectory), C_FILE_NAME);
        m_documents = new SettingsDocumentStore<EditorSettingsDocument>(
            path,
            serialization,
            static () => new EditorSettingsDocument(),
            ValidateDocumentValue);
        m_values = ValidateAndCopy(m_documents.Load().values);
    }

    internal bool TryGet(string path, out EditorSettingObject? value)
    {
        if (m_values.TryGetValue(path, out EditorSettingObject? stored))
        {
            value = stored.Copy();
            return true;
        }
        value = null;
        return false;
    }

    internal bool Contains(string path)
        => m_values.ContainsKey(path);

    internal Dictionary<string, EditorSettingObject> GetSnapshot()
        => Copy(m_values);

    internal byte[] GetDocument()
        => Serialize(m_values);

    internal void Replace(IReadOnlyDictionary<string, EditorSettingObject> values)
    {
        Dictionary<string, EditorSettingObject> candidate = ValidateAndCopy(values);
        byte[] document = Serialize(candidate);
        Write(document);
        m_values = candidate;
    }

    internal void ReplaceDocument(ReadOnlySpan<byte> document)
    {
        Dictionary<string, EditorSettingObject> values = ReadValues(document);
        byte[] normalized = Serialize(values);
        Write(normalized);
        m_values = values;
    }

    internal void ValidateDocument(ReadOnlySpan<byte> document)
        => _ = ReadValues(document);

    private Dictionary<string, EditorSettingObject> ReadValues(ReadOnlySpan<byte> document)
    {
        try
        {
            EditorSettingsDocument parsed = m_documents.Deserialize(document);
            return ValidateAndCopy(parsed.values);
        }
        catch (Exception exception) when (exception is ArgumentException
            or InvalidDataException
            or InvalidOperationException
            or NotSupportedException)
        {
            throw new InvalidDataException(
                "The editor Settings document is not a valid current-format document.",
                exception);
        }
    }

    private byte[] Serialize(IReadOnlyDictionary<string, EditorSettingObject> values)
        => m_documents.Capture(new EditorSettingsDocument { values = Copy(values) });

    private static Dictionary<string, EditorSettingObject> ValidateAndCopy(
        IReadOnlyDictionary<string, EditorSettingObject> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var result = new Dictionary<string, EditorSettingObject>(values.Count, StringComparer.Ordinal);
        foreach ((string path, EditorSettingObject value) in values)
        {
            string normalized;
            try
            {
                normalized = EditorSettingsPathUtility.Normalize(path);
            }
            catch (ArgumentException exception)
            {
                throw new InvalidDataException("The editor Settings document contains an invalid path.", exception);
            }
            if (!string.Equals(path, normalized, StringComparison.Ordinal))
                throw new InvalidDataException($"Settings path '{path}' is not normalized.");
            if (value is null)
                throw new InvalidDataException($"Settings path '{path}' has a null object.");
            value.Validate(path);
            result.Add(path, value.Copy());
        }
        return result;
    }

    private static Dictionary<string, EditorSettingObject> Copy(
        IReadOnlyDictionary<string, EditorSettingObject> values)
    {
        var result = new Dictionary<string, EditorSettingObject>(values.Count, StringComparer.Ordinal);
        foreach ((string path, EditorSettingObject value) in values)
            result.Add(path, value.Copy());
        return result;
    }

    private void Write(ReadOnlySpan<byte> document)
        => m_documents.Restore(document);

    private static void ValidateDocumentValue(EditorSettingsDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        _ = ValidateAndCopy(document.values);
    }
}
