using System;

namespace Inno.Editor.Scene.Inspection;

/// <summary>
/// Associates a property drawer with a declared property type.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class PropertyDrawerAttribute : Attribute
{
    /// <summary>
    /// Gets the declared property type handled by the drawer.
    /// </summary>
    public Type targetType { get; }

    /// <summary>
    /// Gets whether assignable derived types are accepted.
    /// </summary>
    public bool useForChildren { get; }

    /// <summary>
    /// Gets the tie-breaking registration priority.
    /// </summary>
    public int priority { get; }

    /// <summary>
    /// Creates a property drawer registration.
    /// </summary>
    public PropertyDrawerAttribute(Type targetType, bool useForChildren = false, int priority = 0)
    {
        this.targetType = targetType ?? throw new ArgumentNullException(nameof(targetType));
        this.useForChildren = useForChildren;
        this.priority = priority;
    }
}
