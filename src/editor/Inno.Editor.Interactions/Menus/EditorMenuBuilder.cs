using System;
using System.Collections.Generic;

namespace Inno.Editor.Interactions.Menus;

/// <summary>Collects dynamic menu item placements.</summary>
public sealed class EditorMenuBuilder
{
    private readonly List<EditorMenuPlacement> m_items = [];

    /// <summary>
    /// Adds a dynamic action placement to the menu currently being constructed.
    /// </summary>
    /// <param name="path">The slash-delimited path used to create parent menus and the leaf label.</param>
    /// <param name="actionId">The stable identifier of the action invoked by the leaf.</param>
    /// <param name="order">The stable ordering value among sibling placements.</param>
    /// <param name="separatorBefore">Whether a visual separator should precede the leaf placement.</param>
    /// <param name="argument">An optional placement-specific value forwarded to the action context.</param>
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
        if (string.IsNullOrWhiteSpace(actionId))
            throw new ArgumentException("An action id is required.", nameof(actionId));
        m_items.Add(new EditorMenuPlacement(path, actionId, order, separatorBefore, argument));
    }

    /// <summary>Gets the placements collected by this builder.</summary>
    public IReadOnlyList<EditorMenuPlacement> items => m_items;
}

/// <summary>Describes one dynamically contributed menu placement.</summary>
public sealed class EditorMenuPlacement
{
    /// <summary>
    /// Creates an immutable dynamic menu placement.
    /// </summary>
    /// <param name="path">The slash-delimited path used to create parent menus and the leaf label.</param>
    /// <param name="actionId">The stable identifier of the action invoked by the leaf.</param>
    /// <param name="order">The stable ordering value among sibling placements.</param>
    /// <param name="separatorBefore">Whether a visual separator should precede the leaf placement.</param>
    /// <param name="argument">An optional placement-specific value forwarded to the action context.</param>
    public EditorMenuPlacement(
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

    /// <summary>Gets the slash-separated menu path.</summary>
    public string path { get; }

    /// <summary>Gets the command identifier.</summary>
    public string actionId { get; }

    /// <summary>Gets the stable menu order.</summary>
    public int order { get; }

    /// <summary>Gets whether a separator precedes this placement.</summary>
    public bool separatorBefore { get; }

    /// <summary>Gets the optional command argument.</summary>
    public object? argument { get; }
}
