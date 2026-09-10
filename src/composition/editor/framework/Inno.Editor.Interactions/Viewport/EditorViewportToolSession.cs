using System;

namespace Inno.Editor.Interactions;

/// <summary>
/// Routes focused viewport input to one active tool while enforcing pointer-capture ownership.
/// </summary>
public sealed class EditorViewportToolSession : IDisposable
{
    private readonly EditorViewportToolContext m_context;
    private EditorViewportTool? m_tool;
    private bool m_disposed;

    /// <summary>
    /// Creates a viewport tool session.
    /// </summary>
    /// <param name="history">
    /// History stack used by gesture transactions.
    /// </param>
    /// <param name="coordinates">
    /// Current viewport coordinate converter.
    /// </param>
    public EditorViewportToolSession(
        IEditorHistory history,
        IEditorViewportCoordinateConverter coordinates)
    {
        m_context = new EditorViewportToolContext(history, coordinates);
    }

    /// <summary>
    /// Gets the active tool, or <see langword="null"/>.
    /// </summary>
    public EditorViewportTool? tool => m_tool;

    /// <summary>
    /// Gets the requested cursor for the active tool.
    /// </summary>
    public EditorViewportCursor cursor => m_tool?.cursor ?? EditorViewportCursor.Arrow;

    /// <summary>
    /// Activates a tool after cancelling any current gesture.
    /// </summary>
    /// <param name="tool">
    /// Tool to activate, or <see langword="null"/> to deactivate.
    /// </param>
    public void SetTool(EditorViewportTool? tool)
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
        if (ReferenceEquals(m_tool, tool))
            return;
        m_context.Cancel();
        m_tool = tool;
    }

    /// <summary>
    /// Routes one immutable pointer sample.
    /// </summary>
    /// <param name="pointer">
    /// Pointer sample with precomputed world coordinates.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when an active tool accepted the sample.
    /// </returns>
    public bool HandlePointer(EditorViewportPointerEvent pointer)
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
        EditorViewportTool? current = m_tool;
        if (current is null || !m_context.Accepts(pointer.pointerId))
            return false;
        switch (pointer.phase)
        {
            case EditorViewportPointerPhase.Down:
                current.OnPointerDown(m_context, pointer);
                break;
            case EditorViewportPointerPhase.Move:
                current.OnPointerMove(m_context, pointer);
                break;
            case EditorViewportPointerPhase.Up:
                current.OnPointerUp(m_context, pointer);
                break;
            case EditorViewportPointerPhase.Cancel:
                current.OnPointerCancel(m_context, pointer);
                break;
        }
        return true;
    }

    /// <summary>
    /// Routes one focused keyboard shortcut.
    /// </summary>
    /// <param name="shortcut">
    /// Pressed key sample.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when consumed.
    /// </returns>
    public bool HandleShortcut(EditorViewportShortcut shortcut)
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
        return m_tool?.OnShortcut(m_context, shortcut) == true;
    }

    /// <summary>
    /// Draws the active tool overlay.
    /// </summary>
    public void DrawOverlay()
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
        m_tool?.DrawOverlay(m_context);
    }

    /// <summary>
    /// Cancels active gesture state and deactivates the tool.
    /// </summary>
    public void Dispose()
    {
        if (m_disposed)
            return;
        m_context.Cancel();
        m_tool = null;
        m_disposed = true;
    }
}
