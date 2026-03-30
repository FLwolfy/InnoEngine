using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;

using Inno.Core.Reflection;

namespace Inno.Core.Serialization;

internal static class SerializableGraph
{
    private static readonly HashSet<Type> ALLOWED_PRIMITIVES = new()
    {
        typeof(bool),
        typeof(byte),
        typeof(sbyte),
        typeof(short),
        typeof(ushort),
        typeof(int),
        typeof(uint),
        typeof(long),
        typeof(ulong),
        typeof(float),
        typeof(double),
        typeof(decimal),
        typeof(string),
        typeof(Guid)
    };

    private sealed class TypeShape
    {
        public bool hasListElement;
        public Type? listElementType;
        public bool hasDictionaryTypes;
        public Type? dictionaryKeyType;
        public Type? dictionaryValueType;
    }

    private sealed class StructMembers
    {
        public required FieldInfo[] fields { get; init; }
        public required PropertyInfo[] properties { get; init; }
        public required Dictionary<string, StructFieldEntry> fieldMap { get; init; }
        public required Dictionary<string, StructPropertyEntry> propertyMap { get; init; }
    }

    private sealed class GraphValidationState
    {
        public int flags;
        public object syncRoot { get; } = new();
    }

    internal readonly record struct StructFieldEntry(FieldInfo field, bool canDeserialize);
    internal readonly record struct StructPropertyEntry(PropertyInfo property, bool canDeserialize);

    private static readonly Lock TYPE_SHAPE_CACHE_SYNC = new();
    private static readonly Dictionary<int, TypeShape> TYPE_SHAPE_CACHE = [];
    private static readonly Lock STRUCT_MEMBER_CACHE_SYNC = new();
    private static readonly Dictionary<int, StructMembers> STRUCT_MEMBER_CACHE = [];
    private static readonly Lock VALIDATION_CACHE_SYNC = new();
    private static readonly Dictionary<int, GraphValidationState> VALIDATION_CACHE = [];
    private static readonly Lock LOCAL_TYPE_KEY_SYNC = new();
    private static readonly Dictionary<Type, int> LOCAL_TYPE_KEYS = [];
    private static int s_nextLocalTypeKey = int.MinValue;

    private const int VALIDATION_FLAG_ALLOW_ISERIALIZABLE = 1 << 0;

    #region Primitive / State Tests

    internal static bool IsAllowedPrimitive(Type t) => ALLOWED_PRIMITIVES.Contains(t);

    internal static bool IsSerializingState(Type t) => t == typeof(SerializingState);

    #endregion

    #region Collection Helpers

    internal static bool TryGetListElementType(Type t, out Type elem)
    {
        var shape = GetTypeShape(t);
        elem = shape.listElementType!;
        return shape.hasListElement;
    }

    internal static bool TryGetDictionaryTypes(Type t, out Type keyType, out Type valueType)
    {
        var shape = GetTypeShape(t);
        keyType = shape.dictionaryKeyType!;
        valueType = shape.dictionaryValueType!;
        return shape.hasDictionaryTypes;
    }

    internal static bool TryEnumerateDictionaryEntries(
        object dictionaryLike,
        Type dictionaryLikeType,
        out List<KeyValuePair<object?, object?>> entries)
    {
        entries = new List<KeyValuePair<object?, object?>>();

        if (!TryGetDictionaryTypes(dictionaryLikeType, out _, out _))
            return false;

        if (dictionaryLike is System.Collections.IDictionary dict)
        {
            entries.Capacity = dict.Count;
            foreach (System.Collections.DictionaryEntry e in dict)
                entries.Add(new KeyValuePair<object?, object?>(e.Key, e.Value));
            return true;
        }

        if (dictionaryLike is not System.Collections.IEnumerable enumerable)
            return false;

        foreach (var item in enumerable)
        {
            if (item == null)
                return false;

            var itemType = item.GetType();
            if (!itemType.IsGenericType || itemType.GetGenericTypeDefinition() != typeof(KeyValuePair<,>))
                return false;

            var key = itemType.GetProperty("Key")!.GetValue(item);
            var value = itemType.GetProperty("Value")!.GetValue(item);
            entries.Add(new KeyValuePair<object?, object?>(key, value));
        }

        return true;
    }

    #endregion

    #region Member Visibility

    internal static PropertyVisibility GetVisibilityOrShow(MemberInfo m) =>
        m.GetCustomAttribute<SerializablePropertyAttribute>(inherit: true)?.propertyVisibility ?? PropertyVisibility.Show;

