using System;

namespace Inno.Editor.Interactions;

/// <summary>
/// Interprets neutral editor history payloads for one current-generation feature protocol.
/// </summary>
public abstract class EditorHistoryHandler
{
    /// <summary>
    /// Determines whether a neutral change can currently transition in the requested direction.
    /// </summary>
    /// <param name="context">
    /// The current-generation editor services.
    /// </param>
    /// <param name="change">
    /// The neutral history change.
    /// </param>
    /// <param name="direction">
    /// The requested Undo or Redo direction.
    /// </param>
    /// <returns>
    /// The current transition availability.
    /// </returns>
    protected abstract EditorHistoryAvailability Query(
        EditorHistoryContext context,
        EditorHistoryChange change,
        EditorHistoryDirection direction);

    /// <summary>
    /// Atomically applies a neutral history change in the requested direction.
    /// </summary>
    /// <param name="context">
    /// The current-generation editor services.
    /// </param>
    /// <param name="change">
    /// The neutral history change.
    /// </param>
    /// <param name="direction">
    /// The requested Undo or Redo direction.
    /// </param>
    /// <returns>
    /// The transition result. A failure must leave the feature in its original state.
    /// </returns>
    protected abstract EditorHistoryResult Apply(
        EditorHistoryContext context,
        EditorHistoryChange change,
        EditorHistoryDirection direction);

    /// <summary>
    /// Attempts to merge two adjacent changes to the same logical value.
    /// </summary>
    /// <param name="older">
    /// The existing older history change.
    /// </param>
    /// <param name="newer">
    /// The newly recorded adjacent history change.
    /// </param>
    /// <param name="merged">
    /// The independently owned merged change when successful.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when <paramref name="merged"/> replaces both input changes.
    /// </returns>
    protected virtual bool TryMerge(
        EditorHistoryChange older,
        EditorHistoryChange newer,
        out EditorHistoryChange? merged)
    {
        merged = null;
        return false;
    }

    /// <summary>
    /// Creates a failed result for a transition whose compensation also failed.
    /// </summary>
    /// <param name="message">
    /// The diagnostic containing both the transition and compensation failures.
    /// </param>
    /// <returns>
    /// A failure that faults the owning history.
    /// </returns>
    protected static EditorHistoryResult StateIntegrityFailure(string message)
        => EditorHistoryResult.StateIntegrityLost(message);

    internal EditorHistoryAvailability QueryInternal(
        EditorHistoryContext context,
        EditorHistoryChange change,
        EditorHistoryDirection direction)
        => Query(context, change, direction);

    internal EditorHistoryResult ApplyInternal(
        EditorHistoryContext context,
        EditorHistoryChange change,
        EditorHistoryDirection direction)
        => Apply(context, change, direction);

    internal bool TryMergeInternal(
        EditorHistoryChange older,
        EditorHistoryChange newer,
        out EditorHistoryChange? merged)
        => TryMerge(older, newer, out merged);
}
