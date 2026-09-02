using System;
using System.Collections.Generic;
using System.IO;

using Inno.Core.Serialization;

namespace Inno.Editor.Settings;

/// <summary>
/// Represents one isolated, strongly typed object exposed to a Settings field and its consumers.
/// </summary>
[GenerateSerializationConverter]
public sealed class EditorSettingObject : ISerializable
{
    [SerializableProperty]
    internal Dictionary<string, EditorSettingValue> m_values = new(StringComparer.Ordinal);

    /// <summary>
    /// Creates an empty Settings object.
    /// </summary>
    public EditorSettingObject()
    {
    }

    private EditorSettingObject(Dictionary<string, EditorSettingValue> values)
    {
        m_values = values;
    }

    /// <summary>
    /// Gets a Boolean property, or a fallback when the property is absent.
    /// </summary>
    /// <param name="name">
    /// The stable property name.
    /// </param>
    /// <param name="defaultValue">
    /// The value returned when the property is absent.
    /// </param>
    /// <returns>
    /// The stored Boolean or <paramref name="defaultValue"/>.
    /// </returns>
    public bool GetAsBoolean(string name, bool defaultValue = default)
        => Get(name, defaultValue);

    /// <summary>
    /// Sets a Boolean property.
    /// </summary>
    /// <param name="name">
    /// The stable property name.
    /// </param>
    /// <param name="value">
    /// The value to store.
    /// </param>
    public void SetAsBoolean(string name, bool value)
        => Set(name, value);

    /// <summary>
    /// Gets a 32-bit signed integer property, or a fallback when the property is absent.
    /// </summary>
    /// <param name="name">
    /// The stable property name.
    /// </param>
    /// <param name="defaultValue">
    /// The value returned when the property is absent.
    /// </param>
    /// <returns>
    /// The stored integer or <paramref name="defaultValue"/>.
    /// </returns>
    public int GetAsInt32(string name, int defaultValue = default)
        => Get(name, defaultValue);

    /// <summary>
    /// Sets a 32-bit signed integer property.
    /// </summary>
    /// <param name="name">
    /// The stable property name.
    /// </param>
    /// <param name="value">
    /// The value to store.
    /// </param>
    public void SetAsInt32(string name, int value)
        => Set(name, value);

    /// <summary>
    /// Gets a 32-bit unsigned integer property, or a fallback when the property is absent.
    /// </summary>
    /// <param name="name">
    /// The stable property name.
    /// </param>
    /// <param name="defaultValue">
    /// The value returned when the property is absent.
    /// </param>
    /// <returns>
    /// The stored integer or <paramref name="defaultValue"/>.
    /// </returns>
    public uint GetAsUInt32(string name, uint defaultValue = default)
        => Get(name, defaultValue);

    /// <summary>
    /// Sets a 32-bit unsigned integer property.
    /// </summary>
    /// <param name="name">
    /// The stable property name.
    /// </param>
    /// <param name="value">
    /// The value to store.
    /// </param>
    public void SetAsUInt32(string name, uint value)
        => Set(name, value);

    /// <summary>
    /// Gets a 64-bit signed integer property, or a fallback when the property is absent.
    /// </summary>
    /// <param name="name">
    /// The stable property name.
    /// </param>
    /// <param name="defaultValue">
    /// The value returned when the property is absent.
    /// </param>
    /// <returns>
    /// The stored integer or <paramref name="defaultValue"/>.
    /// </returns>
    public long GetAsInt64(string name, long defaultValue = default)
        => Get(name, defaultValue);

    /// <summary>
    /// Sets a 64-bit signed integer property.
    /// </summary>
    /// <param name="name">
    /// The stable property name.
    /// </param>
    /// <param name="value">
    /// The value to store.
    /// </param>
    public void SetAsInt64(string name, long value)
        => Set(name, value);

    /// <summary>
    /// Gets a 64-bit unsigned integer property, or a fallback when the property is absent.
    /// </summary>
    /// <param name="name">
    /// The stable property name.
    /// </param>
    /// <param name="defaultValue">
    /// The value returned when the property is absent.
    /// </param>
    /// <returns>
    /// The stored integer or <paramref name="defaultValue"/>.
    /// </returns>
    public ulong GetAsUInt64(string name, ulong defaultValue = default)
        => Get(name, defaultValue);

    /// <summary>
    /// Sets a 64-bit unsigned integer property.
    /// </summary>
    /// <param name="name">
    /// The stable property name.
    /// </param>
    /// <param name="value">
    /// The value to store.
    /// </param>
    public void SetAsUInt64(string name, ulong value)
        => Set(name, value);

