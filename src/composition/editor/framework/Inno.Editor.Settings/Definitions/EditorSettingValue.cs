using System;
using System.IO;
using System.Linq;

using Inno.Core.Serialization;

namespace Inno.Editor.Settings;

[GenerateSerializationConverter]
internal sealed class EditorSettingValue : ISerializable
{
    [SerializableProperty]
    internal EditorSettingValueKind kind;

    [SerializableProperty]
    internal bool booleanValue;

    [SerializableProperty]
    internal int int32Value;

    [SerializableProperty]
    internal uint uint32Value;

    [SerializableProperty]
    internal long int64Value;

    [SerializableProperty]
    internal ulong uint64Value;

    [SerializableProperty]
    internal float singleValue;

    [SerializableProperty]
    internal double doubleValue;

    [SerializableProperty]
    internal bool hasStringValue;

    [SerializableProperty]
    internal string stringValue = string.Empty;

    [SerializableProperty]
    internal bool[] booleanArrayValue = [];

    [SerializableProperty]
    internal int[] int32ArrayValue = [];

    [SerializableProperty]
    internal uint[] uint32ArrayValue = [];

    [SerializableProperty]
    internal float[] singleArrayValue = [];

    [SerializableProperty]
    internal double[] doubleArrayValue = [];

    [SerializableProperty]
    internal string[] stringArrayValue = [];

    [SerializableProperty]
    internal bool[] stringArrayNulls = [];

    internal static EditorSettingValue Create<T>(T value)
    {
        Type type = typeof(T);
        if (type == typeof(bool))
            return new EditorSettingValue { kind = EditorSettingValueKind.Boolean, booleanValue = (bool)(object)value! };
        if (type == typeof(int))
            return new EditorSettingValue { kind = EditorSettingValueKind.Int32, int32Value = (int)(object)value! };
        if (type == typeof(uint))
            return new EditorSettingValue { kind = EditorSettingValueKind.UInt32, uint32Value = (uint)(object)value! };
        if (type == typeof(long))
            return new EditorSettingValue { kind = EditorSettingValueKind.Int64, int64Value = (long)(object)value! };
        if (type == typeof(ulong))
            return new EditorSettingValue { kind = EditorSettingValueKind.UInt64, uint64Value = (ulong)(object)value! };
        if (type == typeof(float))
            return new EditorSettingValue { kind = EditorSettingValueKind.Single, singleValue = (float)(object)value! };
        if (type == typeof(double))
            return new EditorSettingValue { kind = EditorSettingValueKind.Double, doubleValue = (double)(object)value! };
        if (type == typeof(string))
        {
            string? text = (string?)(object?)value;
            return new EditorSettingValue
            {
                kind = EditorSettingValueKind.String,
                hasStringValue = text is not null,
                stringValue = text ?? string.Empty
            };
        }
        if (type == typeof(bool[]))
            return new EditorSettingValue { kind = EditorSettingValueKind.BooleanArray, booleanArrayValue = Clone((bool[])(object)value!) };
        if (type == typeof(int[]))
            return new EditorSettingValue { kind = EditorSettingValueKind.Int32Array, int32ArrayValue = Clone((int[])(object)value!) };
        if (type == typeof(uint[]))
            return new EditorSettingValue { kind = EditorSettingValueKind.UInt32Array, uint32ArrayValue = Clone((uint[])(object)value!) };
        if (type == typeof(float[]))
            return new EditorSettingValue { kind = EditorSettingValueKind.SingleArray, singleArrayValue = Clone((float[])(object)value!) };
        if (type == typeof(double[]))
            return new EditorSettingValue { kind = EditorSettingValueKind.DoubleArray, doubleArrayValue = Clone((double[])(object)value!) };
        if (type == typeof(string[]))
        {
            string?[] source = (string?[])(object)value!;
            var values = new string[source.Length];
            var nulls = new bool[source.Length];
            for (int i = 0; i < source.Length; i++)
            {
                nulls[i] = source[i] is null;
                values[i] = source[i] ?? string.Empty;
            }
            return new EditorSettingValue
            {
                kind = EditorSettingValueKind.StringArray,
                stringArrayValue = values,
                stringArrayNulls = nulls
            };
        }

        throw new NotSupportedException($"Editor Settings values do not support '{type.FullName}'.");
    }

