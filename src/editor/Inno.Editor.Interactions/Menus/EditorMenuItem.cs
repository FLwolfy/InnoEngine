using System;
using System.Collections.Generic;

using Inno.Editor.Interactions.Actions;

namespace Inno.Editor.Interactions.Menus;

/// <summary>Represents one immutable contextual menu node.</summary>
public sealed class EditorMenuItem
{
    /// <summary>
    /// Creates an immutable leaf or submenu node in a resolved menu model.
    /// </summary>
    /// <param name="label">The visible contextual label of the node.</param>
    /// <param name="actionId">The leaf action identifier, or an empty string for a structural submenu.</param>
    /// <param name="order">The stable ordering value among sibling nodes.</param>
    /// <param name="separatorBefore">Whether a visual separator should precede this node.</param>
    /// <param name="status">The resolved contextual presentation state.</param>
    /// <param name="children">The immutable child nodes for a submenu.</param>
    /// <param name="argument">The optional value forwarded when the leaf action executes.</param>
    public EditorMenuItem(
        string label,
        string actionId,
        int order,
        bool separatorBefore,
        EditorActionState status,
        IReadOnlyList<EditorMenuItem>? children = null,
        object? argument = null)
    {
        this.label = label ?? string.Empty;
        this.actionId = actionId ?? string.Empty;
        this.order = order;
        this.separatorBefore = separatorBefore;
        this.status = status;
        this.children = children ?? Array.Empty<EditorMenuItem>();
        this.argument = argument;
    }

    /// <summary>Gets the visible node label.</summary>
    public string label { get; }

    /// <summary>Gets the command id, or an empty string for a submenu.</summary>
    public string actionId { get; }

    /// <summary>Gets the stable ordering value.</summary>
    public int order { get; }

    /// <summary>Gets whether a separator precedes this node.</summary>
    public bool separatorBefore { get; }

    /// <summary>Gets the current command presentation state.</summary>
    public EditorActionState status { get; }

    /// <summary>Gets child menu nodes.</summary>
    public IReadOnlyList<EditorMenuItem> children { get; }

    /// <summary>Gets the optional command argument.</summary>
    public object? argument { get; }
}
