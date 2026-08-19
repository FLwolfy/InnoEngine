using System;

namespace Inno.Editor.Core.Menus;

/// <summary>Places an editor action at an arbitrary path on a menu surface.</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class EditorMenuAttribute : Attribute
{
    /// <summary>Creates a menu placement.</summary>
    public EditorMenuAttribute(
        Type surface,
        string path,
        int order = 0,
        bool separatorBefore = false)
    {
        this.surface = surface ?? throw new ArgumentNullException(nameof(surface));
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("An editor menu path is required.", nameof(path));
        this.path = path;
        this.order = order;
        this.separatorBefore = separatorBefore;
    }

    /// <summary>Gets the menu surface.</summary>
    public Type surface { get; }

    /// <summary>Gets the slash-delimited menu path.</summary>
    public string path { get; }

    /// <summary>Gets the stable menu ordering value.</summary>
    public int order { get; }

    /// <summary>Gets whether a separator is rendered before the item.</summary>
    public bool separatorBefore { get; }
}
