using System;
using System.Diagnostics;

namespace Inno.Editor.Annotations;

/// <summary>
/// Provides editor-only presentation metadata for one serialized field or property.
/// </summary>
/// <remarks>
/// Presentation attributes never participate in serialization. Concrete attributes are conditional on
/// INNO_EDITOR, so their applications are omitted from Player script assemblies.
/// </remarks>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = true, Inherited = true)]
[Conditional("INNO_EDITOR")]
public abstract class InspectorPresentationAttribute : Attribute
{
    /// <summary>
    /// Gets or sets the relative order among presentation attributes declared on the same member.
    /// </summary>
    public int order { get; set; }
}

/// <summary>
/// Starts a visually separated Inspector section before the annotated property.
/// </summary>
[Conditional("INNO_EDITOR")]
public sealed class HeaderAttribute : InspectorPresentationAttribute
{
    /// <summary>
    /// Creates a section header with optional explanatory text.
    /// </summary>
    /// <param name="title">
    /// Visible section title.
    /// </param>
    /// <param name="description">
    /// Optional explanatory text displayed when the title is hovered.
    /// </param>
    public HeaderAttribute(string title, string description = "")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        this.title = title;
        this.description = description ?? string.Empty;
    }

    /// <summary>
    /// Gets the visible section title.
    /// </summary>
    public string title { get; }

    /// <summary>
    /// Gets the optional explanatory text.
    /// </summary>
    public string description { get; }
}

/// <summary>
/// Draws persistent wrapped explanatory text before the annotated Inspector property.
/// </summary>
[Conditional("INNO_EDITOR")]
public sealed class TextAttribute : InspectorPresentationAttribute
{
    /// <summary>
    /// Creates persistent Inspector text.
    /// </summary>
    /// <param name="text">
    /// Non-empty text displayed in the Inspector layout.
    /// </param>
    public TextAttribute(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        this.text = text;
    }

    /// <summary>
    /// Gets the persistent Inspector text.
    /// </summary>
    public string text { get; }
}

/// <summary>
/// Inserts vertical spacing before the annotated Inspector property.
/// </summary>
[Conditional("INNO_EDITOR")]
public sealed class SpaceAttribute : InspectorPresentationAttribute
{
    /// <summary>
    /// Creates a spacing decorator.
    /// </summary>
    /// <param name="height">
    /// Non-negative logical pixel height.
    /// </param>
    public SpaceAttribute(float height = 8f)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(height);
        this.height = height;
    }

    /// <summary>
    /// Gets the logical pixel height.
    /// </summary>
    public float height { get; }
}

/// <summary>
/// Provides hover help for one Inspector property label.
/// </summary>
[Conditional("INNO_EDITOR")]
public sealed class TooltipAttribute : InspectorPresentationAttribute
{
    /// <summary>
    /// Creates property hover help.
    /// </summary>
    /// <param name="text">
    /// Non-empty tooltip text.
    /// </param>
    public TooltipAttribute(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        this.text = text;
    }

    /// <summary>
    /// Gets the tooltip text.
    /// </summary>
    public string text { get; }
}

/// <summary>
/// Replaces the generated Inspector label for one property.
/// </summary>
[Conditional("INNO_EDITOR")]
public sealed class InspectorNameAttribute : InspectorPresentationAttribute
{
    /// <summary>
    /// Creates a label override.
    /// </summary>
    /// <param name="name">
    /// Non-empty visible label.
    /// </param>
    public InspectorNameAttribute(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        this.name = name;
    }

    /// <summary>
    /// Gets the visible label.
    /// </summary>
    public string name { get; }
}

/// <summary>
/// Constrains a numeric Inspector control to an inclusive range.
/// </summary>
[Conditional("INNO_EDITOR")]
public sealed class RangeAttribute : InspectorPresentationAttribute
{
    /// <summary>
    /// Creates an inclusive numeric range.
    /// </summary>
    /// <param name="minimum">
    /// Inclusive lower bound.
    /// </param>
    /// <param name="maximum">
    /// Inclusive upper bound.
    /// </param>
    public RangeAttribute(double minimum, double maximum)
    {
        if (!double.IsFinite(minimum))
            throw new ArgumentOutOfRangeException(nameof(minimum));
        if (!double.IsFinite(maximum) || maximum < minimum)
            throw new ArgumentOutOfRangeException(nameof(maximum));
        this.minimum = minimum;
        this.maximum = maximum;
    }

