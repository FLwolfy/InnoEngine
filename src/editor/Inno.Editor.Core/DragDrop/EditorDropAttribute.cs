using System;

namespace Inno.Editor.Core.DragDrop;

/// <summary>Registers a typed editor drop handler for an optional exact surface.</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class EditorDropAttribute : Attribute
{
    /// <summary>
    /// Creates a drop registration that can participate on any interaction surface.
    /// </summary>
    /// <param name="priority">The tie-breaking priority used after source and target specificity.</param>
    public EditorDropAttribute(int priority = 0)
        : this(null, priority)
    {
    }

    /// <summary>
    /// Creates a drop registration scoped to an exact interaction surface.
    /// </summary>
    /// <param name="surface">The exact target surface handled by this registration, or <see langword="null"/> for any surface.</param>
    /// <param name="priority">The tie-breaking priority used after source and target specificity.</param>
    public EditorDropAttribute(Type? surface, int priority = 0)
    {
        this.surface = surface;
        this.priority = priority;
    }

    /// <summary>Gets the optional exact interaction surface.</summary>
    public Type? surface { get; }

    /// <summary>Gets the tie-breaking priority.</summary>
    public int priority { get; }
}
