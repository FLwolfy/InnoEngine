using System;

namespace Inno.Editor.Core.DragDrop;

/// <summary>Registers a typed editor drop handler for an optional exact surface.</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class EditorDropAttribute : Attribute
{
    /// <summary>Creates a global drop registration.</summary>
    public EditorDropAttribute(int priority = 0)
        : this(null, priority)
    {
    }

    /// <summary>Creates a surface-specific drop registration.</summary>
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