    internal static FieldInfo[] GetStructSerializableFields(Type t) =>
        GetStructMembers(t).fields;

    internal static PropertyInfo[] GetStructSerializableProperties(Type t) =>
        GetStructMembers(t).properties;

    internal static IReadOnlyDictionary<string, StructFieldEntry> GetStructSerializableFieldMap(Type t) =>
        GetStructMembers(t).fieldMap;

    internal static IReadOnlyDictionary<string, StructPropertyEntry> GetStructSerializablePropertyMap(Type t) =>
        GetStructMembers(t).propertyMap;

    #endregion

    #region Graph Validation

    internal static void ValidateAllowedTypeGraph(Type type, string where)
    {
        var normalizedType = Nullable.GetUnderlyingType(type) ?? type;
        var state = GetValidationState(normalizedType);
        var flag = VALIDATION_FLAG_ALLOW_ISERIALIZABLE;

        if ((Volatile.Read(ref state.flags) & flag) != 0)
            return;

        lock (state.syncRoot)
        {
            if ((state.flags & flag) != 0)
                return;

            ValidateAllowedTypeGraphRec(type, where, new HashSet<Type>(), forbidISerializable: false);
            state.flags |= flag;
        }
    }

    private static void ValidateAllowedTypeGraphRec(Type type, string where, HashSet<Type> visited, bool forbidISerializable)
    {
        var t = Nullable.GetUnderlyingType(type) ?? type;
        if (!visited.Add(t)) return;

        if (t.IsEnum || IsAllowedPrimitive(t) || IsSerializingState(t))
            return;

        if (t.IsArray)
        {
            ValidateAllowedTypeGraphRec(t.GetElementType()!, $"{where}[]", visited, forbidISerializable);
            return;
        }

        if (TryGetDictionaryTypes(t, out var kType, out var vType))
        {
            ValidateAllowedTypeGraphRec(kType, $"{where}<K>", visited, forbidISerializable);
            ValidateAllowedTypeGraphRec(vType, $"{where}<V>", visited, forbidISerializable);
            return;
        }

        if (TryGetListElementType(t, out var listElem))
        {
            ValidateAllowedTypeGraphRec(listElem, $"{where}<T>", visited, forbidISerializable);
            return;
        }

        if (typeof(ISerializable).IsAssignableFrom(t))
        {
            if (forbidISerializable)
                throw new InvalidOperationException($"{where} contains '{t.FullName}', but ISerializable is forbidden inside a non-ISerializable struct graph.");

            var slots = ISerializable.GetSlotsForValidation(t);
            for (var i = 0; i < slots.Length; i++)
                ValidateAllowedTypeGraphRec(slots[i].type, $"{t.FullName}", visited, forbidISerializable: false);

            return;
        }

        if (t.IsValueType)
        {
            foreach (var f in GetStructSerializableFields(t))
                ValidateAllowedTypeGraphRec(f.FieldType, $"{t.FullName}.{f.Name}", visited, forbidISerializable: true);

            foreach (var p in GetStructSerializableProperties(t))
                ValidateAllowedTypeGraphRec(p.PropertyType, $"{t.FullName}.{p.Name}", visited, forbidISerializable: true);

            return;
        }

        throw new InvalidOperationException(
            $"{where} has unsupported type '{t.FullName}'. " +
            "Allowed: primitives, enums, structs (recursive), ISerializable (recursive), arrays, sequence collections, map collections.");
    }

    #endregion

    #region Cache Builders

    private static TypeShape GetTypeShape(Type type)
    {
        int typeId = GetTypeCacheKey(type);
        lock (TYPE_SHAPE_CACHE_SYNC)
        {
            if (TYPE_SHAPE_CACHE.TryGetValue(typeId, out TypeShape? existing))
            {
                return existing;
            }

            TypeShape created = BuildTypeShape(type);
            TYPE_SHAPE_CACHE[typeId] = created;
            return created;
        }
    }

    private static StructMembers GetStructMembers(Type type)
    {
        int typeId = GetTypeCacheKey(type);
        lock (STRUCT_MEMBER_CACHE_SYNC)
        {
            if (STRUCT_MEMBER_CACHE.TryGetValue(typeId, out StructMembers? existing))
            {
                return existing;
            }

            StructMembers created = BuildStructMembers(type);
            STRUCT_MEMBER_CACHE[typeId] = created;
            return created;
        }
    }

