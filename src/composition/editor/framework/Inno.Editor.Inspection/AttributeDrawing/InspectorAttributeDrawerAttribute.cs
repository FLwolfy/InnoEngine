using System;

namespace Inno.Editor.Inspection;

/// <summary>
/// Associates an Inspector attribute drawer with an attribute type.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class InspectorAttributeDrawerAttribute : Attribute
{
    /// <summary>
    /// Creates an Inspector attribute drawer registration.
    /// </summary>
    /// <param name="targetType">
    /// Attribute type handled by the drawer.
    /// </param>
    /// <param name="useForChildren">
    /// Whether derived attribute types are accepted.
    /// </param>
    /// <param name="priority">
    /// Tie-breaking priority after inheritance distance.
    /// </param>
    public InspectorAttributeDrawerAttribute(
        Type targetType,
        bool useForChildren = false,
        int priority = 0)
    {
        ArgumentNullException.ThrowIfNull(targetType);
        if (!typeof(Attribute).IsAssignableFrom(targetType))
            throw new ArgumentException($"'{targetType.FullName}' is not an attribute type.", nameof(targetType));
        this.targetType = targetType;
        this.useForChildren = useForChildren;
        this.priority = priority;
    }

    /// <summary>
    /// Gets the handled attribute type.
    /// </summary>
    public Type targetType { get; }

    /// <summary>
    /// Gets whether derived attribute types are accepted.
    /// </summary>
    public bool useForChildren { get; }

    /// <summary>
    /// Gets the registration priority.
    /// </summary>
    public int priority { get; }
}
