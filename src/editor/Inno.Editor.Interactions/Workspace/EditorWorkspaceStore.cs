using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;

using Inno.Core.Logging;
using Inno.Editor.Core;

namespace Inno.Editor.Interactions;

internal sealed class EditorWorkspaceStore
{
    private const double C_SAVE_INTERVAL_SECONDS = 2.0;
    private const string C_MODULE_SECTION_PREFIX = "Module.";
    private const string C_PANEL_SECTION_PREFIX = "Panel.";
    private const string C_STATE_SECTION_PREFIX = "State.";
    private const string C_PANELS_SECTION = "Panels";

    private readonly EditorProjectSettings m_settings;
    private readonly ConditionalWeakTable<object, RestoredProvider> m_restoredProviders = new();
    private double m_nextSaveTime;

    internal EditorWorkspaceStore(EditorContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        m_settings = context.settings;
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
                    ExportProviderValues(writer.Export()));
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
        if (!m_settings.TryGetSection(sectionName, out IReadOnlyDictionary<string, string> values))
            return new EditorWorkspaceStateReader(null);
        var root = new JsonObject();
        foreach ((string key, string value) in values)
            root[key] = JsonNode.Parse(value);
        return new EditorWorkspaceStateReader(root.ToJsonString());
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

    private static Dictionary<string, string> ExportProviderValues(string payload)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
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

    private sealed class RestoredProvider
    {
        internal bool isRestoring;
        internal bool isRestored;
    }
}
