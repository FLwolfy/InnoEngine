using System;

namespace Inno.Core.Serialization;

internal static class PrimitiveSerializer
{
    internal static bool IsPrimitiveType(Type type)
    {
        Type t = Nullable.GetUnderlyingType(type) ?? type;
        return t == typeof(bool) ||
               t == typeof(byte) ||
               t == typeof(sbyte) ||
               t == typeof(short) ||
               t == typeof(ushort) ||
               t == typeof(int) ||
               t == typeof(uint) ||
               t == typeof(long) ||
               t == typeof(ulong) ||
               t == typeof(float) ||
               t == typeof(double) ||
               t == typeof(decimal) ||
               t == typeof(string) ||
               t == typeof(Guid);
    }

    internal static object? Serialize(object value, Type declaredType)
    {
        Type t = Nullable.GetUnderlyingType(declaredType) ?? declaredType;
        if (!IsPrimitiveType(t))
            throw new InvalidOperationException($"Type '{t.FullName}' is not a supported primitive.");

        return value;
    }

    internal static object? Deserialize(object? node, Type declaredType)
    {
        Type t = Nullable.GetUnderlyingType(declaredType) ?? declaredType;
        if (!IsPrimitiveType(t))
            throw new InvalidOperationException($"Type '{t.FullName}' is not a supported primitive.");

        if (t == typeof(string))
            return node as string ?? string.Empty;
        if (t == typeof(Guid))
            return node is Guid guid ? guid : Guid.Parse(node?.ToString() ?? Guid.Empty.ToString());
        if (t == typeof(bool))
            return Convert.ToBoolean(node);
        if (t == typeof(byte))
            return Convert.ToByte(node);
        if (t == typeof(sbyte))
            return Convert.ToSByte(node);
        if (t == typeof(short))
            return Convert.ToInt16(node);
        if (t == typeof(ushort))
            return Convert.ToUInt16(node);
        if (t == typeof(int))
            return Convert.ToInt32(node);
        if (t == typeof(uint))
            return Convert.ToUInt32(node);
        if (t == typeof(long))
            return Convert.ToInt64(node);
        if (t == typeof(ulong))
            return Convert.ToUInt64(node);
        if (t == typeof(float))
            return Convert.ToSingle(node);
        if (t == typeof(double))
            return Convert.ToDouble(node);
        if (t == typeof(decimal))
            return Convert.ToDecimal(node);

        throw new InvalidOperationException($"Unsupported primitive type: {t.FullName}");
    }
}
