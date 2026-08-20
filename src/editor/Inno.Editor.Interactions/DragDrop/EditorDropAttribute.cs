using System;

namespace Inno.Editor.Interactions.DragDrop;

/// <summary>Registers a typed editor drop handler for an optional exact area.</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class EditorDropAttribute : Attribute
{
    /// <summary>
    /// Creates a drop registration that can participate on any interaction surface.
    /// </summary>
    /// <param name="priority">The tie-breaking priority used after source and target specificity.</param>
    public EditorDropAttribute(int priority = 0)
        : this(string.Empty, priority)
    {
    }

    /// <summary>
    /// Creates a drop registration scoped to an exact interaction area.
    /// </summary>
    /// <param name="area">The exact target area handled by this registration, or an empty string for any area.</param>
    /// <param name="priority">The tie-breaking priority used after source and target specificity.</param>
    public EditorDropAttribute(string area, int priority = 0)
    {
        this.area = area ?? string.Empty;
        this.priority = priority;
    }

    /// <summary>Gets the optional exact interaction area.</summary>
    public string area { get; }

    /// <summary>Gets the tie-breaking priority.</summary>
    public int priority { get; }
}
