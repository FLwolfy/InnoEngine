using System;
using System.Collections.Generic;

using Inno.Core.Events;
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

    /// <summary>Creates an interaction runtime for one project.</summary>
    /// <param name="projectDirectory">The project root containing Assets and Library.</param>
    public EditorInteractionRuntime(string projectDirectory)
        : this(new EditorContext(projectDirectory))
    {
    }

    /// <summary>Creates an interaction runtime for an existing passive editor context.</summary>
    /// <param name="context">The passive editor context shared with the presentation backend.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context"/> is <see langword="null"/>.</exception>
    public EditorInteractionRuntime(EditorContext context)
        : base(context)
    {
        m_interactions = new EditorInteractions(context);
        m_catalog = new EditorExtensionCatalog(context, m_interactions);
        m_interactions.Attach(m_catalog);
    }

    /// <summary>Gets the active presentation-independent interaction entry point.</summary>
    public EditorInteractions interactions => m_interactions;

    /// <summary>Gets active dockable panel extensions in deterministic order.</summary>
    public IReadOnlyList<EditorPanelExtension> panels
    {
        get
        {
            RefreshDescriptions();
            return m_panels;
        }
    }

    /// <summary>Gets active modal extensions in deterministic order.</summary>
    public IReadOnlyList<EditorModalExtension> modals
    {
        get
        {
            RefreshDescriptions();
            return m_modals;
        }
    }

    /// <summary>Gets the number of active dockable panels.</summary>
    public int panelCount => panels.Count;

    /// <inheritdoc />
    public override void Start()
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
        if (m_started)
            return;
        _ = m_catalog.extensions;
        m_started = true;
    }

    /// <inheritdoc />
    public override void Update(EditorFrame frame)
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
        if (!m_started)
            Start();
        SetFrame(frame);
        m_interactions.Update();
        m_catalog.UpdateModules();
    }

    /// <summary>Flushes actions queued during the current presentation traversal.</summary>
    public void Flush() => m_interactions.Update();

    /// <summary>
    /// Captures every stateful active module and panel and atomically flushes changed project state
    /// to disk.
    /// </summary>
    /// <exception cref="ObjectDisposedException">Thrown after this runtime has been disposed.</exception>
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

    /// <summary>Dispatches an unhandled keyboard event through contextual shortcuts.</summary>
    /// <param name="keyEvent">The keyboard event received from the application event stream.</param>
    public void HandleKeyPressed(KeyPressedEvent keyEvent)
    {
        ArgumentNullException.ThrowIfNull(keyEvent);
        if (m_interactions.DispatchShortcut(keyEvent))
            keyEvent.HandleInGlobal();
    }

    /// <inheritdoc />
    public override void Dispose()
    {
        if (m_disposed)
            return;
        m_catalog.PrepareShutdown();
        m_disposed = true;
        m_interactions.Shutdown();
        m_catalog.Shutdown(saveState: false);
        m_catalog.Dispose();
        m_panels = [];
        m_modals = [];
        m_describedSnapshot = null;
        GC.SuppressFinalize(this);
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
                new EditorPanelId(registration.attribute.id),
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
}