    /// <summary>
    /// Gets the inclusive lower bound.
    /// </summary>
    public double minimum { get; }

    /// <summary>
    /// Gets the inclusive upper bound.
    /// </summary>
    public double maximum { get; }
}

/// <summary>
/// Makes an otherwise writable serialized property read-only in the Inspector.
/// </summary>
[Conditional("INNO_EDITOR")]
public sealed class InspectorReadOnlyAttribute : InspectorPresentationAttribute
{
}

/// <summary>
/// Defines the comparison used by conditional Inspector presentation.
/// </summary>
public enum InspectorCondition
{
    /// <summary>
    /// Shows a value when it evaluates as true or non-empty.
    /// </summary>
    Truthy,

    /// <summary>
    /// Shows a value when it evaluates as false, null, or empty.
    /// </summary>
    Falsy,

    /// <summary>
    /// Compares the value with the supplied expected constant.
    /// </summary>
    Equal,

    /// <summary>
    /// Requires the value to differ from the supplied expected constant.
    /// </summary>
    NotEqual,

    /// <summary>
    /// Requires a null value.
    /// </summary>
    Null,

    /// <summary>
    /// Requires a non-null value.
    /// </summary>
    NotNull,

    /// <summary>
    /// Requires a reference-like value to expose an assigned resource.
    /// </summary>
    Assigned,

    /// <summary>
    /// Requires a reference-like value not to expose an assigned resource.
    /// </summary>
    NotAssigned
}

/// <summary>
/// Shows the annotated Inspector property only when a sibling member satisfies a condition.
/// </summary>
[Conditional("INNO_EDITOR")]
public sealed class ShowIfAttribute : InspectorPresentationAttribute
{
    /// <summary>
    /// Creates a truthiness condition.
    /// </summary>
    /// <param name="memberName">
    /// Sibling field or property name.
    /// </param>
    public ShowIfAttribute(string memberName)
        : this(memberName, InspectorCondition.Truthy, null)
    {
    }

    /// <summary>
    /// Creates an equality condition.
    /// </summary>
    /// <param name="memberName">
    /// Sibling field or property name.
    /// </param>
    /// <param name="expectedValue">
    /// Compile-time value required for visibility.
    /// </param>
    public ShowIfAttribute(string memberName, object? expectedValue)
        : this(memberName, InspectorCondition.Equal, expectedValue)
    {
    }

    /// <summary>
    /// Creates a named condition that does not require an expected value.
    /// </summary>
    /// <param name="memberName">
    /// Sibling field or property name.
    /// </param>
    /// <param name="condition">
    /// Comparison applied to the sibling value.
    /// </param>
    public ShowIfAttribute(string memberName, InspectorCondition condition)
        : this(memberName, condition, null)
    {
    }

    private ShowIfAttribute(string memberName, InspectorCondition condition, object? expectedValue)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(memberName);
        this.memberName = memberName;
        this.condition = condition;
        this.expectedValue = expectedValue;
    }

    /// <summary>
    /// Gets the sibling member name.
    /// </summary>
    public string memberName { get; }

    /// <summary>
    /// Gets the comparison.
    /// </summary>
    public InspectorCondition condition { get; }

    /// <summary>
    /// Gets the optional expected constant.
    /// </summary>
    public object? expectedValue { get; }
}

/// <summary>
/// Hides the annotated Inspector property when a sibling member satisfies a condition.
/// </summary>
[Conditional("INNO_EDITOR")]
public sealed class HideIfAttribute : InspectorPresentationAttribute
{
    /// <summary>
    /// Creates a truthiness condition.
    /// </summary>
    /// <param name="memberName">
    /// Sibling field or property name.
    /// </param>
    public HideIfAttribute(string memberName)
        : this(memberName, InspectorCondition.Truthy, null)
    {
    }

    /// <summary>
    /// Creates an equality condition.
    /// </summary>
    /// <param name="memberName">
    /// Sibling field or property name.
    /// </param>
    /// <param name="expectedValue">
    /// Compile-time value that hides the property.
    /// </param>
    public HideIfAttribute(string memberName, object? expectedValue)
        : this(memberName, InspectorCondition.Equal, expectedValue)
    {
    }

