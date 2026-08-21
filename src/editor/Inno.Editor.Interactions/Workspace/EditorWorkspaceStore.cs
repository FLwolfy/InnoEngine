using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json.Nodes;

using Inno.Core.Logging;
using Inno.Editor.Core;

namespace Inno.Editor.Interactions;

internal sealed class EditorWorkspaceStore
{
    private const int C_SCHEMA_VERSION = 2;
    private const double C_SAVE_INTERVAL_SECONDS = 2.0;
    private const string C_META_SECTION = "Project";
    private const string C_MODULE_SECTION_PREFIX = "Module.";
    private const string C_PANEL_SECTION_PREFIX = "Panel.";
    private const string C_STATE_SECTION_PREFIX = "State.";
    private const string C_PANELS_SECTION = "Panels";
    private const string C_LEGACY_WORKSPACE_SECTION = "Workspace";
    private const string C_LEGACY_MODULE_SECTION_PREFIX = "Workspace.Module.";

    private readonly EditorProjectSettings m_settings;
    private readonly ConditionalWeakTable<object, RestoredProvider> m_restoredProviders = new();
    private double m_nextSaveTime;

    internal EditorWorkspaceStore(EditorContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        m_settings = context.settings;
        string legacyPath = Path.Combine(
            context.projectDirectory,
            "Library",
            "Editor",
            "Workspace.json");
        bool migrated = TryMigrateEmbeddedPayload();
        if (!migrated && File.Exists(legacyPath))
            migrated = TryMigrateLegacyFile(legacyPath);
        EnsureMetadata();
        try
        {
            if (m_settings.SaveIfChanged() && migrated)
                TryDeleteLegacyWorkspace(legacyPath);
        }
        catch (Exception exception)
        {
            Log.Error("Editor workspace state could not be saved to '{0}': {1}",
                m_settings.path,
                exception);
        }
    }

