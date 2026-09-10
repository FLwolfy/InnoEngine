using System;
using System.Reflection;
using Inno.Core.Serialization;

namespace Inno.Editor.Inspection;

/// <summary>
/// Carries the mutable presentation state of one attribute-annotated Inspector property.
/// </summary>
public sealed class InspectorAttributeDrawContext
{
    private Attribute m_attribute = null!;

    internal InspectorAttributeDrawContext(
        object owner,
        MemberInfo member,
        SerializedProperty? property,
        string path,
        string label,
        bool isReadOnly)
    {
        this.owner = owner ?? throw new ArgumentNullException(nameof(owner));
        this.member = member ?? throw new ArgumentNullException(nameof(member));
        this.property = property;
        this.path = string.IsNullOrWhiteSpace(path)
            ? throw new ArgumentException("The property path is required.", nameof(path))
            : path;
        this.label = label ?? throw new ArgumentNullException(nameof(label));
        this.isReadOnly = isReadOnly;
    }

    /// <summary>
    /// Gets the object that owns the annotated member.
    /// </summary>
    public object owner { get; }

    /// <summary>
    /// Gets the reflected field or property carrying the current attribute.
    /// </summary>
    public MemberInfo member { get; }

    /// <summary>
    /// Gets the root serialized property when one is available.
    /// </summary>
    public SerializedProperty? property { get; }

    /// <summary>
    /// Gets the stable Inspector control path.
    /// </summary>
    public string path { get; }

    /// <summary>
    /// Gets the attribute currently invoking a drawer.
    /// </summary>
    public Attribute attribute => m_attribute;

    /// <summary>
    /// Gets or sets whether the complete property row is visible.
    /// </summary>
    public bool isVisible { get; set; } = true;

    /// <summary>
    /// Gets or sets whether the property control is read-only in the Inspector.
    /// </summary>
    public bool isReadOnly { get; set; }

    /// <summary>
    /// Gets or sets the visible property label.
    /// </summary>
    public string label { get; set; }

    /// <summary>
    /// Gets or sets optional hover help for the property label.
    /// </summary>
    public string tooltip { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets an optional inclusive numeric lower bound.
    /// </summary>
    public double? minimum { get; set; }

    /// <summary>
    /// Gets or sets an optional inclusive numeric upper bound.
    /// </summary>
    public double? maximum { get; set; }

    /// <summary>
    /// Reads a sibling field or property from the current owner, including non-public base members.
    /// </summary>
    /// <param name="memberName">
    /// Exact CLR member name.
    /// </param>
    /// <returns>
    /// The current sibling value.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the sibling is absent, ambiguous, indexed, or unreadable.
    /// </exception>
    public object? GetSiblingValue(string memberName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(memberName);
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public |
                                   BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        for (Type? type = owner.GetType(); type is not null && type != typeof(object); type = type.BaseType)
        {
            FieldInfo? field = type.GetField(memberName, flags);
            if (field is not null)
                return field.GetValue(owner);
            PropertyInfo? candidate = type.GetProperty(memberName, flags);
            if (candidate is null)
                continue;
            if (candidate.GetIndexParameters().Length != 0 || candidate.GetGetMethod(true) is null)
            {
                throw new InvalidOperationException(
                    $"Inspector condition member '{type.FullName}.{memberName}' is not readable.");
            }
            return candidate.GetValue(owner);
        }
        throw new InvalidOperationException(
            $"Inspector condition member '{owner.GetType().FullName}.{memberName}' does not exist.");
    }

    internal void SelectAttribute(Attribute value)
        => m_attribute = value ?? throw new ArgumentNullException(nameof(value));
}
