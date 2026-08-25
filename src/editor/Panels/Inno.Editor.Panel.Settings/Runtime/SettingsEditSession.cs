using System;
using System.Collections.Generic;
using System.Linq;

using Inno.Editor.Settings;

namespace Inno.Editor.Panel.Settings;

internal sealed class SettingsEditSession
{
    private readonly EditorSettings m_settings;
    private readonly HashSet<string> m_modified = new(StringComparer.Ordinal);
    private readonly HashSet<string> m_resetIntent = new(StringComparer.Ordinal);
    private readonly HashSet<string> m_resets = new(StringComparer.Ordinal);
    private readonly Dictionary<string, EditorSettingObject> m_values;

    internal SettingsEditSession(EditorSettings settings)
    {
        m_settings = settings ?? throw new ArgumentNullException(nameof(settings));
        catalogRevision = settings.catalogRevision;
        definitions = settings.definitions;
        pages = BuildPages(definitions);
        m_values = new Dictionary<string, EditorSettingObject>(StringComparer.Ordinal);
        for (int i = 0; i < definitions.Count; i++)
        {
            if (definitions[i].hasValue)
                m_values.Add(definitions[i].path, settings.Get(definitions[i].path));
        }
    }

    internal long catalogRevision { get; }

    internal IReadOnlyList<EditorSetting> definitions { get; }

    internal IReadOnlyList<SettingsPage> pages { get; }

    internal bool isDirty => m_modified.Count > 0 || m_resets.Count > 0;

    internal EditorSettingObject Get(EditorSetting setting)
        => m_values.TryGetValue(setting.path, out EditorSettingObject? value)
            ? value
            : throw new ArgumentException(
                $"Settings field '{setting.path}' is not part of this edit session.",
                nameof(setting));

    internal bool CanReset(EditorSetting setting)
        => !setting.IsDefault(Get(setting));

    internal bool CanReset(SettingsPage page)
    {
        for (int i = 0; i < page.settings.Count; i++)
        {
            if (CanReset(page.settings[i]))
                return true;
        }
        for (int i = 0; i < page.children.Count; i++)
        {
            if (CanReset(page.children[i]))
                return true;
        }
        return false;
    }

    internal void Reset(EditorSetting setting)
    {
        if (!m_values.ContainsKey(setting.path))
        {
            throw new ArgumentException(
                $"Settings field '{setting.path}' is not part of this edit session.",
                nameof(setting));
        }
        if (!CanReset(setting))
            return;
        m_values[setting.path] = setting.defaultValue
            ?? throw new InvalidOperationException(
                $"Settings field '{setting.path}' did not provide a default value.");
        _ = m_resetIntent.Add(setting.path);
        _ = m_resets.Add(setting.path);
        _ = m_modified.Remove(setting.path);
    }

    internal void Reset(SettingsPage page)
    {
        for (int i = 0; i < page.settings.Count; i++)
            Reset(page.settings[i]);
        for (int i = 0; i < page.children.Count; i++)
            Reset(page.children[i]);
    }

    internal void UpdateDirty(EditorSetting setting, bool differsFromDrawBaseline)
    {
        if (differsFromDrawBaseline)
        {
            _ = m_modified.Add(setting.path);
            _ = m_resets.Remove(setting.path);
            return;
        }

        _ = m_modified.Remove(setting.path);
        if (m_resetIntent.Contains(setting.path))
            _ = m_resets.Add(setting.path);
    }

    internal bool Apply()
    {
        bool changed = m_settings.Apply(m_values, m_resets);
        m_modified.Clear();
        m_resetIntent.Clear();
        m_resets.Clear();
        for (int i = 0; i < definitions.Count; i++)
        {
            if (definitions[i].hasValue)
                m_values[definitions[i].path] = m_settings.Get(definitions[i].path);
        }
        return changed;
    }

    private static SettingsPage[] BuildPages(IReadOnlyList<EditorSetting> definitions)
    {
        var root = new MutablePage(string.Empty, string.Empty);
        var byPath = new Dictionary<string, MutablePage>(StringComparer.Ordinal)
        {
            [string.Empty] = root
        };

        for (int i = 0; i < definitions.Count; i++)
        {
            EditorSetting definition = definitions[i];
            if (!definition.hasValue)
            {
                MutablePage page = EnsurePage(definition.path, byPath);
                page.description = definition.description;
                continue;
            }
            EnsurePage(definition.pagePath, byPath).settings.Add(definition);
        }

        string? collision = definitions
            .Where(static definition => definition.hasValue)
            .Select(static definition => definition.path)
            .FirstOrDefault(byPath.ContainsKey);
        if (collision is not null)
            throw new InvalidOperationException($"Settings path '{collision}' cannot be a field and a page.");

        return root.children
            .OrderBy(static page => page.label, StringComparer.OrdinalIgnoreCase)
            .Select(static page => page.Build())
            .ToArray();
    }

    private static MutablePage EnsurePage(
        string path,
        IDictionary<string, MutablePage> byPath)
    {
        if (byPath.TryGetValue(path, out MutablePage? existing))
            return existing;
        string[] segments = path.Split('/');
        string currentPath = string.Empty;
        MutablePage parent = byPath[string.Empty];
        for (int i = 0; i < segments.Length; i++)
        {
            currentPath = currentPath.Length == 0 ? segments[i] : $"{currentPath}/{segments[i]}";
            if (byPath.TryGetValue(currentPath, out MutablePage? current))
            {
                parent = current;
                continue;
            }
            current = new MutablePage(currentPath, segments[i]);
            byPath.Add(currentPath, current);
            parent.children.Add(current);
            parent = current;
        }
        return parent;
    }

    private sealed class MutablePage(string path, string label)
    {
        internal readonly List<MutablePage> children = [];
        internal readonly List<EditorSetting> settings = [];
        internal string? description;
        internal string label { get; } = label;

        internal SettingsPage Build()
        {
            EditorSetting[] orderedSettings = settings
                .OrderBy(static setting => setting.section ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static setting => setting.order)
                .ThenBy(static setting => setting.label, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static setting => setting.path, StringComparer.Ordinal)
                .ToArray();
            SettingsPage[] orderedChildren = children
                .OrderBy(static page => page.label, StringComparer.OrdinalIgnoreCase)
                .Select(static page => page.Build())
                .ToArray();
            return new SettingsPage(
                path,
                label,
                string.IsNullOrWhiteSpace(description)
                    ? $"Browse {label} settings and related options."
                    : description,
                orderedSettings,
                orderedChildren);
        }
    }
}

internal sealed record SettingsPage(
    string path,
    string label,
    string description,
    IReadOnlyList<EditorSetting> settings,
    IReadOnlyList<SettingsPage> children)
{
    internal bool hasSettings => settings.Count > 0;
}