    /// <summary>
    /// Gets a single-precision property, or a fallback when the property is absent.
    /// </summary>
    /// <param name="name">
    /// The stable property name.
    /// </param>
    /// <param name="defaultValue">
    /// The value returned when the property is absent.
    /// </param>
    /// <returns>
    /// The stored number or <paramref name="defaultValue"/>.
    /// </returns>
    public float GetAsSingle(string name, float defaultValue = default)
        => Get(name, defaultValue);

    /// <summary>
    /// Sets a finite single-precision property.
    /// </summary>
    /// <param name="name">
    /// The stable property name.
    /// </param>
    /// <param name="value">
    /// The finite value to store.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="value"/> is not finite.
    /// </exception>
    public void SetAsSingle(string name, float value)
    {
        if (!float.IsFinite(value))
            throw new ArgumentOutOfRangeException(nameof(value), "Settings numbers must be finite.");
        Set(name, value);
    }

    /// <summary>
    /// Gets a double-precision property, or a fallback when the property is absent.
    /// </summary>
    /// <param name="name">
    /// The stable property name.
    /// </param>
    /// <param name="defaultValue">
    /// The value returned when the property is absent.
    /// </param>
    /// <returns>
    /// The stored number or <paramref name="defaultValue"/>.
    /// </returns>
    public double GetAsDouble(string name, double defaultValue = default)
        => Get(name, defaultValue);

    /// <summary>
    /// Sets a finite double-precision property.
    /// </summary>
    /// <param name="name">
    /// The stable property name.
    /// </param>
    /// <param name="value">
    /// The finite value to store.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="value"/> is not finite.
    /// </exception>
    public void SetAsDouble(string name, double value)
    {
        if (!double.IsFinite(value))
            throw new ArgumentOutOfRangeException(nameof(value), "Settings numbers must be finite.");
        Set(name, value);
    }

    /// <summary>
    /// Gets a string property, or a fallback when the property is absent or null.
    /// </summary>
    /// <param name="name">
    /// The stable property name.
    /// </param>
    /// <param name="defaultValue">
    /// The value returned when the property is absent or null.
    /// </param>
    /// <returns>
    /// The stored string or <paramref name="defaultValue"/>.
    /// </returns>
    public string? GetAsString(string name, string? defaultValue = null)
        => Get(name, defaultValue);

    /// <summary>
    /// Sets a nullable string property.
    /// </summary>
    /// <param name="name">
    /// The stable property name.
    /// </param>
    /// <param name="value">
    /// The value to store.
    /// </param>
    public void SetAsString(string name, string? value)
        => Set(name, value);

    /// <summary>
    /// Gets a Boolean array property, or a copied fallback when the property is absent.
    /// </summary>
    /// <param name="name">
    /// The stable property name.
    /// </param>
    /// <param name="defaultValue">
    /// The array copied when the property is absent.
    /// </param>
    /// <returns>
    /// An independently owned array.
    /// </returns>
    public bool[] GetAsBooleanArray(string name, bool[]? defaultValue = null)
        => GetArray(name, defaultValue ?? []);

    /// <summary>
    /// Sets a Boolean array property by value.
    /// </summary>
    /// <param name="name">
    /// The stable property name.
    /// </param>
    /// <param name="value">
    /// The array copied into the object.
    /// </param>
    public void SetAsBooleanArray(string name, bool[] value)
        => SetArray(name, value);

    /// <summary>
    /// Gets a 32-bit signed integer array property, or a copied fallback when absent.
    /// </summary>
    /// <param name="name">
    /// The stable property name.
    /// </param>
    /// <param name="defaultValue">
    /// The array copied when the property is absent.
    /// </param>
    /// <returns>
    /// An independently owned array.
    /// </returns>
    public int[] GetAsInt32Array(string name, int[]? defaultValue = null)
        => GetArray(name, defaultValue ?? []);

    /// <summary>
    /// Sets a 32-bit signed integer array property by value.
    /// </summary>
    /// <param name="name">
    /// The stable property name.
    /// </param>
    /// <param name="value">
    /// The array copied into the object.
    /// </param>
    public void SetAsInt32Array(string name, int[] value)
        => SetArray(name, value);

    /// <summary>
    /// Gets a 32-bit unsigned integer array property, or a copied fallback when absent.
    /// </summary>
    /// <param name="name">
    /// The stable property name.
    /// </param>
    /// <param name="defaultValue">
    /// The array copied when the property is absent.
    /// </param>
    /// <returns>
    /// An independently owned array.
    /// </returns>
    public uint[] GetAsUInt32Array(string name, uint[]? defaultValue = null)
        => GetArray(name, defaultValue ?? []);

