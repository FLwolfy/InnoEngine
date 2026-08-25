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
    /// <param name="command">The command invoked by the leaf.</param>
    /// <param name="order">The stable ordering value among sibling placements.</param>
    /// <param name="separatorBefore">Whether a visual separator should precede the leaf placement.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="path"/> is empty.</exception>
    public void Add(
        string path,
        EditorCommand command,
        int order = 0,
        bool separatorBefore = false)
        => AddCore(path, command.id, order, separatorBefore, argument: null);

    /// <summary>Adds a typed dynamic command placement to the current menu.</summary>
    /// <typeparam name="TArgument">The command argument type.</typeparam>
    /// <param name="path">The slash-delimited path used to create parent menus and the leaf label.</param>
    /// <param name="command">The typed command invoked by the leaf.</param>
    /// <param name="argument">The typed argument captured for this menu generation.</param>
    /// <param name="order">The stable ordering value among sibling placements.</param>
    /// <param name="separatorBefore">Whether a visual separator should precede the leaf placement.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="path"/> is empty.</exception>
    public void Add<TArgument>(
        string path,
        EditorCommand<TArgument> command,
        TArgument argument,
        int order = 0,
        bool separatorBefore = false)
        => AddCore(path, command.id, order, separatorBefore, argument);

    internal void Add(
        string path,
        string actionId,
        int order = 0,
        bool separatorBefore = false,
        object? argument = null)
        => AddCore(path, new EditorActionId(actionId), order, separatorBefore, argument);

    private void AddCore(
        string path,
        EditorActionId actionId,
        int order,
        bool separatorBefore,
        object? argument)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("A menu path is required.", nameof(path));
        m_items.Add(new EditorMenuPlacement(path, actionId, order, separatorBefore, argument));
    }

    internal IReadOnlyList<EditorMenuPlacement> items => m_items;
}

internal sealed class EditorMenuPlacement
{
    internal EditorMenuPlacement(
        string path,
        EditorActionId actionId,
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

    /// <summary>Gets the command identifier.</summary>
    internal EditorActionId actionId { get; }

    /// <summary>Gets the stable menu order.</summary>
    internal int order { get; }

    /// <summary>Gets whether a separator precedes this placement.</summary>
    internal bool separatorBefore { get; }

    /// <summary>Gets the optional command argument.</summary>
    internal object? argument { get; }
}
