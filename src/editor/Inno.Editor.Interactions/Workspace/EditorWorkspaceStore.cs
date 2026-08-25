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
    private const string C_PANELS_SECTION = "Panels";

    private readonly EditorContext m_context;
    private readonly EditorWorkspaceDiagnosticPublisher m_diagnostics = new();
    private readonly ConditionalWeakTable<object, RestoredProvider> m_restoredProviders = new();
    private double m_nextSaveTime;
    private bool m_isShutdownPrepared;

    internal EditorWorkspaceStore(EditorContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        m_context = context;
    }

    internal bool TryGetPanelOpen(string panelId, out bool isOpen)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(panelId);
        if (m_context.TryGetLayoutSection(C_PANELS_SECTION, out IReadOnlyDictionary<string, string> values) &&
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
        var failures = new List<(string Message, Exception Exception)>();
        for (int i = 0; i < providers.Count; i++)
        {
            EditorExtensionCatalog.WorkspaceRegistration registration = providers[i];
            IEditorWorkspaceState provider = registration.provider;
            if (!m_restoredProviders.TryGetValue(provider, out RestoredProvider? state) ||
                !state.isRestored)
            {
                continue;
            }
            string sectionName = GetProviderSectionName(registration);
            try
            {
                var writer = new EditorWorkspaceStateWriter();
                provider.CaptureWorkspaceState(writer);
                m_context.SetLayoutSection(
                    sectionName,
                    ExportProviderValues(writer.Export()));
            }
            catch (Exception exception)
            {
                failures.Add((
                    $"Provider '{registration.id}' failed to capture state: {exception.Message}",
                    exception));
            }
        }
        if (m_diagnostics.PublishCapture(failures.Select(static failure => failure.Message).ToArray()))
        {
            for (int i = 0; i < failures.Count; i++)
                Log.Error("{0} Full exception: {1}", failures[i].Message, failures[i].Exception);
        }

        var panelValues = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int i = 0; i < panels.Count; i++)
            panelValues[panels[i].attribute.id] = panels[i].panel.isOpen ? "true" : "false";
        m_context.SetLayoutSection(C_PANELS_SECTION, panelValues);
    }

    internal void Restore(IReadOnlyList<EditorExtensionCatalog.WorkspaceRegistration> providers)
    {
        var failures = new List<(string Message, Exception Exception)>();
        for (int i = 0; i < providers.Count; i++)
        {
            EditorExtensionCatalog.WorkspaceRegistration registration = providers[i];
            IEditorWorkspaceState provider = registration.provider;
            RestoredProvider state = m_restoredProviders.GetValue(
                provider,
                static _ => new RestoredProvider());
            if (state.isRestored || state.isRestoring)
                continue;
            state.isRestoring = true;
            try
            {
                EditorWorkspaceStateReader reader = CreateReader(registration);
                provider.RestoreWorkspaceState(reader);
                state.isRestored = true;
            }
            catch (Exception exception)
            {
                _ = m_restoredProviders.Remove(provider);
                failures.Add((
                    $"Provider '{registration.id}' failed to restore state: {exception.Message}",
                    exception));
            }
            finally
            {
                state.isRestoring = false;
            }
        }
        if (m_diagnostics.PublishRestore(failures.Select(static failure => failure.Message).ToArray()))
        {
            for (int i = 0; i < failures.Count; i++)
                Log.Error("{0} Full exception: {1}", failures[i].Message, failures[i].Exception);
        }
        SaveIfChanged();
    }

    internal void ClearDiagnostics()
        => m_diagnostics.Dispose();

    internal void Update(
        double elapsedSeconds,
        IReadOnlyList<EditorExtensionCatalog.WorkspaceRegistration> providers,
        IReadOnlyList<EditorExtensionCatalog.PanelRegistration> panels)
    {
        if (m_isShutdownPrepared)
            return;
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
        if (m_isShutdownPrepared)
        {
            SaveIfChanged();
            return;
        }
        Capture(providers, panels);
        SaveIfChanged();
    }

    internal void PrepareShutdown(
        IReadOnlyList<EditorExtensionCatalog.WorkspaceRegistration> providers,
        IReadOnlyList<EditorExtensionCatalog.PanelRegistration> panels)
    {
        if (m_isShutdownPrepared)
            return;

        // Freeze periodic persistence before capturing the terminal workspace snapshot. Module
        // shutdown may unload scenes and clear panels, and that transient teardown state must
        // never replace the state the user had immediately before closing the editor.
        m_isShutdownPrepared = true;
        Capture(providers, panels);
        SaveIfChanged();
    }

    private EditorWorkspaceStateReader CreateReader(
        EditorExtensionCatalog.WorkspaceRegistration registration)
    {
        string sectionName = GetProviderSectionName(registration);
        if (!m_context.TryGetLayoutSection(sectionName, out IReadOnlyDictionary<string, string> values))
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
            _ = m_context.SaveLayoutIfChanged();
            m_diagnostics.ResolveSave();
        }
        catch (Exception exception)
        {
            if (m_diagnostics.PublishSave(exception))
            {
                Log.Error(
                    "Editor workspace state could not be saved to '{0}': {1}",
                    m_context.layoutPath,
                    exception);
            }
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

    private static string GetProviderSectionName(
        EditorExtensionCatalog.WorkspaceRegistration registration)
    {
        string prefix = registration.provider switch
        {
            EditorModule => C_MODULE_SECTION_PREFIX,
            EditorPanel => C_PANEL_SECTION_PREFIX,
            _ => throw new InvalidOperationException(
                "Only editor modules and panels can own workspace state.")
        };
        return prefix + registration.id;
    }

    private sealed class RestoredProvider
    {
        internal bool isRestoring;
        internal bool isRestored;
    }
}
