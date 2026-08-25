using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Inno.Editor.Core;

/// <summary>
/// Owns the unified, human-readable per-project <c>editor.ini</c> document used by Dear ImGui and
/// stateful editor modules and panels.
/// </summary>
internal sealed class EditorLayoutSettings
{
    private const string C_SECTION_PREFIX = "[InnoEditor][";

    private readonly object m_sync = new();
    private readonly string m_path;
    private readonly Dictionary<string, Dictionary<string, string>> m_sections;
    private string m_imguiLayout;
    private string m_lastSavedDocument;

    /// <summary>
    /// Creates the settings document for one project and reads any existing layout and editor sections.
    /// </summary>
    /// <param name="projectDirectory">
    /// The project root that owns <c>editor.ini</c>.
    /// </param>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="projectDirectory"/> is empty.
    /// </exception>
    internal EditorLayoutSettings(string projectDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectDirectory);
        m_path = Path.Combine(Path.GetFullPath(projectDirectory), "editor.ini");
        string document = File.Exists(m_path) ? File.ReadAllText(m_path) : string.Empty;
        Parse(document, out m_imguiLayout, out m_sections);
        m_lastSavedDocument = Compose(m_imguiLayout, m_sections);
    }

    /// <summary>
    /// Gets the absolute path of the unified project editor settings document.
    /// </summary>
    internal string path => m_path;

    /// <summary>
    /// Gets the current Dear ImGui layout without any Inno Editor settings sections.
    /// </summary>
    internal string imguiLayout
    {
        get
        {
            lock (m_sync)
                return m_imguiLayout;
        }
    }

    /// <summary>
    /// Gets the names of all readable Inno Editor settings sections that begin with an optional prefix.
    /// </summary>
    /// <param name="prefix">
    /// The ordinal section-name prefix to match, or an empty string to return every section.
    /// </param>
    /// <returns>
    /// A stable, ordinally sorted snapshot of matching section names.
    /// </returns>
    internal IReadOnlyList<string> GetSectionNames(string prefix = "")
    {
        ArgumentNullException.ThrowIfNull(prefix);
        lock (m_sync)
        {
            return m_sections.Keys
                .Where(name => name.StartsWith(prefix, StringComparison.Ordinal))
                .OrderBy(static name => name, StringComparer.Ordinal)
                .ToArray();
        }
    }

    /// <summary>
    /// Tries to read an independent snapshot of one Inno Editor settings section.
    /// </summary>
    /// <param name="sectionName">
    /// The stable section name without the surrounding <c>[InnoEditor]</c> header syntax.
    /// </param>
    /// <param name="values">
    /// The copied key/value collection when the section exists.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the requested section exists.
    /// </returns>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="sectionName"/> is empty or cannot be represented by the INI format.
    /// </exception>
    internal bool TryGetSection(
        string sectionName,
        out IReadOnlyDictionary<string, string> values)
    {
        ValidateSectionName(sectionName);
        lock (m_sync)
        {
            if (!m_sections.TryGetValue(sectionName, out Dictionary<string, string>? section))
            {
                values = new Dictionary<string, string>(StringComparer.Ordinal);
                return false;
            }

            values = new Dictionary<string, string>(section, StringComparer.Ordinal);
            return true;
        }
    }

    /// <summary>
    /// Replaces the Dear ImGui layout while retaining every readable Inno Editor settings section.
    /// </summary>
    /// <param name="layout">
    /// The complete layout text returned by Dear ImGui.
    /// </param>
    internal void SetImGuiLayout(string? layout)
    {
        Parse(layout ?? string.Empty, out string parsedLayout, out _);
        lock (m_sync)
            m_imguiLayout = parsedLayout;
    }

    /// <summary>
    /// Adds or atomically replaces one human-readable Inno Editor settings section in memory.
    /// </summary>
    /// <param name="sectionName">
    /// The stable section name without the surrounding <c>[InnoEditor]</c> header syntax.
    /// </param>
    /// <param name="values">
    /// The complete set of scalar or JSON-formatted values owned by the section.
    /// </param>
    /// <exception cref="ArgumentException">
    /// Thrown when a section name, key, or value cannot be represented by the INI format.
    /// </exception>
    internal void SetSection(
        string sectionName,
        IEnumerable<KeyValuePair<string, string>> values)
    {
        ValidateSectionName(sectionName);
        ArgumentNullException.ThrowIfNull(values);
        var replacement = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach ((string key, string value) in values)
        {
            ValidateKey(key);
            ValidateValue(value);
            replacement[key] = value;
        }

        lock (m_sync)
            m_sections[sectionName] = replacement;
    }

    /// <summary>
    /// Removes one Inno Editor settings section from the in-memory document.
    /// </summary>
    /// <param name="sectionName">
    /// The stable section name without the surrounding <c>[InnoEditor]</c> header syntax.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when an existing section was removed.
    /// </returns>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="sectionName"/> is empty or cannot be represented by the INI format.
    /// </exception>
    internal bool RemoveSection(string sectionName)
    {
        ValidateSectionName(sectionName);
        lock (m_sync)
            return m_sections.Remove(sectionName);
    }

    /// <summary>
    /// Atomically writes <c>editor.ini</c> when either the layout or an editor section changed.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when a changed document was written.
    /// </returns>
    internal bool SaveIfChanged()
    {
        lock (m_sync)
        {
            string document = Compose(m_imguiLayout, m_sections);
            if (string.Equals(document, m_lastSavedDocument, StringComparison.Ordinal))
                return false;
            WriteDocument(document);
            return true;
        }
    }

    /// <summary>
    /// Atomically rewrites the complete <c>editor.ini</c> document even when its content is unchanged.
    /// </summary>
    internal void Save()
    {
        lock (m_sync)
            WriteDocument(Compose(m_imguiLayout, m_sections));
    }

    private void WriteDocument(string document)
    {
        string? directory = Path.GetDirectoryName(m_path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
        string temporaryPath = m_path + ".tmp";
        using (var stream = new FileStream(
                   temporaryPath,
                   FileMode.Create,
                   FileAccess.Write,
                   FileShare.None,
                   4096,
                   FileOptions.WriteThrough))
        {
            byte[] bytes = Encoding.UTF8.GetBytes(document);
            stream.Write(bytes);
            stream.Flush(flushToDisk: true);
        }
        File.Move(temporaryPath, m_path, overwrite: true);
        m_lastSavedDocument = document;
    }

    private static void Parse(
        string document,
        out string layout,
        out Dictionary<string, Dictionary<string, string>> sections)
    {
        sections = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        var layoutBuilder = new StringBuilder();
        Dictionary<string, string>? currentEditorSection = null;
        using var reader = new StringReader(document);
        for (string? line = reader.ReadLine(); line is not null; line = reader.ReadLine())
        {
            if (TryParseEditorSectionHeader(line, out string sectionName))
            {
                currentEditorSection = new Dictionary<string, string>(StringComparer.Ordinal);
                sections[sectionName] = currentEditorSection;
                continue;
            }

            if (line.StartsWith("[", StringComparison.Ordinal))
                currentEditorSection = null;

            if (currentEditorSection is not null)
            {
                int separator = line.IndexOf('=');
                if (separator <= 0)
                    continue;
                string key = line[..separator].Trim();
                string value = line[(separator + 1)..].Trim();
                if (IsValidKey(key) && IsValidValue(value))
                    currentEditorSection[key] = value;
                continue;
            }

            layoutBuilder.AppendLine(line);
        }

        layout = NormalizeLayout(layoutBuilder.ToString());
    }

    private static string Compose(
        string layout,
        IReadOnlyDictionary<string, Dictionary<string, string>> sections)
    {
        var builder = new StringBuilder();
        string normalizedLayout = NormalizeLayout(layout);
        if (normalizedLayout.Length > 0)
            builder.Append(normalizedLayout).AppendLine().AppendLine();

        string[] names = sections.Keys.OrderBy(static name => name, StringComparer.Ordinal).ToArray();
        for (int sectionIndex = 0; sectionIndex < names.Length; sectionIndex++)
        {
            string name = names[sectionIndex];
            builder.Append(C_SECTION_PREFIX).Append(name).AppendLine("]");
            Dictionary<string, string> section = sections[name];
            foreach (KeyValuePair<string, string> pair in OrderValues(section))
                builder.Append(pair.Key).Append('=').AppendLine(pair.Value);
            if (sectionIndex + 1 < names.Length)
                builder.AppendLine();
        }
        return builder.ToString();
    }

    private static IEnumerable<KeyValuePair<string, string>> OrderValues(
        IReadOnlyDictionary<string, string> values)
        => values.OrderBy(static pair => pair.Key, StringComparer.Ordinal);

    private static bool TryParseEditorSectionHeader(string line, out string sectionName)
    {
        if (line.StartsWith(C_SECTION_PREFIX, StringComparison.Ordinal) &&
            line.EndsWith(']'))
        {
            sectionName = line[C_SECTION_PREFIX.Length..^1];
            return IsValidSectionName(sectionName);
        }
        sectionName = string.Empty;
        return false;
    }

    private static void ValidateSectionName(string sectionName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sectionName);
        if (!IsValidSectionName(sectionName))
        {
            throw new ArgumentException(
                "Editor settings section names cannot contain brackets or line breaks.",
                nameof(sectionName));
        }
    }

    private static void ValidateKey(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        if (!IsValidKey(key))
        {
            throw new ArgumentException(
                "Editor settings keys cannot contain an equals sign or line break.",
                nameof(key));
        }
    }

    private static void ValidateValue(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (!IsValidValue(value))
        {
            throw new ArgumentException(
                "Editor settings values cannot contain line breaks.",
                nameof(value));
        }
    }

    private static bool IsValidSectionName(string value)
        => !string.IsNullOrWhiteSpace(value) &&
           value.IndexOfAny(['[', ']', '\r', '\n']) < 0;

    private static bool IsValidKey(string value)
        => !string.IsNullOrWhiteSpace(value) &&
           value.IndexOfAny(['=', '\r', '\n']) < 0;

    private static bool IsValidValue(string value)
        => value.IndexOfAny(['\r', '\n']) < 0;

    private static string NormalizeLayout(string? layout)
        => string.IsNullOrWhiteSpace(layout)
            ? string.Empty
            : layout.TrimEnd('\r', '\n');
}
