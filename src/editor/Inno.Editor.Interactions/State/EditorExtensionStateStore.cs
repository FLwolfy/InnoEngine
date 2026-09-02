using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

using Inno.Core.Logging;
using Inno.Editor.Core;

namespace Inno.Editor.Interactions;

internal sealed class EditorExtensionStateStore
{
    private const double C_SAVE_INTERVAL_SECONDS = 2.0;
    private const string C_MODULE_SECTION_PREFIX = "Module.";
    private const string C_PANEL_SECTION_PREFIX = "Panel.";
    private const string C_PANELS_SECTION = "Panels";

    private readonly EditorContext m_context;
    private readonly EditorExtensionStateDiagnosticPublisher m_diagnostics = new();
    private readonly Logger m_log;
    private readonly ConditionalWeakTable<object, RestoredOwner> m_restoredOwners = new();
    private double m_nextSaveTime;
    private bool m_isShutdownPrepared;

    internal EditorExtensionStateStore(EditorContext context, Logger log)
    {
        ArgumentNullException.ThrowIfNull(context);
        m_context = context;
        m_log = log ?? throw new ArgumentNullException(nameof(log));
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
        IReadOnlyList<EditorExtensionCatalog.StateRegistration> registrations,
        IReadOnlyList<EditorExtensionCatalog.PanelRegistration> panels)
    {
        var failures = new List<(string Message, Exception Exception)>();
        for (int i = 0; i < registrations.Count; i++)
        {
            EditorExtensionCatalog.StateRegistration registration = registrations[i];
            if (!m_restoredOwners.TryGetValue(registration.owner, out RestoredOwner? owner) ||
                !owner.isRestored)
            {
                continue;
            }
            try
            {
                var state = new EditorJsonState();
                registration.capture(state);
                m_context.SetLayoutSection(
                    GetSectionName(registration),
                    state.Export());
            }
            catch (Exception exception)
            {
                failures.Add((
                    $"Extension '{registration.id}' failed to capture state: {exception.Message}",
                    exception));
            }
        }
        if (m_diagnostics.PublishCapture(failures.Select(static failure => failure.Message).ToArray()))
        {
            for (int i = 0; i < failures.Count; i++)
                m_log.Write(
                    LogLevel.Error,
                    "{0} Full exception: {1}",
                    [failures[i].Message, failures[i].Exception]);
        }

        var panelValues = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int i = 0; i < panels.Count; i++)
            panelValues[panels[i].attribute.id] = panels[i].panel.isOpen ? "true" : "false";
        m_context.SetLayoutSection(C_PANELS_SECTION, panelValues);
    }

    internal void Restore(IReadOnlyList<EditorExtensionCatalog.StateRegistration> registrations)
    {
        var failures = new List<(string Message, Exception Exception)>();
        for (int i = 0; i < registrations.Count; i++)
        {
            EditorExtensionCatalog.StateRegistration registration = registrations[i];
            RestoredOwner owner = m_restoredOwners.GetValue(
                registration.owner,
                static _ => new RestoredOwner());
            if (owner.isRestored || owner.isRestoring)
                continue;
            owner.isRestoring = true;
            try
            {
                registration.restore(CreateState(registration));
                owner.isRestored = true;
            }
            catch (Exception exception)
            {
                _ = m_restoredOwners.Remove(registration.owner);
                failures.Add((
                    $"Extension '{registration.id}' failed to restore state: {exception.Message}",
                    exception));
            }
            finally
            {
                owner.isRestoring = false;
            }
        }
        if (m_diagnostics.PublishRestore(failures.Select(static failure => failure.Message).ToArray()))
        {
            for (int i = 0; i < failures.Count; i++)
                m_log.Write(
                    LogLevel.Error,
                    "{0} Full exception: {1}",
                    [failures[i].Message, failures[i].Exception]);
        }
        SaveIfChanged();
    }

    internal void ClearDiagnostics()
        => m_diagnostics.Dispose();

    internal void Update(
        double elapsedSeconds,
        IReadOnlyList<EditorExtensionCatalog.StateRegistration> registrations,
        IReadOnlyList<EditorExtensionCatalog.PanelRegistration> panels)
    {
        if (m_isShutdownPrepared || elapsedSeconds < m_nextSaveTime)
            return;
        m_nextSaveTime = elapsedSeconds + C_SAVE_INTERVAL_SECONDS;
        Capture(registrations, panels);
        SaveIfChanged();
    }

    internal void Save(
        IReadOnlyList<EditorExtensionCatalog.StateRegistration> registrations,
        IReadOnlyList<EditorExtensionCatalog.PanelRegistration> panels)
    {
        if (m_isShutdownPrepared)
        {
            SaveIfChanged();
            return;
        }
        Capture(registrations, panels);
        SaveIfChanged();
    }

    internal void PrepareShutdown(
        IReadOnlyList<EditorExtensionCatalog.StateRegistration> registrations,
        IReadOnlyList<EditorExtensionCatalog.PanelRegistration> panels)
    {
        if (m_isShutdownPrepared)
            return;

        // Freeze periodic persistence before capturing the terminal extension snapshot. Module
        // shutdown may unload scenes and clear panels, and that transient teardown state must
        // never replace the state the user had immediately before closing the editor.
        m_isShutdownPrepared = true;
        Capture(registrations, panels);
        SaveIfChanged();
    }

    private EditorState CreateState(EditorExtensionCatalog.StateRegistration registration)
    {
        string sectionName = GetSectionName(registration);
        if (!m_context.TryGetLayoutSection(sectionName, out IReadOnlyDictionary<string, string> values))
            return new EditorJsonState(values: null);
        return new EditorJsonState(values);
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
                m_log.Write(
                    LogLevel.Error,
                    "Editor extension state could not be saved to '{0}': {1}",
                    [m_context.layoutPath, exception]);
            }
        }
    }

    private static string GetSectionName(EditorExtensionCatalog.StateRegistration registration)
    {
        string prefix = registration.kind switch
        {
            EditorExtensionCatalog.StateOwnerKind.Module => C_MODULE_SECTION_PREFIX,
            EditorExtensionCatalog.StateOwnerKind.Panel => C_PANEL_SECTION_PREFIX,
            _ => throw new ArgumentOutOfRangeException(nameof(registration))
        };
        return prefix + registration.id;
    }

    private sealed class RestoredOwner
    {
        internal bool isRestoring;
        internal bool isRestored;
    }
}