    internal T Read<T>(T defaultValue, string propertyName)
    {
        Type type = typeof(T);
        object? value;
        if (type == typeof(bool) && kind == EditorSettingValueKind.Boolean)
            value = booleanValue;
        else if (type == typeof(int) && kind == EditorSettingValueKind.Int32)
            value = int32Value;
        else if (type == typeof(uint) && kind == EditorSettingValueKind.UInt32)
            value = uint32Value;
        else if (type == typeof(long) && kind == EditorSettingValueKind.Int64)
            value = int64Value;
        else if (type == typeof(ulong) && kind == EditorSettingValueKind.UInt64)
            value = uint64Value;
        else if (type == typeof(float) && kind == EditorSettingValueKind.Single)
            value = singleValue;
        else if (type == typeof(double) && kind == EditorSettingValueKind.Double)
            value = doubleValue;
        else if (type == typeof(string) && kind == EditorSettingValueKind.String)
            value = hasStringValue ? stringValue : defaultValue;
        else if (type == typeof(bool[]) && kind == EditorSettingValueKind.BooleanArray)
            value = Clone(booleanArrayValue);
        else if (type == typeof(int[]) && kind == EditorSettingValueKind.Int32Array)
            value = Clone(int32ArrayValue);
        else if (type == typeof(uint[]) && kind == EditorSettingValueKind.UInt32Array)
            value = Clone(uint32ArrayValue);
        else if (type == typeof(float[]) && kind == EditorSettingValueKind.SingleArray)
            value = Clone(singleArrayValue);
        else if (type == typeof(double[]) && kind == EditorSettingValueKind.DoubleArray)
            value = Clone(doubleArrayValue);
        else if (type == typeof(string[]) && kind == EditorSettingValueKind.StringArray)
            value = ReadStringArray();
        else
            throw new InvalidDataException(
                $"Settings property '{propertyName}' contains {kind}, not {GetKind(type)}.");

        return (T)value!;
    }

    internal EditorSettingValue Copy()
        => new()
        {
            kind = kind,
            booleanValue = booleanValue,
            int32Value = int32Value,
            uint32Value = uint32Value,
            int64Value = int64Value,
            uint64Value = uint64Value,
            singleValue = singleValue,
            doubleValue = doubleValue,
            hasStringValue = hasStringValue,
            stringValue = stringValue,
            booleanArrayValue = Clone(booleanArrayValue),
            int32ArrayValue = Clone(int32ArrayValue),
            uint32ArrayValue = Clone(uint32ArrayValue),
            singleArrayValue = Clone(singleArrayValue),
            doubleArrayValue = Clone(doubleArrayValue),
            stringArrayValue = Clone(stringArrayValue),
            stringArrayNulls = Clone(stringArrayNulls)
        };

    internal bool ValueEquals(EditorSettingValue other)
    {
        ArgumentNullException.ThrowIfNull(other);
        if (kind != other.kind)
            return false;
        return kind switch
        {
            EditorSettingValueKind.Boolean => booleanValue == other.booleanValue,
            EditorSettingValueKind.Int32 => int32Value == other.int32Value,
            EditorSettingValueKind.UInt32 => uint32Value == other.uint32Value,
            EditorSettingValueKind.Int64 => int64Value == other.int64Value,
            EditorSettingValueKind.UInt64 => uint64Value == other.uint64Value,
            EditorSettingValueKind.Single => singleValue.Equals(other.singleValue),
            EditorSettingValueKind.Double => doubleValue.Equals(other.doubleValue),
            EditorSettingValueKind.String => hasStringValue == other.hasStringValue
                && string.Equals(stringValue, other.stringValue, StringComparison.Ordinal),
            EditorSettingValueKind.BooleanArray => booleanArrayValue.SequenceEqual(other.booleanArrayValue),
            EditorSettingValueKind.Int32Array => int32ArrayValue.SequenceEqual(other.int32ArrayValue),
            EditorSettingValueKind.UInt32Array => uint32ArrayValue.SequenceEqual(other.uint32ArrayValue),
            EditorSettingValueKind.SingleArray => singleArrayValue.SequenceEqual(other.singleArrayValue),
            EditorSettingValueKind.DoubleArray => doubleArrayValue.SequenceEqual(other.doubleArrayValue),
            EditorSettingValueKind.StringArray => stringArrayValue.SequenceEqual(other.stringArrayValue, StringComparer.Ordinal)
                && stringArrayNulls.SequenceEqual(other.stringArrayNulls),
            _ => false
        };
    }

