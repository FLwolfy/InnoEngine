using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Inno.Core.Serialization;

internal static class StructSerializer
{
    internal readonly record struct StructFieldEntry(FieldInfo field, bool canDeserialize);
    internal readonly record struct StructPropertyEntry(PropertyInfo property, bool canDeserialize);

    internal static bool IsStructPayloadType(Type type)
    {
        Type t = Nullable.GetUnderlyingType(type) ?? type;
        if (!t.IsValueType || t.IsEnum)
            return false;

        if (PrimitiveSerializer.IsPrimitiveType(t) ||
            t == typeof(DateTime) ||
            t == typeof(DateTimeOffset) ||
            t == typeof(TimeSpan))
        {
            return false;
        }

        return true;
    }

    internal static object Serialize(object value, Type structType, Func<object?, Type, object?> serializeValue)
    {
        Type t = Nullable.GetUnderlyingType(structType) ?? structType;
        if (!IsStructPayloadType(t))
            throw new InvalidOperationException($"Type '{t.FullName}' is not a struct payload type.");

        object boxed = value;
        FieldInfo[] fields = GetStructSerializableFields(t);
        PropertyInfo[] properties = GetStructSerializableProperties(t);
        var map = new Dictionary<string, object?>(fields.Length + properties.Length, StringComparer.Ordinal);

        for (int i = 0; i < fields.Length; i++)
        {
            FieldInfo field = fields[i];
            map["F:" + field.Name] = serializeValue(field.GetValue(boxed), field.FieldType);
        }

        for (int i = 0; i < properties.Length; i++)
        {
            PropertyInfo property = properties[i];
            map["P:" + property.Name] = serializeValue(property.GetValue(boxed), property.PropertyType);
        }

        return map;
    }

    internal static object Deserialize(object raw, Type structType, Func<object?, Type, object?> deserializeValue)
    {
        Type t = Nullable.GetUnderlyingType(structType) ?? structType;
        if (!IsStructPayloadType(t))
            throw new InvalidOperationException($"Type '{t.FullName}' is not a struct payload type.");

        if (raw is not IDictionary dict)
            throw new InvalidOperationException($"Struct node must be dictionary. Got: {raw.GetType().FullName}");

        object boxed = Activator.CreateInstance(t)!;
        IReadOnlyDictionary<string, StructFieldEntry> fieldMap = GetStructSerializableFieldMap(t);
        IReadOnlyDictionary<string, StructPropertyEntry> propertyMap = GetStructSerializablePropertyMap(t);

        foreach (DictionaryEntry entry in dict)
        {
            if (entry.Key is not string key)
                continue;

            if (fieldMap.TryGetValue(key, out StructFieldEntry fieldEntry))
            {
                if (fieldEntry.canDeserialize)
                {
                    object? restored = deserializeValue(entry.Value, fieldEntry.field.FieldType);
                    fieldEntry.field.SetValue(boxed, restored);
                }

                continue;
            }

            if (propertyMap.TryGetValue(key, out StructPropertyEntry propertyEntry) && propertyEntry.canDeserialize)
            {
                object? restored = deserializeValue(entry.Value, propertyEntry.property.PropertyType);
                propertyEntry.property.SetValue(boxed, restored);
            }
        }

        return boxed;
    }

    internal static FieldInfo[] GetStructSerializableFields(Type type)
    {
        Type t = Nullable.GetUnderlyingType(type) ?? type;
        return t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(f => (GetVisibilityOrShow(f) & PropertyVisibility.Transient) != 0)
            .OrderBy(f => f.MetadataToken)
            .ToArray();
    }

    internal static PropertyInfo[] GetStructSerializableProperties(Type type)
    {
        Type t = Nullable.GetUnderlyingType(type) ?? type;
        return t.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(p =>
            {
                if (p.GetIndexParameters().Length != 0)
                    return false;

                PropertyVisibility vis = GetVisibilityOrShow(p);
                if ((vis & PropertyVisibility.Transient) == 0)
                    return false;
                if (!p.CanRead)
                    return false;

                return !((vis & PropertyVisibility.Deserialize) != 0 && p.GetSetMethod(nonPublic: true) == null);
            })
            .OrderBy(p => p.MetadataToken)
            .ToArray();
    }

    internal static IReadOnlyDictionary<string, StructFieldEntry> GetStructSerializableFieldMap(Type type)
    {
        FieldInfo[] fields = GetStructSerializableFields(type);
        var map = new Dictionary<string, StructFieldEntry>(fields.Length, StringComparer.Ordinal);
        for (int i = 0; i < fields.Length; i++)
        {
            FieldInfo field = fields[i];
            PropertyVisibility visibility = GetVisibilityOrShow(field);
            map["F:" + field.Name] = new StructFieldEntry(
                field,
                (visibility & PropertyVisibility.Deserialize) != 0 && !field.IsInitOnly);
        }

        return map;
    }

    internal static IReadOnlyDictionary<string, StructPropertyEntry> GetStructSerializablePropertyMap(Type type)
    {
        PropertyInfo[] properties = GetStructSerializableProperties(type);
        var map = new Dictionary<string, StructPropertyEntry>(properties.Length, StringComparer.Ordinal);
        for (int i = 0; i < properties.Length; i++)
        {
            PropertyInfo property = properties[i];
            PropertyVisibility visibility = GetVisibilityOrShow(property);
            map["P:" + property.Name] = new StructPropertyEntry(
                property,
                (visibility & PropertyVisibility.Deserialize) != 0 && property.CanWrite);
        }

        return map;
    }

    private static PropertyVisibility GetVisibilityOrShow(MemberInfo member)
        => member.GetCustomAttribute<SerializablePropertyAttribute>(inherit: true)?.propertyVisibility ?? PropertyVisibility.Show;
}
