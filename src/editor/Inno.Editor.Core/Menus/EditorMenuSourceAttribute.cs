using System;

namespace Inno.Editor.Core.Menus;

/// <summary>Registers a dynamic editor menu source.</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class EditorMenuSourceAttribute : Attribute
{
    /// <summary>Creates a menu source registration.</summary>
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
