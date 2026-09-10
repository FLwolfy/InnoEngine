using System;
using System.Collections;
using System.Globalization;
using System.Reflection;
using Inno.Adapter.Presentation.ImGui;
using Inno.Core.Serialization;
using Inno.Editor.Annotations;
using Inno.Editor.ImGui;
using Inno.Editor.ImGui.ImGuiWidget;
using Inno.Native.ImGui;
using NativeImGui = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.Inspection;

[InspectorAttributeDrawer(typeof(HeaderAttribute))]
internal sealed class HeaderInspectorAttributeDrawer : IInspectorAttributeDrawer
{
    /// <summary>
    /// Renders the section heading before its annotated property.
    /// </summary>
    /// <param name="context">
    /// Active attribute drawing context.
    /// </param>
    public void DrawBefore(InspectorAttributeDrawContext context)
    {
        var header = (HeaderAttribute)context.attribute;
        ImGuiWidget.SectionHeader(header.title, header.description);
    }
}

[InspectorAttributeDrawer(typeof(TextAttribute))]
internal sealed class TextInspectorAttributeDrawer : IInspectorAttributeDrawer
{
    /// <summary>
    /// Renders persistent explanatory text before its annotated property.
    /// </summary>
    /// <param name="context">
    /// Active attribute drawing context.
    /// </param>
    public void DrawBefore(InspectorAttributeDrawContext context)
    {
        string text = ((TextAttribute)context.attribute).text;
        NativeImGui.PushStyleColor(ImGuiCol.Text, EditorPalette.textDisabled);
        try
        {
            ImGuiWidget.WrappedText(text);
        }
        finally
        {
            NativeImGui.PopStyleColor();
        }
    }
}

[InspectorAttributeDrawer(typeof(SpaceAttribute))]
internal sealed class SpaceInspectorAttributeDrawer : IInspectorAttributeDrawer
{
    /// <summary>
    /// Renders the requested vertical spacing before its annotated property.
    /// </summary>
    /// <param name="context">
    /// Active attribute drawing context.
    /// </param>
    public void DrawBefore(InspectorAttributeDrawContext context)
    {
        float height = ((SpaceAttribute)context.attribute).height;
        NativeImGui.Dummy(new System.Numerics.Vector2(0f, height));
    }
}

[InspectorAttributeDrawer(typeof(TooltipAttribute))]
internal sealed class TooltipInspectorAttributeDrawer : IInspectorAttributeDrawer
{
    /// <summary>
    /// Applies tooltip text to the annotated property row.
    /// </summary>
    /// <param name="context">
    /// Active attribute drawing context.
    /// </param>
    public void Update(InspectorAttributeDrawContext context)
        => context.tooltip = ((TooltipAttribute)context.attribute).text;
}

[InspectorAttributeDrawer(typeof(InspectorNameAttribute))]
internal sealed class NameInspectorAttributeDrawer : IInspectorAttributeDrawer
{
    /// <summary>
    /// Replaces the default serialized member label.
    /// </summary>
    /// <param name="context">
    /// Active attribute drawing context.
    /// </param>
    public void Update(InspectorAttributeDrawContext context)
        => context.label = ((InspectorNameAttribute)context.attribute).name;
}

[InspectorAttributeDrawer(typeof(RangeAttribute))]
internal sealed class RangeInspectorAttributeDrawer : IInspectorAttributeDrawer
{
    /// <summary>
    /// Applies inclusive numeric slider bounds to the annotated property.
    /// </summary>
    /// <param name="context">
    /// Active attribute drawing context.
    /// </param>
    public void Update(InspectorAttributeDrawContext context)
    {
        var range = (RangeAttribute)context.attribute;
        context.minimum = range.minimum;
        context.maximum = range.maximum;
    }
}

[InspectorAttributeDrawer(typeof(InspectorReadOnlyAttribute))]
internal sealed class ReadOnlyInspectorAttributeDrawer : IInspectorAttributeDrawer
{
    /// <summary>
    /// Disables assignment for the annotated property.
    /// </summary>
    /// <param name="context">
    /// Active attribute drawing context.
    /// </param>
    public void Update(InspectorAttributeDrawContext context)
        => context.isReadOnly = true;
}

