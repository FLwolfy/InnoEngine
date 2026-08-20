using System;

namespace Inno.Editor.Interactions.Menus;

/// <summary>Registers a dynamic editor menu source.</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class EditorMenuSourceAttribute : Attribute
{
    /// <summary>
    /// Creates a dynamic menu-source registration for an exact interaction surface.
    /// </summary>
    /// <param name="area">The exact menu area that receives placements from the source.</param>
    /// <param name="priority">The ordering priority used when multiple dynamic sources contribute to the surface.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="area"/> is empty.</exception>
    public EditorMenuSourceAttribute(string area, int priority = 0)
    {
        if (string.IsNullOrWhiteSpace(area))
            throw new ArgumentException("An editor menu area is required.", nameof(area));
        this.area = area;
        this.priority = priority;
    }

    /// <summary>Gets the contributed menu area.</summary>
    public string area { get; }

    /// <summary>Gets the provider ordering priority.</summary>
    public int priority { get; }
}
