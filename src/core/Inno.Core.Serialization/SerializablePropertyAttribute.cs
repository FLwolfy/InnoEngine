using System;

namespace Inno.Core.Serialization;

/// <summary>
/// Declares a field or property as participating in the <see cref="ISerializable"/> state graph.
/// </summary>
/// <remarks>
/// The default visibility is <see cref=".PropertyVisibility.Show"/>.
/// </remarks>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public sealed class SerializablePropertyAttribute : Attribute
{
    /// <summary>
    /// Gets the visibility of the annotated member.
    /// </summary>
    public PropertyVisibility propertyVisibility { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="SerializablePropertyAttribute"/> class.
    /// </summary>
    /// <param name="visibility">The member visibility in the serialization graph.</param>
    public SerializablePropertyAttribute(PropertyVisibility visibility = PropertyVisibility.Show)
    {
        propertyVisibility = visibility;
    }
}
