using System;
using Inno.Core.Mathematics;

namespace Inno.Editor.Interactions;

/// <summary>Coordinates pointer capture, transforms, and one history transaction for a viewport gesture.</summary>
public sealed class EditorViewportToolContext
{
    private readonly IEditorHistory m_history;
    private readonly IEditorViewportCoordinateConverter m_coordinates;
    private EditorHistoryTransaction? m_transaction;
    private int? m_capturedPointerId;

    internal EditorViewportToolContext(
        IEditorHistory history,
        IEditorViewportCoordinateConverter coordinates)
    {
        m_history = history ?? throw new ArgumentNullException(nameof(history));
        m_coordinates = coordinates ?? throw new ArgumentNullException(nameof(coordinates));
    }

    /// <summary>Gets whether this tool owns a pointer.</summary>
    public bool hasPointerCapture => m_capturedPointerId.HasValue;

    /// <summary>Gets the captured pointer identity, or <see langword="null"/>.</summary>
    public int? capturedPointerId => m_capturedPointerId;

    /// <summary>Gets whether a history transaction is active for the current gesture.</summary>
    public bool hasHistoryGesture => m_transaction is not null;

    /// <summary>Captures one pointer until explicit release or cancellation.</summary>
    /// <param name="pointerId">Platform pointer identity.</param>
    public void CapturePointer(int pointerId)
    {
        if (m_capturedPointerId.HasValue && m_capturedPointerId.Value != pointerId)
            throw new InvalidOperationException("A viewport tool cannot capture two pointers concurrently.");
        m_capturedPointerId = pointerId;
    }

    /// <summary>Releases the currently captured pointer.</summary>
    public void ReleasePointer()
    {
        if (m_transaction is not null)
            throw new InvalidOperationException("Complete the viewport history gesture before releasing its pointer.");
        m_capturedPointerId = null;
    }

    /// <summary>Begins the only history transaction allowed for the current captured gesture.</summary>
    /// <param name="name">User-facing Undo operation name.</param>
    public void BeginHistoryGesture(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (!hasPointerCapture)
            throw new InvalidOperationException("A viewport history gesture requires pointer capture.");
        if (m_transaction is not null)
            throw new InvalidOperationException("The current viewport gesture already owns a history transaction.");
        m_transaction = m_history.BeginTransaction(name);
    }

    /// <summary>Commits or rolls back the current gesture transaction exactly once.</summary>
    /// <param name="commit"><see langword="true"/> to commit; otherwise, roll back.</param>
    public void CompleteHistoryGesture(bool commit)
    {
        EditorHistoryTransaction? transaction = m_transaction;
        if (transaction is null)
            return;
        m_transaction = null;
        if (commit)
            transaction.Commit();
        else
            _ = transaction.Rollback();
        transaction.Dispose();
    }

    /// <summary>Converts viewport-local pixel coordinates to world coordinates.</summary>
    /// <param name="screenPosition">Viewport-local pixel coordinates.</param>
    /// <returns>World coordinates.</returns>
    public Vector2 ScreenToWorld(Vector2 screenPosition)
        => m_coordinates.ScreenToWorld(screenPosition);

    /// <summary>Converts world coordinates to viewport-local pixel coordinates.</summary>
    /// <param name="worldPosition">World coordinates.</param>
    /// <returns>Viewport-local pixel coordinates.</returns>
    public Vector2 WorldToScreen(Vector2 worldPosition)
        => m_coordinates.WorldToScreen(worldPosition);

    internal bool Accepts(int pointerId)
        => !m_capturedPointerId.HasValue || m_capturedPointerId.Value == pointerId;

    internal void Cancel()
    {
        CompleteHistoryGesture(commit: false);
        m_capturedPointerId = null;
    }
}
