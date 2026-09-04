using System;
using System.Collections.Generic;
using System.Linq;

using Inno.Build;
using Inno.Core.Serialization;
using Inno.Core.Settings;
using Inno.Editor.Interactions;
using Inno.Editor.Settings;

namespace Inno.Editor.Panel.Settings;

internal enum SettingsScope
{
    Editor,
    Project,
    Build
}

internal sealed class SettingsField
{
    private readonly BuildSettingsField? m_build;
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

    internal SettingsField(BuildSettingsField definition)
    {
        m_build = definition;
        scope = SettingsScope.Build;
        path = definition.path;
        pagePath = definition.pagePath;
        label = definition.label;
        order = 0;
        section = definition.section;
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
    internal BuildSettingsField? build => m_build;
}

internal sealed class SettingsEditSession
{
    private readonly EditorSettings m_editorSettings;
    private readonly ProjectSettingsEditor m_projectSettings;
    private readonly HashSet<string> m_editorInitiallyNonDefault = new(StringComparer.Ordinal);
    private readonly HashSet<string> m_editorModified = new(StringComparer.Ordinal);
    private readonly HashSet<string> m_editorResetIntent = new(StringComparer.Ordinal);
    private readonly HashSet<string> m_editorResets = new(StringComparer.Ordinal);
    private readonly Dictionary<string, EditorSettingObject> m_editorValues = new(StringComparer.Ordinal);
    private readonly HashSet<ProjectSettingId> m_projectModified = [];
    private readonly HashSet<ProjectSettingId> m_projectInitiallyNonDefault = [];
    private readonly HashSet<ProjectSettingId> m_projectResetIntent = [];
    private readonly HashSet<ProjectSettingId> m_projectResets = [];
    private readonly Dictionary<ProjectSettingId, ISerializable> m_projectOriginalValues = [];
    private readonly Dictionary<ProjectSettingId, ISerializable> m_projectValues = [];
    private readonly BuildSettingsStore m_buildSettings;
    private readonly IEditorHistory m_history;
    private BuildSettings m_buildDefault;
    private BuildSettings m_buildOriginal;
    private BuildSettings m_buildValue;

    internal SettingsEditSession(
        EditorSettings editorSettings,
        ProjectSettingsEditor projectSettings,
        BuildSettingsStore buildSettings,
        IEditorHistory history)
    {
        m_editorSettings = editorSettings ?? throw new ArgumentNullException(nameof(editorSettings));
        m_projectSettings = projectSettings ?? throw new ArgumentNullException(nameof(projectSettings));
        m_buildSettings = buildSettings ?? throw new ArgumentNullException(nameof(buildSettings));
        m_history = history ?? throw new ArgumentNullException(nameof(history));
        editorCatalogRevision = editorSettings.catalogRevision;
        projectCatalogRevision = projectSettings.catalogRevision;
        m_buildDefault = buildSettings.defaultSettings;
        m_buildOriginal = buildSettings.Load();
        m_buildValue = m_buildOriginal.Copy();

        var fields = new List<SettingsField>();
        foreach (EditorSetting definition in editorSettings.definitions)
        {
            if (!definition.hasValue)
                continue;
            fields.Add(new SettingsField(definition));
            EditorSettingObject value = editorSettings.Get(definition.path);
            m_editorValues.Add(definition.path, value);
            if (!definition.IsDefault(value))
                m_editorInitiallyNonDefault.Add(definition.path);
        }
        foreach (ProjectSettingEditor definition in projectSettings.definitions)
        {
            fields.Add(new SettingsField(definition));
            if (m_projectValues.ContainsKey(definition.settingId))
                continue;
            ISerializable value = projectSettings.Get(definition);
            m_projectValues.Add(definition.settingId, value);
            m_projectOriginalValues.Add(definition.settingId, projectSettings.Get(definition));
            if (!definition.ValuesEqual(value, projectSettings.GetComposedDefault(definition)))
                m_projectInitiallyNonDefault.Add(definition.settingId);
        }
        foreach (BuildSettingsField definition in BuildSettingsPresentation.fields)
            fields.Add(new SettingsField(definition));

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
    internal bool isBuildDirty => !BuildSettingsField.ValuesEqual(m_buildValue, m_buildOriginal);
    internal bool isDirty => isEditorDirty || isProjectDirty || isBuildDirty;

    internal bool Draw(SettingsField field)
    {
        if (field.editor is EditorSetting editor)
            return editor.Draw(m_editorValues[editor.path]);
        if (field.project is ProjectSettingEditor project)
            return project.Draw(m_projectValues[project.settingId]);
        if (field.build is BuildSettingsField build)
            return build.Draw(m_buildValue);
        throw new InvalidOperationException($"Settings field '{field.path}' has no owner.");
    }

    internal bool CanReset(SettingsField field)
    {
        if (field.editor is EditorSetting editor)
            return !editor.IsDefault(m_editorValues[editor.path]);
        if (field.build is BuildSettingsField build)
            return !build.IsDefault(m_buildValue, m_buildDefault);
        ProjectSettingEditor project = field.project
            ?? throw new InvalidOperationException($"Settings field '{field.path}' has no owner.");
        bool differsFromDefault = !project.ValuesEqual(
            m_projectValues[project.settingId],
            m_projectSettings.GetComposedDefault(project));
        return differsFromDefault;
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
            SetResetState(
                editor.path,
                m_editorInitiallyNonDefault.Contains(editor.path),
                m_editorResetIntent,
                m_editorResets);
            _ = m_editorModified.Remove(editor.path);
            return;
        }
        if (field.build is BuildSettingsField build)
        {
            build.Reset(m_buildValue, m_buildDefault);
            return;
        }
        ProjectSettingEditor project = field.project
            ?? throw new InvalidOperationException($"Settings field '{field.path}' has no owner.");
        m_projectValues[project.settingId] = m_projectSettings.GetComposedDefault(project);
        SetResetState(
            project.settingId,
            m_projectInitiallyNonDefault.Contains(project.settingId),
            m_projectResetIntent,
            m_projectResets);
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
        if (field.build is not null)
            return;
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
        ProjectSettingEditor project = field.project;
        ISerializable value = m_projectValues[id];
        if (m_projectResetIntent.Contains(id))
        {
            ISerializable composedDefault = m_projectSettings.GetComposedDefault(project);
            if (project.ValuesEqual(value, composedDefault))
            {
                _ = m_projectModified.Remove(id);
                return;
            }
            _ = m_projectResetIntent.Remove(id);
            _ = m_projectResets.Remove(id);
        }

        bool differsFromOriginal = !project.ValuesEqual(value, m_projectOriginalValues[id]);
        if (differsFromOriginal)
            _ = m_projectModified.Add(id);
        else
            _ = m_projectModified.Remove(id);
    }