    /// <summary>
    /// Sets a 32-bit unsigned integer array property by value.
    /// </summary>
    /// <param name="name">
    /// The stable property name.
    /// </param>
    /// <param name="value">
    /// The array copied into the object.
    /// </param>
    public void SetAsUInt32Array(string name, uint[] value)
        => SetArray(name, value);

    /// <summary>
    /// Gets a single-precision array property, or a copied fallback when absent.
    /// </summary>
    /// <param name="name">
    /// The stable property name.
    /// </param>
    /// <param name="defaultValue">
    /// The array copied when the property is absent.
    /// </param>
    /// <returns>
    /// An independently owned array.
    /// </returns>
    public float[] GetAsSingleArray(string name, float[]? defaultValue = null)
        => GetArray(name, defaultValue ?? []);

    /// <summary>
    /// Sets a single-precision array property by value.
    /// </summary>
    /// <param name="name">
    /// The stable property name.
    /// </param>
    /// <param name="value">
    /// The array copied into the object.
    /// </param>
    public void SetAsSingleArray(string name, float[] value)
        => SetArray(name, value);

    /// <summary>
    /// Gets a double-precision array property, or a copied fallback when absent.
    /// </summary>
    /// <param name="name">
    /// The stable property name.
    /// </param>
    /// <param name="defaultValue">
    /// The array copied when the property is absent.
    /// </param>
    /// <returns>
    /// An independently owned array.
    /// </returns>
    public double[] GetAsDoubleArray(string name, double[]? defaultValue = null)
        => GetArray(name, defaultValue ?? []);

    /// <summary>
    /// Sets a double-precision array property by value.
    /// </summary>
    /// <param name="name">
    /// The stable property name.
    /// </param>
    /// <param name="value">
    /// The array copied into the object.
    /// </param>
    public void SetAsDoubleArray(string name, double[] value)
        => SetArray(name, value);

    /// <summary>
    /// Gets a nullable string array property, or a copied fallback when absent.
    /// </summary>
    /// <param name="name">
    /// The stable property name.
    /// </param>
    /// <param name="defaultValue">
    /// The array copied when the property is absent.
    /// </param>
    /// <returns>
    /// An independently owned array.
    /// </returns>
    public string?[] GetAsStringArray(string name, string?[]? defaultValue = null)
        => GetArray(name, defaultValue ?? []);

    /// <summary>
    /// Sets a nullable string array property by value.
    /// </summary>
    /// <param name="name">
    /// The stable property name.
    /// </param>
    /// <param name="value">
    /// The array copied into the object.
    /// </param>
    public void SetAsStringArray(string name, string?[] value)
        => SetArray(name, value);

    internal EditorSettingObject Copy()
    {
        var values = new Dictionary<string, EditorSettingValue>(m_values.Count, StringComparer.Ordinal);
        foreach ((string name, EditorSettingValue value) in m_values)
            values.Add(name, value.Copy());
        return new EditorSettingObject(values);
    }

    internal bool ValueEquals(EditorSettingObject other)
    {
        ArgumentNullException.ThrowIfNull(other);
        if (m_values.Count != other.m_values.Count)
            return false;
        foreach ((string name, EditorSettingValue value) in m_values)
        {
            if (!other.m_values.TryGetValue(name, out EditorSettingValue? candidate)
                || !value.ValueEquals(candidate))
            {
                return false;
            }
        }
        return true;
    }

    internal void Validate(string settingPath)
    {
        if (m_values is null)
            throw new InvalidDataException($"Settings path '{settingPath}' has no value map.");
        foreach ((string name, EditorSettingValue value) in m_values)
        {
            ValidateName(name);
            if (value is null)
                throw new InvalidDataException($"Settings property '{settingPath}/{name}' is null.");
            value.Validate($"{settingPath}/{name}");
        }
    }

    private T Get<T>(string name, T defaultValue)
    {
        ValidateName(name);
        if (!m_values.TryGetValue(name, out EditorSettingValue? value))
            return defaultValue;
        return value.Read(defaultValue, name);
    }

    private T[] GetArray<T>(string name, T[] defaultValue)
        => (T[])Get(name, defaultValue).Clone();

    private void Set<T>(string name, T value)
    {
        ValidateName(name);
        EditorSettingValue next = EditorSettingValue.Create(value);
        if (m_values.TryGetValue(name, out EditorSettingValue? current)
            && current.ValueEquals(next))
        {
            return;
        }
        m_values[name] = next;
    }

    private void SetArray<T>(string name, T[] value)
    {
        ArgumentNullException.ThrowIfNull(value);
        Set(name, (T[])value.Clone());
    }

    private static void ValidateName(string name)
        => ArgumentException.ThrowIfNullOrWhiteSpace(name);
}
