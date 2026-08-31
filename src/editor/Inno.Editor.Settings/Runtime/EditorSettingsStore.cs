using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Inno.Editor.Settings;

internal sealed class EditorSettingsStore
{
    internal const string C_FILE_NAME = "EditorSettings.json";

    private readonly string m_path;
    private Dictionary<string, EditorSettingObject> m_values;

    internal EditorSettingsStore(string projectDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectDirectory);
        m_path = Path.Combine(Path.GetFullPath(projectDirectory), C_FILE_NAME);
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
    {
        var result = new Dictionary<string, EditorSettingObject>(m_values.Count, StringComparer.Ordinal);
        foreach ((string path, EditorSettingObject value) in m_values)
            result.Add(path, value.Copy());
        return result;
    }

    internal byte[] GetDocument()
        => Serialize(m_values);

    internal void Replace(IReadOnlyDictionary<string, EditorSettingObject> values)
    {
        byte[] document = Serialize(values);
        Write(document);
        m_values = Copy(values);
    }

    internal void ReplaceDocument(ReadOnlySpan<byte> document)
    {
        Dictionary<string, EditorSettingObject> values = ReadValues(document);
        byte[] normalized = Serialize(values);
        Write(normalized);
        m_values = values;
    }

    internal static void ValidateDocument(ReadOnlySpan<byte> document)
        => _ = ReadValues(document);

    private static Dictionary<string, EditorSettingObject> Copy(
        IReadOnlyDictionary<string, EditorSettingObject> values)
    {
        var result = new Dictionary<string, EditorSettingObject>(values.Count, StringComparer.Ordinal);
        foreach ((string path, EditorSettingObject value) in values)
            result.Add(path, value.Copy());
        return result;
    }

    private static byte[] Serialize(IReadOnlyDictionary<string, EditorSettingObject> values)
    {
        var root = new JsonObject();
        string[] paths = new string[values.Count];
        int pathIndex = 0;
        foreach (string path in values.Keys)
            paths[pathIndex++] = path;
        Array.Sort(paths, StringComparer.Ordinal);
        for (int i = 0; i < paths.Length; i++)
            root[paths[i]] = JsonNode.Parse(values[paths[i]].Serialize());
        string json = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        return Encoding.UTF8.GetBytes(json);
    }

    private static Dictionary<string, EditorSettingObject> ReadValues(string path)
        => File.Exists(path) ? ReadValues(File.ReadAllBytes(path)) : [];

    private static Dictionary<string, EditorSettingObject> ReadValues(ReadOnlySpan<byte> document)
    {
        var result = new Dictionary<string, EditorSettingObject>(StringComparer.Ordinal);
        try
        {
            JsonNode? parsed = JsonNode.Parse(document);
            if (parsed is not JsonObject root)
                throw new InvalidDataException("The editor Settings document must contain a JSON object.");
            foreach ((string path, JsonNode? value) in root)
            {
                string normalized = EditorSettingsPathUtility.Normalize(path);
                if (!string.Equals(path, normalized, StringComparison.Ordinal))
                    throw new InvalidDataException($"Settings path '{path}' is not normalized.");
                if (value is not JsonObject objectValue)
                    throw new InvalidDataException($"Settings path '{path}' must contain a JSON object.");
                result.Add(path, EditorSettingObject.Deserialize(objectValue.ToJsonString()));
            }
            return result;
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("The editor Settings document is not valid JSON.", exception);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException("The editor Settings document contains an invalid path.", exception);
        }
    }

    private void Write(byte[] document)
    {
        string temporaryPath = m_path + ".tmp";
        using (var stream = new FileStream(
                   temporaryPath,
                   FileMode.Create,
                   FileAccess.Write,
                   FileShare.None,
                   4096,
                   FileOptions.WriteThrough))
        {
            stream.Write(document);
            stream.Flush(flushToDisk: true);
        }
        File.Move(temporaryPath, m_path, overwrite: true);
    }
}
