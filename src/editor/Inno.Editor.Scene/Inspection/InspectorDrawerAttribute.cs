using System;

namespace Inno.Editor.Scene.Inspection;

/// <summary>
/// Associates an inspector drawer with a selected target type.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class InspectorDrawerAttribute : Attribute
{
    /// <summary>
    /// Gets the selected target type handled by the drawer.
    /// </summary>
    public Type targetType { get; }

    /// <summary>
    /// Gets whether derived target types are accepted.
    /// </summary>
    public bool useForChildren { get; }

    /// <summary>
    /// Gets the tie-breaking registration priority.
    /// </summary>
    public int priority { get; }

    /// <summary>
    /// Creates an inspector drawer registration.
    /// </summary>
    public InspectorDrawerAttribute(Type targetType, bool useForChildren = false, int priority = 0)
    {
        this.targetType = targetType ?? throw new ArgumentNullException(nameof(targetType));
        this.useForChildren = useForChildren;
        this.priority = priority;
    }
}
