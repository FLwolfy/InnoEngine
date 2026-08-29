using System;
using System.Collections.Generic;
using System.Linq;

using Inno.Core.Serialization;
using Inno.Core.Settings;
using Inno.Editor.Settings;

namespace Inno.Editor.Panel.Settings;

internal enum SettingsScope
{
    Editor,
    Project
}

internal sealed class SettingsField
{
    private readonly EditorSetting? m_editor;
    private readonly ProjectSettingEditor? m_project;

    internal SettingsField(EditorSetting definition)
    {
        m_editor = definition;
        scope = SettingsScope.Editor;
        path = definition.path;
        pagePath = definition.pagePath;
        label = definition.label;
        order = definition.order;
        section = definition.section ?? string.Empty;
        description = definition.description;
    }

    internal SettingsField(ProjectSettingEditor definition)
    {
        m_project = definition;
        scope = SettingsScope.Project;
        path = definition.path;
        pagePath = definition.pagePath;
        label = definition.label;
        order = definition.order;
        section = definition.section ?? string.Empty;
        description = definition.description;
    }

    internal SettingsScope scope { get; }
    internal string path { get; }
    internal string pagePath { get; }
    internal string label { get; }
    internal int order { get; }
    internal string section { get; }
    internal string description { get; }
    internal EditorSetting? editor => m_editor;
    internal ProjectSettingEditor? project => m_project;
}

internal sealed class SettingsEditSession
{
    private readonly EditorSettings m_editorSettings;
    private readonly ProjectSettingsEditor m_projectSettings;
    private readonly HashSet<string> m_editorModified = new(StringComparer.Ordinal);
    private readonly HashSet<string> m_editorResetIntent = new(StringComparer.Ordinal);
    private readonly HashSet<string> m_editorResets = new(StringComparer.Ordinal);
    private readonly Dictionary<string, EditorSettingObject> m_editorValues = new(StringComparer.Ordinal);
    private readonly HashSet<ProjectSettingId> m_projectModified = [];
    private readonly HashSet<ProjectSettingId> m_projectOverrides = [];
    private readonly HashSet<ProjectSettingId> m_projectResetIntent = [];
    private readonly HashSet<ProjectSettingId> m_projectResets = [];
    private readonly Dictionary<ProjectSettingId, ISerializable> m_projectValues = [];

    internal SettingsEditSession(
        EditorSettings editorSettings,
        ProjectSettingsEditor projectSettings)
    {
        m_editorSettings = editorSettings ?? throw new ArgumentNullException(nameof(editorSettings));
        m_projectSettings = projectSettings ?? throw new ArgumentNullException(nameof(projectSettings));
        editorCatalogRevision = editorSettings.catalogRevision;
        projectCatalogRevision = projectSettings.catalogRevision;

        var fields = new List<SettingsField>();
        foreach (EditorSetting definition in editorSettings.definitions)
        {
            if (!definition.hasValue)
                continue;
            fields.Add(new SettingsField(definition));
            m_editorValues.Add(definition.path, editorSettings.Get(definition.path));
        }
        foreach (ProjectSettingEditor definition in projectSettings.definitions)
        {
            fields.Add(new SettingsField(definition));
            m_projectValues.Add(definition.settingId, projectSettings.Get(definition));
            if (ProjectSettingsManager.HasProjectOverride(definition.settingId))
                m_projectOverrides.Add(definition.settingId);
        }

        string? duplicatePath = fields
            .GroupBy(static field => field.path, StringComparer.Ordinal)
            .FirstOrDefault(static group => group.Count() > 1)?.Key;
        if (duplicatePath is not null)
            throw new InvalidOperationException($"Settings field path '{duplicatePath}' is registered more than once.");

        definitions = fields
            .OrderBy(static field => field.path, StringComparer.Ordinal)
            .ThenBy(static field => field.order)
            .ToArray();
        pages = BuildPages(editorSettings.definitions, definitions);
    }

    internal long editorCatalogRevision { get; }
    internal long projectCatalogRevision { get; }
    internal IReadOnlyList<SettingsField> definitions { get; }
    internal IReadOnlyList<SettingsPage> pages { get; }
    internal bool isEditorDirty => m_editorModified.Count > 0 || m_editorResets.Count > 0;
    internal bool isProjectDirty => m_projectModified.Count > 0 || m_projectResets.Count > 0;
    internal bool isDirty => isEditorDirty || isProjectDirty;

    internal bool Draw(SettingsField field)
    {
        if (field.editor is EditorSetting editor)
            return editor.Draw(m_editorValues[editor.path]);
        ProjectSettingEditor project = field.project
            ?? throw new InvalidOperationException($"Settings field '{field.path}' has no owner.");
        return project.Draw(m_projectValues[project.settingId]);
    }

    internal bool CanReset(SettingsField field)
    {
        if (field.editor is EditorSetting editor)
            return !editor.IsDefault(m_editorValues[editor.path]);
        ProjectSettingEditor project = field.project
            ?? throw new InvalidOperationException($"Settings field '{field.path}' has no owner.");
        bool differsFromDefault = !project.ValuesEqual(
            m_projectValues[project.settingId],
            m_projectSettings.GetComposedDefault(project));
        return differsFromDefault ||
               (m_projectOverrides.Contains(project.settingId) &&
                !m_projectResets.Contains(project.settingId));
    }

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

