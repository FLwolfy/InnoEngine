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
    /// Captures every active workspace provider and atomically flushes changed project state to disk.
    /// </summary>
    /// <exception cref="ObjectDisposedException">Thrown after this runtime has been disposed.</exception>
    public void SaveWorkspace()
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
        m_catalog.SaveWorkspace();
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
        m_disposed = true;
        m_catalog.SaveWorkspace();
        m_interactions.Shutdown();
        m_catalog.Shutdown(saveWorkspace: false);
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

        m_panels = new EditorPanelExtension[snapshot.panels.Length];
        for (int i = 0; i < snapshot.panels.Length; i++)
        {
            EditorExtensionCatalog.PanelRegistration registration = snapshot.panels[i];
            m_panels[i] = new EditorPanelExtension(
                registration.attribute.id,
                registration.attribute.title,
                registration.attribute.order,
                registration.panel);
        }

        m_modals = new EditorModalExtension[snapshot.modals.Length];
        for (int i = 0; i < snapshot.modals.Length; i++)
        {
            EditorExtensionCatalog.ModalRegistration registration = snapshot.modals[i];
            m_modals[i] = new EditorModalExtension(
                registration.attribute.id,
                registration.attribute.title,
                registration.attribute.order,
                registration.modal);
        }
        m_describedSnapshot = snapshot;
    }
}
