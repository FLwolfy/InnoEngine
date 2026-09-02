using System;
using System.Collections.Generic;
using System.IO;

using Inno.Core.Serialization;

namespace Inno.Editor.Settings;

internal sealed class EditorSettingsStore
{
    internal const string C_FILE_NAME = "EditorSettings.inno";

    private readonly string m_path;
    private readonly SerializationRegistry m_serialization;
    private Dictionary<string, EditorSettingObject> m_values;

    internal EditorSettingsStore(string projectDirectory, SerializationRegistry serialization)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectDirectory);
        ArgumentNullException.ThrowIfNull(serialization);
        m_path = Path.Combine(Path.GetFullPath(projectDirectory), C_FILE_NAME);
        m_serialization = serialization;
        m_values = ReadValues(m_path);
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

    private Dictionary<string, EditorSettingObject> ReadValues(string path)
        => File.Exists(path) ? ReadValues(File.ReadAllBytes(path)) : [];

    private Dictionary<string, EditorSettingObject> ReadValues(ReadOnlySpan<byte> document)
    {
        try
        {
            EditorSettingsDocument parsed = m_serialization.Deserialize<EditorSettingsDocument>(document);
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
        => m_serialization.Serialize(new EditorSettingsDocument { values = Copy(values) });

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

    private void Write(byte[] document)
    {
        string candidate = m_path + ".staging-" + Guid.NewGuid().ToString("N");
        try
        {
            using (var stream = new FileStream(
                       candidate,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       4096,
                       FileOptions.WriteThrough))
            {
                stream.Write(document);
                stream.Flush(flushToDisk: true);
            }
            File.Move(candidate, m_path, overwrite: true);
        }
        finally
        {
            if (File.Exists(candidate))
                File.Delete(candidate);
        }
    }
}