    internal bool Apply()
    {
        bool changed = false;
        if (isEditorDirty)
            changed |= ApplyEditor();
        if (isProjectDirty)
            changed |= ApplyProject();
        if (isBuildDirty)
            changed |= ApplyBuild();
        return changed;
    }

    private bool ApplyEditor()
    {
        var values = m_editorModified.ToDictionary(
            path => path,
            path => m_editorValues[path],
            StringComparer.Ordinal);
        bool changed = m_editorSettings.Apply(values, m_editorResets);
        m_editorModified.Clear();
        m_editorInitiallyNonDefault.Clear();
        m_editorResetIntent.Clear();
        m_editorResets.Clear();
        foreach (SettingsField field in definitions.Where(static field => field.scope == SettingsScope.Editor))
        {
            EditorSetting definition = field.editor!;
            EditorSettingObject value = m_editorSettings.Get(field.path);
            m_editorValues[field.path] = value;
            if (!definition.IsDefault(value))
                m_editorInitiallyNonDefault.Add(field.path);
        }
        return changed;
    }

    private bool ApplyProject()
    {
        var values = m_projectModified.ToDictionary(id => id, id => m_projectValues[id]);
        bool changed = m_projectSettings.Apply(values, m_projectResets);
        m_projectModified.Clear();
        m_projectInitiallyNonDefault.Clear();
        m_projectResetIntent.Clear();
        m_projectResets.Clear();
        foreach (SettingsField field in definitions
                     .Where(static field => field.scope == SettingsScope.Project)
                     .GroupBy(static field => field.project!.settingId)
                     .Select(static group => group.First()))
        {
            ProjectSettingEditor definition = field.project!;
            ISerializable value = m_projectSettings.Get(definition);
            m_projectValues[definition.settingId] = value;
            m_projectOriginalValues[definition.settingId] = m_projectSettings.Get(definition);
            if (!definition.ValuesEqual(value, m_projectSettings.GetComposedDefault(definition)))
                m_projectInitiallyNonDefault.Add(definition.settingId);
        }
        return changed;
    }

    private bool ApplyBuild()
    {
        byte[] before = m_buildSettings.CaptureDocument();
        m_buildSettings.Save(m_buildValue);
        byte[] after = m_buildSettings.CaptureDocument();
        try
        {
            using EditorHistoryChange change = BuildSettingsHistory.CreateChange(before, after);
            m_history.RecordApplied("Apply Build Settings", change);
        }
        catch
        {
            m_buildSettings.RestoreDocument(before);
            throw;
        }

        m_buildDefault = m_buildSettings.defaultSettings;
        m_buildOriginal = m_buildSettings.Load();
        m_buildValue = m_buildOriginal.Copy();
        return true;
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

    private static void SetResetState<T>(
        T id,
        bool changesEffectiveValue,
        ISet<T> resetIntent,
        ISet<T> resets)
        where T : notnull
    {
        if (changesEffectiveValue)
        {
            _ = resetIntent.Add(id);
            _ = resets.Add(id);
            return;
        }
        _ = resetIntent.Remove(id);
        _ = resets.Remove(id);
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
                "Build" => "Configure project-owned defaults copied into temporary export requests.",
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
