using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using Inno.Core.Logging;
using Inno.Scripting.Api;
using Inno.Core.Serialization;
using Inno.Editor.Core;
using Inno.Editor.ImGui;
using Inno.Editor.ImGui.ImGuiWidget;
using EditorWidget = Inno.Editor.ImGui.ImGuiWidget.ImGuiWidget;
using Inno.Editor.Interactions;
using NativeImGui = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.Inspection;

/// <summary>
/// Resolves drawers and renders serialized property paths with isolated error handling.
/// </summary>
public sealed class SerializedPropertyRenderer
{
    private readonly ConditionalWeakTable<object, Dictionary<string, string>> m_failureStates = new();
    private readonly ConditionalWeakTable<object, Dictionary<string, string>> m_textStates = new();
    private readonly PropertyDrawerRegistry m_drawers;
    private readonly InspectorAttributeDrawerRegistry m_attributes;
    private readonly EditorInteractions m_interactions;
    private readonly IInspectionPropertyEditService m_edits;
    private readonly Logger m_logger;

    /// <summary>
    /// Creates a serialized property renderer over one drawer registry and feature-owned edit service.
    /// </summary>
    /// <param name="drawers">
    /// The property drawer registry used for runtime type resolution.
    /// </param>
    /// <param name="attributes">
    /// The generation-aware registry that interprets Inspector presentation attributes.
    /// </param>
    /// <param name="interactions">
    /// The active editor interaction entry point.
    /// </param>
    /// <param name="edits">
    /// The feature-owned service used to apply and record property changes.
    /// </param>
    /// <param name="logs">
    /// The host-owned log router used to report isolated drawer failures.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="drawers"/>, <paramref name="interactions"/>, or
    /// <paramref name="edits"/> is <see langword="null"/>.
    /// </exception>
    [ScriptingApiIgnore]
    public SerializedPropertyRenderer(
        PropertyDrawerRegistry drawers,
        InspectorAttributeDrawerRegistry attributes,
        EditorInteractions interactions,
        IInspectionPropertyEditService edits,
        LogRouter logs)
    {
        m_drawers = drawers ?? throw new ArgumentNullException(nameof(drawers));
        m_attributes = attributes ?? throw new ArgumentNullException(nameof(attributes));
        m_interactions = interactions ?? throw new ArgumentNullException(nameof(interactions));
        m_edits = edits ?? throw new ArgumentNullException(nameof(edits));
        ArgumentNullException.ThrowIfNull(logs);
        m_logger = logs.CreateLogger<SerializedPropertyRenderer>();
    }

    /// <summary>
    /// Draws a root serialized property.
    /// </summary>
    /// <param name="editorContext">
    /// Shared editor context.
    /// </param>
    /// <param name="owner">
    /// The live domain object that owns the root property.
    /// </param>
    /// <param name="ownerPath">
    /// Stable owner path.
    /// </param>
    /// <param name="property">
    /// Serialized property.
    /// </param>
    public void Draw(
        EditorContext editorContext,
        object owner,
        string ownerPath,
        SerializedProperty property)
    {
        ArgumentNullException.ThrowIfNull(editorContext);
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(property);
        Draw(
            editorContext,
            owner,
            owner,
            InspectorMemberMetadata.Resolve(owner.GetType(), property.name),
            property,
            property.name,
            $"{ownerPath}.{property.name}",
            property.name,
            property.propertyType,
            property.visibility,
            property.GetValue,
            property.SetValue);
    }

    /// <summary>Draws a declared authoring value with existing property drawers and a feature-owned draft edit service.</summary>
    /// <param name="editorContext">Current Editor frame.</param>
    /// <param name="owner">Stable owner for text state and edit routing; not necessarily a canonical serialized object.</param>
    /// <param name="path">Stable unique property path.</param>
    /// <param name="label">User-facing label.</param>
    /// <param name="propertyType">Declared value type used to select its registered drawer.</param>
    /// <param name="getter">Reads the detached value.</param>
    /// <param name="setter">Changes the detached value within the supplied edit service.</param>
    /// <param name="edits">Feature-owned transaction boundary, propagated to nested properties.</param>
    /// <param name="readOnly">Whether assignment is disabled.</param>
    /// <param name="hdrColor">Whether Color values use unclamped linear floating-point channels.</param>
    /// <param name="tooltip">Optional authoring description rendered by the shared property tooltip.</param>
    /// <param name="minimum">Optional inclusive numeric editing bound.</param>
    /// <param name="maximum">Optional inclusive numeric editing bound.</param>
    [ScriptingApiIgnore]
    public void DrawValue(EditorContext editorContext, object owner, string path, string label, Type propertyType,
        Func<object?> getter, Action<object?> setter, IInspectionPropertyEditService edits, bool readOnly = false, bool hdrColor = false,
        string? tooltip = null, double? minimum = null, double? maximum = null)
    {
        ArgumentNullException.ThrowIfNull(edits);
        Draw(editorContext, owner, owner, null, null, path, path, label, propertyType,
            readOnly ? PropertyVisibility.Readonly : PropertyVisibility.Show, getter, setter, edits, hdrColor, tooltip, minimum, maximum);
    }