    /// <summary>
    /// Creates a named condition that does not require an expected value.
    /// </summary>
    /// <param name="memberName">
    /// Sibling field or property name.
    /// </param>
    /// <param name="condition">
    /// Comparison applied to the sibling value.
    /// </param>
    public HideIfAttribute(string memberName, InspectorCondition condition)
        : this(memberName, condition, null)
    {
    }

    private HideIfAttribute(string memberName, InspectorCondition condition, object? expectedValue)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(memberName);
        this.memberName = memberName;
        this.condition = condition;
        this.expectedValue = expectedValue;
    }

    /// <summary>
    /// Gets the sibling member name.
    /// </summary>
    public string memberName { get; }

    /// <summary>
    /// Gets the comparison.
    /// </summary>
    public InspectorCondition condition { get; }

    /// <summary>
    /// Gets the optional expected constant.
    /// </summary>
    public object? expectedValue { get; }
}

/// <summary>
/// Selects the visual severity of an Inspector help message.
/// </summary>
public enum InspectorMessageType
{
    /// <summary>
    /// Neutral explanatory information.
    /// </summary>
    Info,

    /// <summary>
    /// A non-blocking configuration warning.
    /// </summary>
    Warning,

    /// <summary>
    /// An invalid or unusable configuration.
    /// </summary>
    Error
}

/// <summary>
/// Draws a wrapped explanatory message before the annotated Inspector property.
/// </summary>
[Conditional("INNO_EDITOR")]
public sealed class HelpBoxAttribute : InspectorPresentationAttribute
{
    /// <summary>
    /// Creates an Inspector help message.
    /// </summary>
    /// <param name="text">
    /// Non-empty message text.
    /// </param>
    /// <param name="messageType">
    /// Visual severity.
    /// </param>
    public HelpBoxAttribute(string text, InspectorMessageType messageType = InspectorMessageType.Info)
        : this(text, messageType, string.Empty, InspectorCondition.Truthy, null)
    {
    }

    /// <summary>
    /// Creates an Inspector help message shown when a sibling satisfies a named condition.
    /// </summary>
    /// <param name="text">
    /// Non-empty message text.
    /// </param>
    /// <param name="conditionMember">
    /// Sibling field or property name.
    /// </param>
    /// <param name="condition">
    /// Comparison applied to the sibling value.
    /// </param>
    /// <param name="messageType">
    /// Visual severity.
    /// </param>
    public HelpBoxAttribute(
        string text,
        string conditionMember,
        InspectorCondition condition,
        InspectorMessageType messageType = InspectorMessageType.Info)
        : this(text, messageType, conditionMember, condition, null)
    {
    }

    /// <summary>
    /// Creates an Inspector help message shown when a sibling equals a compile-time value.
    /// </summary>
    /// <param name="text">
    /// Non-empty message text.
    /// </param>
    /// <param name="conditionMember">
    /// Sibling field or property name.
    /// </param>
    /// <param name="expectedValue">
    /// Compile-time value required to show the message.
    /// </param>
    /// <param name="messageType">
    /// Visual severity.
    /// </param>
    public HelpBoxAttribute(
        string text,
        string conditionMember,
        object? expectedValue,
        InspectorMessageType messageType = InspectorMessageType.Info)
        : this(text, messageType, conditionMember, InspectorCondition.Equal, expectedValue)
    {
    }

    private HelpBoxAttribute(
        string text,
        InspectorMessageType messageType,
        string conditionMember,
        InspectorCondition condition,
        object? expectedValue)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        this.text = text;
        this.messageType = messageType;
        this.conditionMember = conditionMember ?? string.Empty;
        this.condition = condition;
        this.expectedValue = expectedValue;
    }

    /// <summary>
    /// Gets the message text.
    /// </summary>
    public string text { get; }

    /// <summary>
    /// Gets the visual severity.
    /// </summary>
    public InspectorMessageType messageType { get; }

    /// <summary>
    /// Gets the optional sibling member controlling message visibility.
    /// </summary>
    public string conditionMember { get; }

    /// <summary>
    /// Gets the optional sibling comparison.
    /// </summary>
    public InspectorCondition condition { get; }

    /// <summary>
    /// Gets the optional expected constant.
    /// </summary>
    public object? expectedValue { get; }
}
