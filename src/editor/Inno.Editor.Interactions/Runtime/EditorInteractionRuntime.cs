using System;
using System.Collections.Generic;
using System.Linq;

using Inno.Core.Events;
using Inno.Core.Logging;
using Inno.Extensibility.Types;
using Inno.Editor.Core;

namespace Inno.Editor.Interactions;

/// <summary>
/// Hosts attribute-discovered editor extensions without depending on a presentation backend.
/// </summary>
public sealed class EditorInteractionRuntime : Inno.Editor.Core.EditorRuntime
{
    private readonly EditorInteractions m_interactions;
    private readonly EditorExtensionCatalog m_catalog;
    private EditorExtensionCatalog.Snapshot? m_describedSnapshot;
    private EditorPanelExtension[] m_panels = [];
    private EditorModalExtension[] m_modals = [];
    private bool m_started;
    private bool m_disposed;

    /// <summary>
    /// Creates an interaction runtime for one project.
    /// </summary>
    /// <param name="projectDirectory">
    /// The project root containing Assets and Library.
    /// </param>
    /// <param name="types">
    /// The type catalog that owns editor extension generations.
    /// </param>
    /// <param name="logs">
    /// The host-owned logging router used by editor infrastructure.
    /// </param>
    public EditorInteractionRuntime(
        string projectDirectory,
        TypeCatalog types,
        LogRouter logs)
        : this(new EditorContext(projectDirectory), types, logs)
    {
    }

    /// <summary>
    /// Creates an interaction runtime for an existing passive editor context.
    /// </summary>
    /// <param name="context">
    /// The passive editor context shared with the presentation backend.
    /// </param>
    /// <param name="types">
    /// The type catalog that owns editor extension generations.
    /// </param>
    /// <param name="logs">
    /// The host-owned logging router used by editor infrastructure.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="context"/> or <paramref name="logs"/> is <see langword="null"/>.
    /// </exception>
    public EditorInteractionRuntime(
        EditorContext context,
        TypeCatalog types,
        LogRouter logs)
        : this(context, types, logs, Array.Empty<object>())
    {
    }

    /// <summary>
    /// Creates an interaction runtime with stable host-owned extension services.
    /// </summary>
    /// <param name="context">
    /// The passive editor context shared with the presentation backend.
    /// </param>
    /// <param name="hostServices">
    /// Stable host-owned services that editor extension constructors may request by assignable contract.
    /// </param>
    /// <param name="logs">
    /// The host-owned logging router used by editor infrastructure.
    /// </param>
    /// <param name="types">
    /// The type catalog that owns editor extension generations.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="context"/> or <paramref name="hostServices"/> is <see langword="null"/>.
    /// </exception>
    public EditorInteractionRuntime(
        EditorContext context,
        TypeCatalog types,
        LogRouter logs,
        IEnumerable<object> hostServices)
        : base(context)
    {
        ArgumentNullException.ThrowIfNull(types);
        ArgumentNullException.ThrowIfNull(logs);
        ArgumentNullException.ThrowIfNull(hostServices);
        Logger logger = logs.CreateLogger<EditorInteractionRuntime>();
        m_interactions = new EditorInteractions(context, logger);
        m_catalog = new EditorExtensionCatalog(
            types,
            context,
            m_interactions,
            logger,
            new object[] { logs }.Concat(hostServices),
            InvalidateDescriptions);
        m_interactions.Attach(m_catalog);
    }

    /// <summary>
    /// Gets the active presentation-independent interaction entry point.
    /// </summary>
    public EditorInteractions interactions => m_interactions;

    /// <summary>
    /// Gets active dockable panel extensions in deterministic order.
    /// </summary>
    public IReadOnlyList<EditorPanelExtension> panels
    {
        get
        {
            RefreshDescriptions();
            return m_panels;
        }
    }

    /// <summary>
    /// Gets active modal extensions in deterministic order.
    /// </summary>
    public IReadOnlyList<EditorModalExtension> modals
    {
        get
        {
            RefreshDescriptions();
            return m_modals;
        }
    }

    /// <summary>
    /// Gets the number of active dockable panels.
    /// </summary>
    public int panelCount => panels.Count;

