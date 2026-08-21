using System;

namespace Inno.Editor.Interactions;

/// <summary>
/// Interprets neutral editor history payloads for one current-generation feature protocol.
/// </summary>
public abstract class EditorHistoryHandler
{
    /// <summary>
    /// Gets whether this handler can interpret the supplied historical payload schema version.
    /// </summary>
    /// <param name="version">The positive schema version stored by the history entry.</param>
    /// <returns><see langword="true"/> when the payload can be queried and applied.</returns>
    public virtual bool CanReadVersion(int version)
        => GetType().GetCustomAttributes(typeof(EditorHistoryHandlerAttribute), false) is
               [EditorHistoryHandlerAttribute attribute] &&
           version == attribute.version;

    /// <summary>
    /// Determines whether a neutral change can currently transition in the requested direction.
    /// </summary>
    /// <param name="context">The current-generation editor services.</param>
    /// <param name="change">The neutral history change.</param>
    /// <param name="direction">The requested Undo or Redo direction.</param>
    /// <returns>The current transition availability.</returns>
    protected abstract EditorHistoryAvailability Query(
        EditorHistoryContext context,
        EditorHistoryChange change,
        EditorHistoryDirection direction);

    /// <summary>
    /// Atomically applies a neutral history change in the requested direction.
    /// </summary>
    /// <param name="context">The current-generation editor services.</param>
    /// <param name="change">The neutral history change.</param>
    /// <param name="direction">The requested Undo or Redo direction.</param>
    /// <returns>The transition result. A failure must leave the feature in its original state.</returns>
    protected abstract EditorHistoryResult Apply(
        EditorHistoryContext context,
        EditorHistoryChange change,
        EditorHistoryDirection direction);

    /// <summary>
    /// Attempts to merge two adjacent changes to the same logical value.
    /// </summary>
    /// <param name="older">The existing older history change.</param>
    /// <param name="newer">The newly recorded adjacent history change.</param>
    /// <param name="merged">The independently owned merged change when successful.</param>
    /// <returns><see langword="true"/> when <paramref name="merged"/> replaces both input changes.</returns>
    protected virtual bool TryMerge(
        EditorHistoryChange older,
        EditorHistoryChange newer,
        out EditorHistoryChange? merged)
    {
        merged = null;
        return false;
    }

    internal EditorHistoryAvailability QueryInternal(
        EditorHistoryContext context,
        EditorHistoryChange change,
        EditorHistoryDirection direction)
        => CanReadVersion(change.version)
            ? Query(context, change, direction)
            : EditorHistoryAvailability.Unavailable(
                $"History handler '{change.kind}' cannot read payload version {change.version}.");

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