    private static GraphValidationState GetValidationState(Type type)
    {
        int typeId = GetTypeCacheKey(type);
        lock (VALIDATION_CACHE_SYNC)
        {
            if (VALIDATION_CACHE.TryGetValue(typeId, out GraphValidationState? existing))
            {
                return existing;
            }

            GraphValidationState created = new();
            VALIDATION_CACHE[typeId] = created;
            return created;
        }
    }

    private static TypeShape BuildTypeShape(Type t)
    {
        var shape = new TypeShape();

        if (t == typeof(string))
            return shape;

        var candidates = EnumerateSelfAndInterfaces(t);

        foreach (var candidate in candidates)
        {
            if (!candidate.IsGenericType)
                continue;

            var def = candidate.GetGenericTypeDefinition();
            if (def == typeof(IDictionary<,>) || def == typeof(IReadOnlyDictionary<,>))
            {
                var args = candidate.GetGenericArguments();
                shape.hasDictionaryTypes = true;
                shape.dictionaryKeyType = args[0];
                shape.dictionaryValueType = args[1];
                break;
            }

            if (def != typeof(IEnumerable<>))
                continue;

            var elemType = candidate.GetGenericArguments()[0];
            if (!elemType.IsGenericType || elemType.GetGenericTypeDefinition() != typeof(KeyValuePair<,>))
                continue;

            var kvArgs = elemType.GetGenericArguments();
            shape.hasDictionaryTypes = true;
            shape.dictionaryKeyType = kvArgs[0];
            shape.dictionaryValueType = kvArgs[1];
            break;
        }

        if (t.IsArray)
        {
            shape.hasListElement = false;
            return shape;
        }

        if (!shape.hasDictionaryTypes)
        {
            foreach (var candidate in candidates)
            {
                if (!candidate.IsGenericType)
                    continue;

                var def = candidate.GetGenericTypeDefinition();
                if (def != typeof(IEnumerable<>))
                    continue;

                var elemType = candidate.GetGenericArguments()[0];
                if (elemType == typeof(char) && t == typeof(string))
                    continue;

                shape.hasListElement = true;
                shape.listElementType = elemType;
                break;
            }
        }

        return shape;
    }

    private static IEnumerable<Type> EnumerateSelfAndInterfaces(Type t)
    {
        yield return t;

        var interfaces = t.GetInterfaces();
        for (var i = 0; i < interfaces.Length; i++)
            yield return interfaces[i];
    }

    private static StructMembers BuildStructMembers(Type t)
    {
        var fields = t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(f => (GetVisibilityOrShow(f) & PropertyVisibility.Transient) != 0)
            .OrderBy(f => f.MetadataToken)
            .ToArray();

        var properties = t.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(p =>
            {
                if (p.GetIndexParameters().Length != 0)
                    return false;

                var vis = GetVisibilityOrShow(p);
                if ((vis & PropertyVisibility.Transient) == 0)
                    return false;
                if (!p.CanRead)
                    return false;

                return !((vis & PropertyVisibility.Deserialize) != 0 && p.GetSetMethod(nonPublic: true) == null);
            })
            .OrderBy(p => p.MetadataToken)
            .ToArray();

        var fieldMap = new Dictionary<string, StructFieldEntry>(fields.Length, StringComparer.Ordinal);
        for (var i = 0; i < fields.Length; i++)
        {
            var field = fields[i];
            var visibility = GetVisibilityOrShow(field);
            fieldMap["F:" + field.Name] = new StructFieldEntry(
                field,
                (visibility & PropertyVisibility.Deserialize) != 0 && !field.IsInitOnly);
        }

        var propertyMap = new Dictionary<string, StructPropertyEntry>(properties.Length, StringComparer.Ordinal);
        for (var i = 0; i < properties.Length; i++)
        {
            var property = properties[i];
            var visibility = GetVisibilityOrShow(property);
            propertyMap["P:" + property.Name] = new StructPropertyEntry(
                property,
                (visibility & PropertyVisibility.Deserialize) != 0 && property.CanWrite);
        }

        return new StructMembers
        {
            fields = fields,
            properties = properties,
            fieldMap = fieldMap,
            propertyMap = propertyMap
        };
    }

    private static int GetTypeCacheKey(Type type)
    {
        if (TypeCache.TryGetRuntimeTypeId(type, out int runtimeTypeId))
        {
            return runtimeTypeId;
        }

        lock (LOCAL_TYPE_KEY_SYNC)
        {
            if (LOCAL_TYPE_KEYS.TryGetValue(type, out int existing))
            {
                return existing;
            }

            int created = s_nextLocalTypeKey++;
            LOCAL_TYPE_KEYS[type] = created;
            return created;
        }
    }

    #endregion
}
