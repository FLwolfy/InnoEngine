using System;
using System.Runtime.CompilerServices;

using Inno.Core.Serialization;
using Inno.Core.Scripting;
using Inno.Core.Settings;

namespace Inno.Editor.Settings;

/// <summary>
/// Describes one Editor presentation for a strongly typed runtime project setting.
/// </summary>
public abstract class ProjectSettingEditor
{
    private string? m_label;
    private int m_order;
    private string? m_pagePath;
    private string? m_path;

    /// <summary>Gets the stable runtime project setting protocol edited by this presentation.</summary>
    public abstract ProjectSettingId settingId { get; }

    /// <summary>Gets the complete slash-delimited placement path.</summary>
    public string path => m_path ?? string.Empty;

    /// <summary>Gets the page that owns this field.</summary>
    public string pagePath => m_pagePath ?? string.Empty;

    /// <summary>Gets the display label derived from the final path segment.</summary>
    public string label => m_label ?? string.Empty;

    /// <summary>Gets the stable order among fields in the same section.</summary>
    public int order => m_order;

    /// <summary>Gets the section heading used to group this field.</summary>
    public virtual string section => string.Empty;

    /// <summary>Gets the explanation displayed by the unified Settings frontend.</summary>
    public virtual string description => string.Empty;

    /// <summary>Draws one isolated staged value through this presentation.</summary>
    /// <param name="value">The exact setting type owned by this presentation.</param>
    /// <returns><see langword="true"/> when the value differs from its first drawn state.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="value"/> has another runtime type.</exception>
    [ScriptingApiIgnore]
    public bool Draw(ISerializable value)
        => DrawValue(value);

    /// <summary>Compares two exact-type values through their native serialized property data.</summary>
    /// <param name="left">The first setting value.</param>
    /// <param name="right">The second setting value.</param>
    /// <returns><see langword="true"/> when both values contain equal serialized properties.</returns>
    /// <exception cref="ArgumentException">Thrown when either value has another runtime type.</exception>
    [ScriptingApiIgnore]
    public bool ValuesEqual(ISerializable left, ISerializable right)
        => ValueEquals(left, right);

    internal abstract Type valueType { get; }

    internal abstract bool DrawValue(ISerializable value);

    internal abstract bool ValueEquals(ISerializable left, ISerializable right);

    internal void BindPlacement(string placementPath, int placementOrder)
    {
        string normalized = EditorSettingsPathUtility.Normalize(placementPath);
        if (m_path is not null &&
            (!string.Equals(m_path, normalized, StringComparison.Ordinal) || m_order != placementOrder))
        {
            throw new InvalidOperationException(
                $"Project setting editor '{GetType().FullName}' was registered at more than one path.");
        }
        if (!settingId.isValid)
        {
            throw new InvalidOperationException(
                $"Project setting editor '{GetType().FullName}' returned an invalid setting ID.");
        }
        (string path, string parentPath, string label) = EditorSettingsPathUtility.Parse(normalized);
        if (parentPath.Length == 0)
            throw new InvalidOperationException($"Project setting editor '{path}' requires a parent page.");
        m_path = path;
        m_pagePath = parentPath;
        m_label = label;
        m_order = placementOrder;
    }

    internal void ValidateValue(ISerializable value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.GetType() != valueType)
        {
            throw new ArgumentException(
                $"Project setting '{settingId}' requires '{valueType.FullName}', not '{value.GetType().FullName}'.",
                nameof(value));
        }
    }
}

/// <summary>
/// Provides a typed drawing extension for one runtime project setting protocol.
/// </summary>
/// <typeparam name="TSetting">The exact serializable setting type registered by the runtime Plugin or host.</typeparam>
public abstract class ProjectSettingEditor<TSetting> : ProjectSettingEditor
    where TSetting : class, ISerializable
{
    private readonly ConditionalWeakTable<TSetting, Baseline> m_baselines = new();

    /// <summary>
    /// Draws the isolated staged value. Mutations remain local until the Settings frontend applies them.
    /// </summary>
    /// <param name="setting">The isolated current-generation setting snapshot.</param>
    protected abstract void OnDraw(TSetting setting);

    internal sealed override Type valueType => typeof(TSetting);

    internal sealed override bool DrawValue(ISerializable value)
    {
        ValidateValue(value);
        var setting = (TSetting)value;
        Baseline baseline = m_baselines.GetValue(
            setting,
            static candidate => new Baseline(SerializationManager.CapturePropertiesData(candidate)));
        OnDraw(setting);
        return !baseline.propertyData.AsSpan().SequenceEqual(
            SerializationManager.CapturePropertiesData(setting));
    }

    internal sealed override bool ValueEquals(ISerializable left, ISerializable right)
    {
        ValidateValue(left);
        ValidateValue(right);
        return SerializationManager.CapturePropertiesData(left).AsSpan().SequenceEqual(
            SerializationManager.CapturePropertiesData(right));
    }

    private sealed record Baseline(byte[] propertyData);
}
