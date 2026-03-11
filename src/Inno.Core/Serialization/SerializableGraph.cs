using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Inno.Core.Serialization;

internal static class SerializableGraph
{
    #region Primitive / State Tests

    internal static bool IsAllowedPrimitive(Type t) =>
        t == typeof(bool)
        || t == typeof(byte) || t == typeof(sbyte)
        || t == typeof(short) || t == typeof(ushort)
        || t == typeof(int) || t == typeof(uint)
        || t == typeof(long) || t == typeof(ulong)
        || t == typeof(float) || t == typeof(double)
        || t == typeof(decimal)
        || t == typeof(string)
        || t == typeof(Guid);

    internal static bool IsSerializingState(Type t) => t == typeof(SerializingState);

    #endregion

    #region Collection Helpers

    internal static bool TryGetListElementType(Type t, out Type elem)
    {
        elem = null!;

        if (t.IsArray)
            return false;

        if (t.IsGenericType)
        {
            var def = t.GetGenericTypeDefinition();
            if (def == typeof(List<>) || def == typeof(IList<>) || def == typeof(IReadOnlyList<>))
            {
                elem = t.GetGenericArguments()[0];
                return true;
            }
        }

        var ilist = t.GetInterfaces().FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IList<>));
        if (ilist != null)
        {
            elem = ilist.GetGenericArguments()[0];
            return true;
        }

        var irolist = t.GetInterfaces().FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IReadOnlyList<>));
        if (irolist != null)
        {
            elem = irolist.GetGenericArguments()[0];
            return true;
        }

        return false;
    }

    internal static bool TryGetDictionaryTypes(Type t, out Type keyType, out Type valueType)
    {
        keyType = null!;
        valueType = null!;

        if (t.IsGenericType)
        {
            var def = t.GetGenericTypeDefinition();
            if (def == typeof(Dictionary<,>) || def == typeof(IDictionary<,>) || def == typeof(IReadOnlyDictionary<,>))
            {
                var args = t.GetGenericArguments();
                keyType = args[0];
                valueType = args[1];
                return true;
            }
        }

        foreach (var i in t.GetInterfaces())
        {
            if (!i.IsGenericType) continue;

            var def = i.GetGenericTypeDefinition();
            if (def == typeof(IDictionary<,>) || def == typeof(IReadOnlyDictionary<,>))
            {
                var args = i.GetGenericArguments();
                keyType = args[0];
                valueType = args[1];
                return true;
            }
        }

        return false;
    }

    #endregion

    #region Member Visibility

    internal static PropertyVisibility GetVisibilityOrShow(MemberInfo m) =>
        m.GetCustomAttribute<SerializablePropertyAttribute>(inherit: true)?.propertyVisibility ?? PropertyVisibility.Show;

    internal static FieldInfo[] GetStructSerializableFields(Type t) =>
        t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(f => (GetVisibilityOrShow(f) & PropertyVisibility.Transient) != 0)
            .OrderBy(f => f.MetadataToken)
            .ToArray();

    internal static PropertyInfo[] GetStructSerializableProperties(Type t) =>
        t.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(p =>
            {
                if (p.GetIndexParameters().Length != 0) return false;

                var vis = GetVisibilityOrShow(p);
                if ((vis & PropertyVisibility.Transient) == 0) return false;
                if (!p.CanRead) return false;

                return !((vis & PropertyVisibility.Deserialize) != 0 && p.GetSetMethod(nonPublic: true) == null);
            })
            .OrderBy(p => p.MetadataToken)
            .ToArray();

    #endregion

    #region Graph Validation

    internal static void ValidateAllowedTypeGraph(Type type, string where) =>
        ValidateAllowedTypeGraphRec(type, where, new HashSet<Type>(), forbidISerializable: false);

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

        if (TryGetListElementType(t, out var listElem))
        {
            ValidateAllowedTypeGraphRec(listElem, $"{where}<T>", visited, forbidISerializable);
            return;
        }

        if (TryGetDictionaryTypes(t, out var kType, out var vType))
        {
            ValidateAllowedTypeGraphRec(kType, $"{where}<K>", visited, forbidISerializable);
            ValidateAllowedTypeGraphRec(vType, $"{where}<V>", visited, forbidISerializable);
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
            "Allowed: primitives, enums, structs (recursive), ISerializable (recursive), arrays, List<T>, Dictionary<K,V>.");
    }

    #endregion
}