    internal void Validate(string propertyName)
    {
        if (!Enum.IsDefined(kind))
            throw new InvalidDataException($"Settings property '{propertyName}' has an unknown value kind.");
        if (booleanArrayValue is null
            || int32ArrayValue is null
            || uint32ArrayValue is null
            || singleArrayValue is null
            || doubleArrayValue is null
            || stringArrayValue is null
            || stringArrayNulls is null)
        {
            throw new InvalidDataException($"Settings property '{propertyName}' has an invalid null array payload.");
        }
        if (stringValue is null)
            throw new InvalidDataException($"Settings property '{propertyName}' has an invalid null string payload.");
        if (kind == EditorSettingValueKind.StringArray
            && stringArrayValue.Length != stringArrayNulls.Length)
        {
            throw new InvalidDataException($"Settings property '{propertyName}' has inconsistent string-array payloads.");
        }
        if (kind == EditorSettingValueKind.Single && !float.IsFinite(singleValue)
            || kind == EditorSettingValueKind.Double && !double.IsFinite(doubleValue)
            || kind == EditorSettingValueKind.SingleArray && singleArrayValue.Any(static value => !float.IsFinite(value))
            || kind == EditorSettingValueKind.DoubleArray && doubleArrayValue.Any(static value => !double.IsFinite(value)))
        {
            throw new InvalidDataException($"Settings property '{propertyName}' contains a non-finite number.");
        }
    }

    private static EditorSettingValueKind GetKind(Type type)
    {
        if (type == typeof(bool)) return EditorSettingValueKind.Boolean;
        if (type == typeof(int)) return EditorSettingValueKind.Int32;
        if (type == typeof(uint)) return EditorSettingValueKind.UInt32;
        if (type == typeof(long)) return EditorSettingValueKind.Int64;
        if (type == typeof(ulong)) return EditorSettingValueKind.UInt64;
        if (type == typeof(float)) return EditorSettingValueKind.Single;
        if (type == typeof(double)) return EditorSettingValueKind.Double;
        if (type == typeof(string)) return EditorSettingValueKind.String;
        if (type == typeof(bool[])) return EditorSettingValueKind.BooleanArray;
        if (type == typeof(int[])) return EditorSettingValueKind.Int32Array;
        if (type == typeof(uint[])) return EditorSettingValueKind.UInt32Array;
        if (type == typeof(float[])) return EditorSettingValueKind.SingleArray;
        if (type == typeof(double[])) return EditorSettingValueKind.DoubleArray;
        if (type == typeof(string[])) return EditorSettingValueKind.StringArray;
        throw new NotSupportedException($"Editor Settings values do not support '{type.FullName}'.");
    }

    private string?[] ReadStringArray()
    {
        var result = new string?[stringArrayValue.Length];
        for (int i = 0; i < result.Length; i++)
            result[i] = stringArrayNulls[i] ? null : stringArrayValue[i];
        return result;
    }

    private static T[] Clone<T>(T[] value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return (T[])value.Clone();
    }
}

internal enum EditorSettingValueKind
{
    Boolean,
    Int32,
    UInt32,
    Int64,
    UInt64,
    Single,
    Double,
    String,
    BooleanArray,
    Int32Array,
    UInt32Array,
    SingleArray,
    DoubleArray,
    StringArray
}