    /// <summary>
    /// Starts value processing after validating the current state.
    /// </summary>
    public override void Start()
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
        if (m_started)
            return;
        _ = m_catalog.extensions;
        m_started = true;
    }

    /// <summary>
    /// Recomputes owned state from the current validated inputs.
    /// </summary>
    /// <param name="frame">
    /// The frame consumed by update; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    public override void Update(EditorFrame frame)
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
        if (!m_started)
            Start();
        SetFrame(frame);
        m_interactions.Update();
        m_catalog.UpdateModules();
    }

    /// <summary>
    /// Flushes actions queued during the current presentation traversal.
    /// </summary>
    public void Flush() => m_interactions.Update();

    /// <summary>
    /// Captures every stateful active module and panel and atomically flushes changed project state
    /// to disk.
    /// </summary>
    /// <exception cref="ObjectDisposedException">
    /// Thrown after this runtime has been disposed.
    /// </exception>
    public void SaveState()
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
        m_catalog.SaveState();
    }

    /// <summary>
    /// Freezes automatic extension-state persistence and writes the final state before modules
    /// begin shutting down.
    /// </summary>
    /// <remarks>
    /// This operation is idempotent. After it succeeds, later disposal cannot recapture transient
    /// teardown state such as an empty scene workspace.
    /// </remarks>
    /// <exception cref="ObjectDisposedException">
    /// Thrown after this runtime has been disposed.
    /// </exception>
    public void PrepareShutdown()
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
        m_catalog.PrepareShutdown();
    }

    /// <summary>
    /// Dispatches an unhandled keyboard event through contextual shortcuts.
    /// </summary>
    /// <param name="keyEvent">
    /// The keyboard event received from the application event stream.
    /// </param>
    public void HandleKeyPressed(KeyPressedEvent keyEvent)
    {
        ArgumentNullException.ThrowIfNull(keyEvent);
        if (m_interactions.DispatchShortcut(keyEvent))
            keyEvent.HandleInGlobal();
    }

    /// <summary>
    /// Releases the resources owned by this implementation.
    /// </summary>
    /// <exception cref="AggregateException">
    /// Thrown after all shutdown stages have been attempted when one or more stages failed.
    /// </exception>
    public override void Dispose()
    {
        if (m_disposed)
            return;
        m_disposed = true;
        var failures = new List<Exception>();
        EditorExtensionCatalog.ActionRegistration[] shutdownActions =
            m_catalog.GetActionsForShutdown();
        TryShutdownStage(m_catalog.PrepareShutdown, failures);
        TryShutdownStage(() => m_catalog.Shutdown(saveState: false), failures);
        TryShutdownStage(() => m_interactions.Shutdown(shutdownActions), failures);
        TryShutdownStage(m_catalog.Dispose, failures);
        m_panels = [];
        m_modals = [];
        m_describedSnapshot = null;
        GC.SuppressFinalize(this);
        if (failures.Count != 0)
        {
            throw new AggregateException(
                "One or more editor interaction shutdown stages failed.",
                failures);
        }
    }

    private void RefreshDescriptions()
    {
        EditorExtensionCatalog.Snapshot snapshot = m_catalog.extensions;
        if (ReferenceEquals(snapshot, m_describedSnapshot))
            return;

        var panels = new List<EditorPanelExtension>(snapshot.panels.Length);
        for (int i = 0; i < snapshot.panels.Length; i++)
        {
            EditorExtensionCatalog.PanelRegistration registration = snapshot.panels[i];
            if (snapshot.quarantinedPanels.Contains(registration.panel))
                continue;
            panels.Add(new EditorPanelExtension(
                registration.attribute.id,
                registration.attribute.title,
                registration.attribute.order,
                registration.panel,
                exception => m_catalog.QuarantinePanel(snapshot, registration, exception)));
        }
        m_panels = panels.ToArray();

        var modals = new List<EditorModalExtension>(snapshot.modals.Length);
        for (int i = 0; i < snapshot.modals.Length; i++)
        {
            EditorExtensionCatalog.ModalRegistration registration = snapshot.modals[i];
            if (snapshot.quarantinedModals.Contains(registration.modal))
                continue;
            modals.Add(new EditorModalExtension(
                registration.attribute.id,
                registration.attribute.title,
                registration.attribute.order,
                registration.modal,
                exception => m_catalog.QuarantineModal(snapshot, registration, exception)));
        }
        m_modals = modals.ToArray();
        m_describedSnapshot = snapshot;
    }

    private void InvalidateDescriptions()
    {
        m_panels = [];
        m_modals = [];
        m_describedSnapshot = null;
    }

    private static void TryShutdownStage(Action stage, ICollection<Exception> failures)
    {
        try
        {
            stage();
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }
    }
}
