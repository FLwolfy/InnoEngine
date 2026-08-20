using System;

namespace Inno.Editor.Interactions.Menus;

/// <summary>Places an editor action at an arbitrary path on a menu surface.</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class EditorMenuAttribute : Attribute
{
    /// <summary>
    /// Creates a static placement for the annotated action on an exact menu surface.
    /// </summary>
    /// <param name="area">The exact interaction area whose menu receives the action.</param>
    /// <param name="path">The slash-delimited path used to create parent menus and the leaf label.</param>
    /// <param name="order">The stable ordering value among sibling placements.</param>
    /// <param name="separatorBefore">Whether a visual separator should precede the leaf placement.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="area"/> or <paramref name="path"/> is empty.</exception>
    public EditorMenuAttribute(
        string area,
        string path,
        int order = 0,
        bool separatorBefore = false)
    {
        if (string.IsNullOrWhiteSpace(area))
            throw new ArgumentException("An editor menu area is required.", nameof(area));
        this.area = area;
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("An editor menu path is required.", nameof(path));
        this.path = path;
        this.order = order;
        this.separatorBefore = separatorBefore;
    }

    /// <summary>Gets the menu area.</summary>
    public string area { get; }

    /// <summary>Gets the slash-delimited menu path.</summary>
    public string path { get; }

    /// <summary>Gets the stable menu ordering value.</summary>
    public int order { get; }

    /// <summary>Gets whether a separator is rendered before the item.</summary>
    public bool separatorBefore { get; }
}
