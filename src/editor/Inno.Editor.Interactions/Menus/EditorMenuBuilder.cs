using System;
using System.Collections.Generic;

namespace Inno.Editor.Interactions;

/// <summary>Collects dynamic menu item placements.</summary>
public sealed class EditorMenuBuilder
{
    private readonly List<EditorMenuPlacement> m_items = [];

    /// <summary>
    /// Adds a dynamic action placement to the menu currently being constructed.
    /// </summary>
    /// <param name="path">The slash-delimited path used to create parent menus and the leaf label.</param>
    /// <param name="actionId">The stable action identifier invoked by the leaf.</param>
    /// <param name="order">The stable ordering value among sibling placements.</param>
    /// <param name="separatorBefore">Whether a visual separator should precede the leaf placement.</param>
    /// <param name="argument">An optional argument captured for this menu generation.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="path"/> or <paramref name="actionId"/> is empty.</exception>
    public void Add(
        string path,
        string actionId,
        int order = 0,
        bool separatorBefore = false,
        object? argument = null)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("A menu path is required.", nameof(path));
        ArgumentException.ThrowIfNullOrWhiteSpace(actionId);
        m_items.Add(new EditorMenuPlacement(path, actionId, order, separatorBefore, argument));
    }

    internal IReadOnlyList<EditorMenuPlacement> items => m_items;
}

internal sealed class EditorMenuPlacement
{
    internal EditorMenuPlacement(
        string path,
        string actionId,
        int order,
        bool separatorBefore,
        object? argument)
    {
        this.path = path;
        this.actionId = actionId;
        this.order = order;
        this.separatorBefore = separatorBefore;
        this.argument = argument;
    }

    internal string path { get; }

    /// <summary>Gets the action identifier.</summary>
    internal string actionId { get; }

    /// <summary>Gets the stable menu order.</summary>
    internal int order { get; }

    /// <summary>Gets whether a separator precedes this placement.</summary>
    internal bool separatorBefore { get; }

    /// <summary>Gets the optional action argument.</summary>
    internal object? argument { get; }
}
