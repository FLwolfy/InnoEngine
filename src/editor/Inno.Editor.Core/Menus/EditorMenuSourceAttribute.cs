using System;

namespace Inno.Editor.Core.Menus;

/// <summary>Registers a dynamic editor menu source.</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class EditorMenuSourceAttribute : Attribute
{
    /// <summary>
    /// Creates a dynamic menu-source registration for an exact interaction surface.
    /// </summary>
    /// <param name="surface">The exact menu surface that receives placements from the source.</param>
    /// <param name="priority">The ordering priority used when multiple dynamic sources contribute to the surface.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="surface"/> is <see langword="null"/>.</exception>
    public EditorMenuSourceAttribute(Type surface, int priority = 0)
    {
        this.surface = surface ?? throw new ArgumentNullException(nameof(surface));
        this.priority = priority;
    }

    /// <summary>Gets the contributed menu surface.</summary>
    public Type surface { get; }

    /// <summary>Gets the provider ordering priority.</summary>
    public int priority { get; }
}