[InspectorAttributeDrawer(typeof(ShowIfAttribute))]
internal sealed class ShowIfInspectorAttributeDrawer : IInspectorAttributeDrawer
{
    /// <summary>
    /// Hides the annotated property unless its sibling condition succeeds.
    /// </summary>
    /// <param name="context">
    /// Active attribute drawing context.
    /// </param>
    public void Update(InspectorAttributeDrawContext context)
    {
        var condition = (ShowIfAttribute)context.attribute;
        context.isVisible &= InspectorConditionEvaluator.Evaluate(
            context.GetSiblingValue(condition.memberName),
            condition.condition,
            condition.expectedValue);
    }
}

[InspectorAttributeDrawer(typeof(HideIfAttribute))]
internal sealed class HideIfInspectorAttributeDrawer : IInspectorAttributeDrawer
{
    /// <summary>
    /// Hides the annotated property when its sibling condition succeeds.
    /// </summary>
    /// <param name="context">
    /// Active attribute drawing context.
    /// </param>
    public void Update(InspectorAttributeDrawContext context)
    {
        var condition = (HideIfAttribute)context.attribute;
        context.isVisible &= !InspectorConditionEvaluator.Evaluate(
            context.GetSiblingValue(condition.memberName),
            condition.condition,
            condition.expectedValue);
    }
}

[InspectorAttributeDrawer(typeof(HelpBoxAttribute))]
internal sealed class HelpBoxInspectorAttributeDrawer : IInspectorAttributeDrawer
{
    /// <summary>
    /// Renders a conditional diagnostic message before its annotated property.
    /// </summary>
    /// <param name="context">
    /// Active attribute drawing context.
    /// </param>
    public void DrawBefore(InspectorAttributeDrawContext context)
    {
        var help = (HelpBoxAttribute)context.attribute;
        if (!string.IsNullOrWhiteSpace(help.conditionMember)
            && !InspectorConditionEvaluator.Evaluate(
                context.GetSiblingValue(help.conditionMember),
                help.condition,
                help.expectedValue))
        {
            return;
        }
        System.Numerics.Vector4 color = help.messageType switch
        {
            InspectorMessageType.Warning => EditorPalette.warning,
            InspectorMessageType.Error => EditorPalette.error,
            _ => new System.Numerics.Vector4(0.42f, 0.66f, 0.88f, 1f)
        };
        string icon = help.messageType switch
        {
            InspectorMessageType.Warning => ImGuiIcon.TriangleExclamation,
            InspectorMessageType.Error => ImGuiIcon.CircleXmark,
            _ => ImGuiIcon.CircleInfo
        };
        ImGuiWidget.HelpBox(help.text, icon, color);
    }
}

internal static class InspectorConditionEvaluator
{
    internal static bool Evaluate(object? value, InspectorCondition condition, object? expected)
    {
        return condition switch
        {
            InspectorCondition.Truthy => IsTruthy(value),
            InspectorCondition.Falsy => !IsTruthy(value),
            InspectorCondition.Equal => AreEqual(value, expected),
            InspectorCondition.NotEqual => !AreEqual(value, expected),
            InspectorCondition.Null => value is null,
            InspectorCondition.NotNull => value is not null,
            InspectorCondition.Assigned => IsAssigned(value),
            InspectorCondition.NotAssigned => !IsAssigned(value),
            _ => throw new ArgumentOutOfRangeException(nameof(condition), condition, "Unknown Inspector condition.")
        };
    }

    private static bool IsTruthy(object? value)
    {
        return value switch
        {
            null => false,
            bool boolean => boolean,
            string text => !string.IsNullOrWhiteSpace(text),
            ICollection collection => collection.Count > 0,
            _ => true
        };
    }

    private static bool IsAssigned(object? value)
    {
        if (value is null)
            return false;
        PropertyInfo? property = value.GetType().GetProperty(
            "isAssigned",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        return property?.PropertyType == typeof(bool) && property.GetIndexParameters().Length == 0
            ? (bool)(property.GetValue(value) ?? false)
            : true;
    }

    private static bool AreEqual(object? value, object? expected)
    {
        if (Equals(value, expected))
            return true;
        if (value is null || expected is null)
            return false;
        Type valueType = value.GetType();
        try
        {
            object converted = valueType.IsEnum
                ? expected is string name
                    ? Enum.Parse(valueType, name, ignoreCase: false)
                    : Enum.ToObject(valueType, expected)
                : Convert.ChangeType(expected, valueType, CultureInfo.InvariantCulture);
            return Equals(value, converted);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidCastException or FormatException or OverflowException)
        {
            return false;
        }
    }
}