    /// <summary>Draws a detached serialized property with its attributes and a feature-owned draft transaction.</summary>
    /// <param name="editorContext">Current Editor frame.</param>
    /// <param name="stateOwner">Stable host-owned presentation state, containing no collectible objects.</param>
    /// <param name="valueOwner">Invocation-local detached value declaring the property and attributes.</param>
    /// <param name="ownerPath">Stable document or settings path.</param>
    /// <param name="property">Detached native serialized property.</param>
    /// <param name="edits">Draft mutation and shared History boundary.</param>
    /// <param name="readOnly">Whether source ownership prohibits edits.</param>
    [ScriptingApiIgnore]
    public void DrawDraftProperty(EditorContext editorContext, object stateOwner, object valueOwner, string ownerPath,
        SerializedProperty property, IInspectionPropertyEditService edits, bool readOnly = false)
    {
        ArgumentNullException.ThrowIfNull(valueOwner);
        ArgumentNullException.ThrowIfNull(stateOwner);
        ArgumentNullException.ThrowIfNull(property);
        ArgumentNullException.ThrowIfNull(edits);
        Draw(editorContext, stateOwner, valueOwner, InspectorMemberMetadata.Resolve(valueOwner.GetType(), property.name),
            property, property.name, ownerPath + "." + property.name, property.name, property.propertyType,
            readOnly ? property.visibility & ~PropertyVisibility.RuntimeSet : property.visibility,
            property.GetValue, property.SetValue, edits);
    }

    internal void Draw(
        EditorContext editorContext,
        object owner,
        object metadataOwner,
        MemberInfo? member,
        SerializedProperty? property,
        string rootPropertyName,
        string path,
        string label,
        Type propertyType,
        PropertyVisibility visibility,
        Func<object?> getter,
        Action<object?> setter,
        IInspectionPropertyEditService? edits = null, bool hdrColor = false,
        string? tooltip = null, double? minimum = null, double? maximum = null)
    {
        string displayLabel = EditorWidget.NicifyName(label);
        bool isReadOnly = (visibility & PropertyVisibility.RuntimeSet) == 0;
        Attribute[] attributes = member is null ? [] : InspectorMemberMetadata.GetAttributes(member);
        InspectorAttributeDrawContext? attributeContext = null;
        if (member is not null && attributes.Length > 0)
        {
            attributeContext = new InspectorAttributeDrawContext(
                metadataOwner,
                member,
                property,
                path,
                displayLabel,
                isReadOnly);
            m_attributes.Update(attributeContext, attributes);
            if (!attributeContext.isVisible)
                return;
            displayLabel = attributeContext.label;
            isReadOnly = attributeContext.isReadOnly;
            m_attributes.DrawBefore(attributeContext, attributes);
        }

        var context = new PropertyDrawContext(
            editorContext,
            m_interactions,
            edits ?? m_edits,
            owner,
            rootPropertyName,
            path,
            displayLabel,
            propertyType,
            visibility,
            isReadOnly,
            attributeContext?.minimum ?? minimum,
            attributeContext?.maximum ?? maximum,
            getter,
            setter,
            this, hdrColor);

        EditorWidget.PropertyRow(
            path,
            context.label,
            () => DrawContent(context),
            tooltip: attributeContext?.tooltip ?? tooltip);
        if (attributeContext is not null)
            m_attributes.DrawAfter(attributeContext, attributes);
    }

    internal void DrawInline(
        EditorContext editorContext,
        object owner,
        object metadataOwner,
        MemberInfo? member,
        string rootPropertyName,
        string path,
        string label,
        Type propertyType,
        PropertyVisibility visibility,
        Func<object?> getter,
        Action<object?> setter,
        IInspectionPropertyEditService? edits = null)
    {
        var context = new PropertyDrawContext(
            editorContext,
            m_interactions,
            edits ?? m_edits,
            owner,
            rootPropertyName,
            path,
            EditorWidget.NicifyName(label),
            propertyType,
            visibility,
            (visibility & PropertyVisibility.RuntimeSet) == 0,
            null,
            null,
            getter,
            setter,
            this);
        DrawContent(context);
    }

    private void DrawContent(PropertyDrawContext context)
    {
        Dictionary<string, string> failureStates = m_failureStates.GetOrCreateValue(context.owner);
        try
        {
            IPropertyDrawer drawer = m_drawers.Resolve(context.propertyType);
            EditorWidget.Disabled(context.isReadOnly, () => drawer.Draw(context));
            failureStates.Remove(context.path);
        }
        catch (Exception exception)
        {
            EditorWidget.ColoredText(EditorPalette.error, $"Error: {exception.Message}");
            string failureState = $"{exception.GetType().FullName}|{exception.Message}";
            if (!failureStates.TryGetValue(context.path, out string? previous) ||
                !string.Equals(previous, failureState, StringComparison.Ordinal))
            {
                m_logger.Write(
                    LogLevel.Error,
                    "Inspector failed to draw property '{0}': {1}",
                    [context.path, exception]);
                failureStates[context.path] = failureState;
            }
        }
    }

    internal bool TryGetTextState(object owner, string path, string key, out string? value)
    {
        ArgumentNullException.ThrowIfNull(owner);
        value = null;
        return m_textStates.TryGetValue(owner, out Dictionary<string, string>? states)
            && states.TryGetValue(CreateStateKey(path, key), out value);
    }

    internal void SetTextState(object owner, string path, string key, string value)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(value);
        m_textStates.GetOrCreateValue(owner)[CreateStateKey(path, key)] = value;
    }

    internal void ClearTextState(object owner, string path, string key)
    {
        ArgumentNullException.ThrowIfNull(owner);
        if (m_textStates.TryGetValue(owner, out Dictionary<string, string>? states))
            states.Remove(CreateStateKey(path, key));
    }

    private static string CreateStateKey(string path, string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return string.Concat(path, "\n", key);
    }
}
