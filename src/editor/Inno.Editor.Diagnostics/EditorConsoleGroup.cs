using System;
using System.Collections.Generic;

namespace Inno.Editor.Diagnostics;

/// <summary>
/// Groups equivalent Console occurrences regardless of whether they arrived consecutively.
/// </summary>
public sealed class EditorConsoleGroup
{
    internal EditorConsoleGroup(string identity, EditorConsoleOccurrence[] occurrences)
    {
        this.identity = identity;
        this.occurrences = Array.AsReadOnly(occurrences);
        latest = occurrences[^1];
    }

    /// <summary>
    /// Gets the deterministic content fingerprint used as the presentation identity.
    /// </summary>
    public string identity { get; }

    /// <summary>
    /// Gets every matching occurrence in arrival order.
    /// </summary>
    public IReadOnlyList<EditorConsoleOccurrence> occurrences { get; }

    /// <summary>
    /// Gets the most recently received matching occurrence.
    /// </summary>
    public EditorConsoleOccurrence latest { get; }

    /// <summary>
    /// Gets the total number of matching occurrences.
    /// </summary>
    public int count => occurrences.Count;
}
