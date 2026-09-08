using System;
using System.Collections.Generic;

namespace Inno.Editor.Diagnostics;

/// <summary>
/// Provides one immutable and internally consistent view of editor Console state.
/// </summary>
public sealed class EditorConsoleSnapshot
{
    internal EditorConsoleSnapshot(
        long revision,
        EditorConsoleOccurrence[] occurrences,
        EditorConsoleGroup[] groups)
    {
        this.revision = revision;
        this.occurrences = Array.AsReadOnly(occurrences);
        this.groups = Array.AsReadOnly(groups);
    }

    /// <summary>
    /// Gets the monotonic revision of the captured Console state.
    /// </summary>
    public long revision { get; }

    /// <summary>
    /// Gets every current occurrence in chronological arrival order.
    /// </summary>
    public IReadOnlyList<EditorConsoleOccurrence> occurrences { get; }

    /// <summary>
    /// Gets global fingerprint groups ordered by their latest occurrence.
    /// </summary>
    public IReadOnlyList<EditorConsoleGroup> groups { get; }
}