    internal bool TryGetPanelOpen(string panelId, out bool isOpen)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(panelId);
        if (m_settings.TryGetSection(C_PANELS_SECTION, out IReadOnlyDictionary<string, string> values) &&
            values.TryGetValue(panelId, out string? stored) &&
            bool.TryParse(stored, out isOpen))
        {
            return true;
        }
        isOpen = false;
        return false;
    }

    internal void Capture(
        IReadOnlyList<EditorExtensionCatalog.WorkspaceRegistration> providers,
        IReadOnlyList<EditorExtensionCatalog.PanelRegistration> panels)
    {
        for (int i = 0; i < providers.Count; i++)
        {
            IEditorWorkspaceState provider = providers[i].provider;
            if (!m_restoredProviders.TryGetValue(provider, out RestoredProvider? state) ||
                !state.isRestored)
            {
                continue;
            }
            string sectionName = GetProviderSectionName(provider);
            try
            {
                var writer = new EditorWorkspaceStateWriter();
                provider.CaptureWorkspaceState(writer);
                m_settings.SetSection(
                    sectionName,
                    ExportProviderValues(provider.workspaceStateVersion, writer.Export()));
                RemoveLegacyProviderSections(provider.workspaceStateId, sectionName);
            }
            catch (Exception exception)
            {
                Log.Error(
                    "Editor workspace provider '{0}' failed to capture state: {1}",
                    provider.workspaceStateId,
                    exception);
            }
        }

        var panelValues = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int i = 0; i < panels.Count; i++)
            panelValues[panels[i].attribute.id] = panels[i].panel.isOpen ? "true" : "false";
        m_settings.SetSection(C_PANELS_SECTION, panelValues);
        EnsureMetadata();
    }

    internal void Restore(IReadOnlyList<EditorExtensionCatalog.WorkspaceRegistration> providers)
    {
        for (int i = 0; i < providers.Count; i++)
        {
            IEditorWorkspaceState provider = providers[i].provider;
            RestoredProvider state = m_restoredProviders.GetValue(
                provider,
                static _ => new RestoredProvider());
            if (state.isRestored || state.isRestoring)
                continue;
            state.isRestoring = true;
            try
            {
                EditorWorkspaceStateReader reader = CreateReader(provider);
                provider.RestoreWorkspaceState(reader);
                state.isRestored = true;
            }
            catch (Exception exception)
            {
                _ = m_restoredProviders.Remove(provider);
                Log.Error(
                    "Editor workspace provider '{0}' failed to restore state: {1}",
                    provider.workspaceStateId,
                    exception);
            }
            finally
            {
                state.isRestoring = false;
            }
        }
        SaveIfChanged();
    }

    internal void Update(
        double elapsedSeconds,
        IReadOnlyList<EditorExtensionCatalog.WorkspaceRegistration> providers,
        IReadOnlyList<EditorExtensionCatalog.PanelRegistration> panels)
    {
        if (elapsedSeconds < m_nextSaveTime)
            return;
        m_nextSaveTime = elapsedSeconds + C_SAVE_INTERVAL_SECONDS;
        Capture(providers, panels);
        SaveIfChanged();
    }

    internal void Save(
        IReadOnlyList<EditorExtensionCatalog.WorkspaceRegistration> providers,
        IReadOnlyList<EditorExtensionCatalog.PanelRegistration> panels)
    {
        Capture(providers, panels);
        SaveIfChanged();
    }

    private EditorWorkspaceStateReader CreateReader(IEditorWorkspaceState provider)
    {
        string sectionName = GetProviderSectionName(provider);
        bool found = m_settings.TryGetSection(sectionName, out IReadOnlyDictionary<string, string> values);
        string storedSection = sectionName;
        if (!found)
        {
            storedSection = C_STATE_SECTION_PREFIX + provider.workspaceStateId;
            found = m_settings.TryGetSection(
                storedSection,
                out values);
        }
        if (!found)
        {
            storedSection = C_LEGACY_MODULE_SECTION_PREFIX + provider.workspaceStateId;
            found = m_settings.TryGetSection(
                storedSection,
                out values);
        }
        if (!found)
        {
            return new EditorWorkspaceStateReader(0, null);
        }

        if (!string.Equals(storedSection, sectionName, StringComparison.Ordinal))
        {
            m_settings.SetSection(sectionName, values);
            _ = m_settings.RemoveSection(storedSection);
        }

        int version = values.TryGetValue("Version", out string? storedVersion) &&
                      int.TryParse(storedVersion, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
            ? parsed
            : 0;
        var root = new JsonObject();
        foreach ((string key, string value) in values)
        {
            if (string.Equals(key, "Version", StringComparison.Ordinal))
                continue;
            root[key] = JsonNode.Parse(value);
        }
        return new EditorWorkspaceStateReader(version, root.ToJsonString());
    }

    private void SaveIfChanged()
    {
        try
        {
            _ = m_settings.SaveIfChanged();
        }
        catch (Exception exception)
        {
            Log.Error("Editor workspace state could not be saved to '{0}': {1}", m_settings.path, exception);
        }
    }

    private bool TryMigrateEmbeddedPayload()
    {
        if (!m_settings.TryGetSection(
                C_LEGACY_WORKSPACE_SECTION,
                out IReadOnlyDictionary<string, string> values) ||
            !values.TryGetValue("Payload", out string? encoded) ||
            string.IsNullOrWhiteSpace(encoded))
        {
            return false;
        }

        try
        {
            string payload = Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
            bool migrated = TryMigratePayload(payload, m_settings.path);
            if (migrated)
                _ = m_settings.RemoveSection(C_LEGACY_WORKSPACE_SECTION);
            return migrated;
        }
        catch (Exception exception) when (exception is FormatException or ArgumentException)
        {
            Log.Warn(
                "Legacy editor workspace payload in '{0}' could not be decoded: {1}",
                m_settings.path,
                exception.Message);
            return false;
        }
    }

    private bool TryMigrateLegacyFile(string path)
    {
        try
        {
            return TryMigratePayload(File.ReadAllText(path), path);
        }
        catch (Exception exception)
        {
            Log.Warn("Legacy editor workspace state '{0}' could not be read: {1}", path, exception.Message);
            return false;
        }
    }

    private bool TryMigratePayload(string payload, string sourcePath)
    {
        try
        {
            if (JsonNode.Parse(payload) is not JsonObject root)
                return false;
            if (root["states"] is JsonObject states)
            {
                foreach ((string stateId, JsonNode? node) in states)
                {
                    if (node is not JsonObject entry)
                        continue;
                    int version = entry["version"]?.GetValue<int>() ?? 0;
                    JsonObject stateValues = entry["values"] as JsonObject ?? [];
                    m_settings.SetSection(
                        C_STATE_SECTION_PREFIX + stateId,
                        ExportProviderValues(version, stateValues.ToJsonString()));
                }
            }

            if (root["panels"] is JsonObject panels)
            {
                m_settings.SetSection(
                    C_PANELS_SECTION,
                    panels.Select(static pair => new KeyValuePair<string, string>(
                        pair.Key,
                        pair.Value?.GetValue<bool>() == true ? "true" : "false")));
            }

            EnsureMetadata();
            return true;
        }
        catch (Exception exception)
        {
            Log.Warn(
                "Legacy editor workspace state '{0}' could not be migrated: {1}",
                sourcePath,
                exception.Message);
            return false;
        }
    }

    private void EnsureMetadata()
    {
        m_settings.SetSection(C_META_SECTION, new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["SchemaVersion"] = C_SCHEMA_VERSION.ToString(CultureInfo.InvariantCulture)
        });
    }

    private static Dictionary<string, string> ExportProviderValues(int version, string payload)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Version"] = version.ToString(CultureInfo.InvariantCulture)
        };
        if (JsonNode.Parse(payload) is not JsonObject values)
            return result;
        foreach ((string key, JsonNode? value) in values)
            result[key] = value?.ToJsonString() ?? "null";
        return result;
    }

    private static string GetProviderSectionName(IEditorWorkspaceState provider)
    {
        string prefix = provider switch
        {
            EditorModule => C_MODULE_SECTION_PREFIX,
            EditorPanel => C_PANEL_SECTION_PREFIX,
            _ => C_STATE_SECTION_PREFIX
        };
        return prefix + provider.workspaceStateId;
    }

    private void RemoveLegacyProviderSections(string workspaceStateId, string activeSection)
    {
        string[] candidates =
        [
            C_MODULE_SECTION_PREFIX + workspaceStateId,
            C_PANEL_SECTION_PREFIX + workspaceStateId,
            C_STATE_SECTION_PREFIX + workspaceStateId,
            C_LEGACY_MODULE_SECTION_PREFIX + workspaceStateId
        ];
        for (int i = 0; i < candidates.Length; i++)
        {
            if (!string.Equals(candidates[i], activeSection, StringComparison.Ordinal))
                _ = m_settings.RemoveSection(candidates[i]);
        }
    }

    private static void TryDeleteLegacyWorkspace(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception exception)
        {
            Log.Warn("Legacy editor workspace state '{0}' could not be removed: {1}", path, exception.Message);
        }
    }

    private sealed class RestoredProvider
    {
        internal bool isRestoring;
        internal bool isRestored;
    }
}