    internal void Reset(SettingsField field)
    {
        if (!CanReset(field))
            return;
        if (field.editor is EditorSetting editor)
        {
            m_editorValues[editor.path] = editor.defaultValue;
            _ = m_editorResetIntent.Add(editor.path);
            _ = m_editorResets.Add(editor.path);
            _ = m_editorModified.Remove(editor.path);
            return;
        }
        ProjectSettingEditor project = field.project
            ?? throw new InvalidOperationException($"Settings field '{field.path}' has no owner.");
        m_projectValues[project.settingId] = m_projectSettings.GetComposedDefault(project);
        _ = m_projectResetIntent.Add(project.settingId);
        _ = m_projectResets.Add(project.settingId);
        _ = m_projectModified.Remove(project.settingId);
    }

    internal void Reset(SettingsPage page)
    {
        for (int i = 0; i < page.settings.Count; i++)
            Reset(page.settings[i]);
        for (int i = 0; i < page.children.Count; i++)
            Reset(page.children[i]);
    }

    internal void UpdateDirty(SettingsField field, bool differsFromDrawBaseline)
    {
        if (field.editor is EditorSetting editor)
        {
            UpdateDirty(
                editor.path,
                differsFromDrawBaseline,
                m_editorModified,
                m_editorResets,
                m_editorResetIntent);
            return;
        }
        ProjectSettingId id = field.project?.settingId
            ?? throw new InvalidOperationException($"Settings field '{field.path}' has no owner.");
        UpdateDirty(id, differsFromDrawBaseline, m_projectModified, m_projectResets, m_projectResetIntent);
    }

    internal bool ApplyEditor()
    {
        var values = m_editorModified.ToDictionary(
            path => path,
            path => m_editorValues[path],
            StringComparer.Ordinal);
        bool changed = m_editorSettings.Apply(values, m_editorResets);
        m_editorModified.Clear();
        m_editorResetIntent.Clear();
        m_editorResets.Clear();
        foreach (SettingsField field in definitions.Where(static field => field.scope == SettingsScope.Editor))
            m_editorValues[field.path] = m_editorSettings.Get(field.path);
        return changed;
    }

    internal bool ApplyProject()
    {
        var values = m_projectModified.ToDictionary(id => id, id => m_projectValues[id]);
        bool changed = m_projectSettings.Apply(values, m_projectResets);
        m_projectModified.Clear();
        m_projectOverrides.Clear();
        m_projectResetIntent.Clear();
        m_projectResets.Clear();
        foreach (SettingsField field in definitions.Where(static field => field.scope == SettingsScope.Project))
        {
            ProjectSettingEditor definition = field.project!;
            m_projectValues[definition.settingId] = m_projectSettings.Get(definition);
            if (ProjectSettingsManager.HasProjectOverride(definition.settingId))
                m_projectOverrides.Add(definition.settingId);
        }
        return changed;
    }

    private static void UpdateDirty<T>(
        T id,
        bool differs,
        ISet<T> modified,
        ISet<T> resets,
        ISet<T> resetIntent)
        where T : notnull
    {
        if (differs)
        {
            _ = modified.Add(id);
            _ = resets.Remove(id);
            return;
        }
        _ = modified.Remove(id);
        if (resetIntent.Contains(id))
            _ = resets.Add(id);
    }

    private static SettingsPage[] BuildPages(
        IReadOnlyList<EditorSetting> editorDefinitions,
        IReadOnlyList<SettingsField> fields)
    {
        var root = new MutablePage(string.Empty, string.Empty);
        var byPath = new Dictionary<string, MutablePage>(StringComparer.Ordinal)
        {
            [string.Empty] = root
        };

        foreach (EditorSetting definition in editorDefinitions.Where(static definition => !definition.hasValue))
        {
            MutablePage page = EnsurePage(definition.path, byPath);
            page.description = definition.description;
        }
        foreach (SettingsField field in fields)
            EnsurePage(field.pagePath, byPath).settings.Add(field);

        string? collision = fields.Select(static field => field.path).FirstOrDefault(byPath.ContainsKey);
        if (collision is not null)
            throw new InvalidOperationException($"Settings path '{collision}' cannot be a field and a page.");

        return root.children
            .OrderBy(static page => page.label, StringComparer.OrdinalIgnoreCase)
            .Select(static page => page.Build())
            .ToArray();
    }

    private static MutablePage EnsurePage(string path, IDictionary<string, MutablePage> byPath)
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
        internal readonly List<SettingsField> settings = [];
        internal string? description;
        internal string label { get; } = label;

        internal SettingsPage Build()
        {
            SettingsField[] orderedSettings = settings
                .OrderBy(static setting => setting.section, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static setting => setting.order)
                .ThenBy(static setting => setting.label, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static setting => setting.path, StringComparer.Ordinal)
                .ToArray();
            SettingsPage[] orderedChildren = children
                .OrderBy(static page => page.label, StringComparer.OrdinalIgnoreCase)
                .Select(static page => page.Build())
                .ToArray();
            string fallback = path switch
            {
                "Editor" => "Configure Editor-only appearance, tools, and authoring preferences.",
                "Project" => "Configure runtime-facing project behavior and Plugin-provided protocols.",
                _ => $"Browse {label} settings and related options."
            };
            return new SettingsPage(
                path,
                label,
                string.IsNullOrWhiteSpace(description) ? fallback : description,
                orderedSettings,
                orderedChildren);
        }
    }
}

internal sealed record SettingsPage(
    string path,
    string label,
    string description,
    IReadOnlyList<SettingsField> settings,
    IReadOnlyList<SettingsPage> children)
{
    internal bool hasSettings => settings.Count > 0;
}
