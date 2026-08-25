using System;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Inno.Editor.Settings;

/// <summary>
/// Defines one path-addressed Settings page or one custom-drawn Settings field.
/// </summary>
public abstract class EditorSetting
{
    private readonly ConditionalWeakTable<EditorSettingObject, EditorSettingObject> m_drawBaselines = new();
    private EditorSettingObject? m_boundDefaultValue;
    private string? m_label;
    private int m_order;
    private string? m_pagePath;
    private string? m_path;

    /// <summary>
    /// Creates a Settings definition. A field supplies its persisted default through
    /// <see cref="defaultValue"/>; a page keeps the base implementation.
    /// </summary>
    protected EditorSetting()
    {
    }

    /// <summary>
    /// Gets the complete slash-delimited identity and placement path.
    /// </summary>
    public string path => m_path ?? string.Empty;

    /// <summary>
    /// Gets the page that owns this field, or this page's own path for a page definition.
    /// </summary>
    public string pagePath => m_pagePath ?? string.Empty;

    /// <summary>
    /// Gets the display label derived from the final path segment.
    /// </summary>
    public string label => m_label ?? string.Empty;

    /// <summary>
    /// Gets the stable order among fields with the same section and label.
    /// </summary>
    public int order => m_order;

    /// <summary>
    /// Gets whether this definition owns one persisted JSON object.
    /// </summary>
    public bool hasValue => m_boundDefaultValue is not null;

    /// <summary>
    /// Creates the default object for a field. Page definitions keep the base implementation,
    /// whose internal value is <see langword="null"/>.
    /// </summary>
    /// <remarks>
    /// Field implementations must return a newly owned object on every access.
    /// </remarks>
    public virtual EditorSettingObject defaultValue => null!;

    /// <summary>
    /// Gets the section heading used to group fields alphabetically. Definitions without a
    /// section keep the base implementation, whose internal value is <see langword="null"/>.
    /// </summary>
    public virtual string section => null!;

    /// <summary>
    /// Gets the page or field explanation displayed by the Settings frontend.
    /// </summary>
    public virtual string description => string.Empty;

    /// <summary>
    /// Draws this field inside the frontend-managed content container.
    /// </summary>
    /// <param name="setting">The isolated mutable object being edited.</param>
    /// <returns>
    /// <see langword="true"/> when the staged object differs from the value it held when this
    /// frontend first drew that object.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="setting"/> is <see langword="null"/>.
    /// </exception>
    public bool Draw(EditorSettingObject setting)
    {
        ArgumentNullException.ThrowIfNull(setting);
        EditorSettingObject baseline = m_drawBaselines.GetValue(
            setting,
            static value => value.Copy());
        OnDraw(setting);
        return !setting.ValueEquals(baseline);
    }

    /// <summary>
    /// Determines whether a staged field object equals this definition's bound default value.
    /// </summary>
    /// <param name="setting">The isolated staged object to compare.</param>
    /// <returns>
    /// <see langword="true"/> when every stored property equals the field default.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="setting"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when this definition describes a page rather than a field.
    /// </exception>
    public bool IsDefault(EditorSettingObject setting)
    {
        ArgumentNullException.ThrowIfNull(setting);
        EditorSettingObject boundDefault = m_boundDefaultValue
            ?? throw new InvalidOperationException(
                $"Settings page '{path}' does not own a default value.");
        return boundDefault.ValueEquals(setting);
    }

    /// <summary>
    /// Draws the field using the staged JSON object owned by the Settings frontend.
    /// A type that does not override this method describes a page.
    /// </summary>
    /// <param name="setting">The isolated mutable object being edited.</param>
    protected virtual void OnDraw(EditorSettingObject setting)
    {
    }

    internal bool isPageDefinition { get; private set; }

    internal EditorSettingObject CreateDefault()
        => m_boundDefaultValue?.Copy()
           ?? throw new InvalidOperationException($"Settings page '{path}' does not own a value.");

    internal void BindPlacement(string placementPath, int placementOrder)
    {
        string normalized = EditorSettingsPathUtility.Normalize(placementPath);
        if (m_path is not null &&
            (!string.Equals(m_path, normalized, StringComparison.Ordinal) || m_order != placementOrder))
        {
            throw new InvalidOperationException(
                $"Settings definition '{GetType().FullName}' was registered at more than one path.");
        }

        MethodInfo? drawMethod = GetType().GetMethod(
            nameof(OnDraw),
            BindingFlags.Instance | BindingFlags.NonPublic);
        isPageDefinition = drawMethod?.DeclaringType == typeof(EditorSetting);
        EditorSettingObject? declaredDefault = defaultValue;
        (string path, string parentPath, string label) = EditorSettingsPathUtility.Parse(normalized);
        if (isPageDefinition == (declaredDefault is not null))
        {
            throw new InvalidOperationException(
                isPageDefinition
                    ? $"Settings page '{path}' cannot declare a persisted default value."
                    : $"Settings field '{path}' must declare a persisted default value.");
        }
        if (!isPageDefinition && parentPath.Length == 0)
        {
            throw new InvalidOperationException(
                $"Settings field '{path}' requires at least one parent page segment.");
        }
        m_path = path;
        m_pagePath = isPageDefinition ? path : parentPath;
        m_label = label;
        m_order = placementOrder;
        m_boundDefaultValue = declaredDefault?.Copy();
    }
}
