using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;

using Inno.Core.Logging;
using Inno.Core.Reflection;

namespace Inno.Core.Serialization;


/// <summary>
/// Marks an instance method to be invoked after <see cref="ISerializable.RestoreState"/> completes.
/// </summary>
/// <remarks>
/// The method must be parameterless and return void. Invocation order is base-type to derived-type.
/// </remarks>
[AttributeUsage(AttributeTargets.Method, Inherited = true)]
public sealed class OnSerializableRestored : Attribute;


/// <summary>
/// Represents an object that can capture and restore its state using <see cref="SerializingState"/>.
/// </summary>
/// <remarks>
/// Only members annotated with <see cref="SerializablePropertyAttribute"/> participate in the state graph.
/// </remarks>
public interface ISerializable
{
    #region Public API

    /// <summary>
    /// Returns the serialized properties declared on this instance type.
    /// </summary>
    /// <returns>A stable, ordered list of serialized properties for this instance.</returns>
    public IReadOnlyList<SerializedProperty> GetSerializedProperties()
    {
        var slots = GetSlots(GetType());
        var result = new List<SerializedProperty>(slots.Length);

        foreach (var s in slots)
        {
            if (s.visibility == PropertyVisibility.Hide)
                continue;

            var noSetterAllowed =
                ((s.visibility & PropertyVisibility.RuntimeSet) == 0);

            result.Add(new SerializedProperty(
                s.name,
                s.type,
                () => s.getter(this),
                v =>
                {
                    if (noSetterAllowed)
                    {
                        Log.Warn($"SerializedProperty {s.name} is not allowed to set its value.");
                        return;
                    }

                    s.setter(this, v);
                },
                s.visibility));
        }

        return result;
    }

    /// <summary>
    /// Captures this instance into an in-memory state tree.
    /// </summary>
    /// <returns>A <see cref="SerializingState"/> representing this instance.</returns>
    /// <exception cref="InvalidOperationException">Thrown when a member value is not supported by the serialization graph.</exception>
    public SerializingState CaptureState()
    {
        var slots = GetSlots(GetType());
        var node = new Dictionary<string, object?>(slots.Length, StringComparer.Ordinal);

        foreach (var s in slots)
        {
            if ((s.visibility & PropertyVisibility.Serialize) == 0)
            {
                continue;
            }

            node[s.name] = CaptureValue(s.getter(this), s.type);
        }

        return new SerializingState(node);
    }

    /// <summary>
    /// Restores this instance from a previously captured state.
    /// </summary>
    /// <param name="state">The source state.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="state"/> is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown when a state value cannot be converted to the declared member type.</exception>
    /// <remarks>
    /// After restoration, methods annotated with <see cref="OnSerializableRestored"/> are invoked.
    /// </remarks>
    public void RestoreState(SerializingState state)
    {
        if (state == null) throw new ArgumentNullException(nameof(state));

        var slots = GetSlots(GetType());
        foreach (var s in slots)
        {
            if ((s.visibility & PropertyVisibility.Deserialize) == 0)
            {
                continue;
            }

            if (!state.values.TryGetValue(s.name, out var raw))
                continue;

            s.setter(this, RestoreValue(raw, s.type));
        }

        InvokeAfterRestoreHooks();
    }

    /// <summary>
    /// Creates an instance for a runtime <see cref="Type"/> that implements <see cref="ISerializable"/>.
    /// </summary>
    /// <param name="runtimeType">The concrete runtime type.</param>
    /// <returns>An instance of <paramref name="runtimeType"/>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="runtimeType"/> is null.</exception>
    /// <exception cref="InvalidCastException">Thrown when <paramref name="runtimeType"/> does not implement <see cref="ISerializable"/>.</exception>
    /// <remarks>
    /// The creation strategy prefers a non-public parameterless constructor; otherwise it falls back to an uninitialized object.
    /// </remarks>
    /// <example>
    /// <code>
    /// var inst = ISerializable.CreateSerializableInstance(typeof(MyComponent));
    /// inst.RestoreState(state);
    /// </code>
    /// </example>
    public static ISerializable CreateSerializableInstance(Type runtimeType)
    {
        if (runtimeType == null) throw new ArgumentNullException(nameof(runtimeType));
        if (!typeof(ISerializable).IsAssignableFrom(runtimeType))
            throw new InvalidCastException($"Type '{runtimeType.FullName}' does not implement {nameof(ISerializable)}.");

        try
        {
            return (ISerializable)Activator.CreateInstance(runtimeType, nonPublic: true)!;
        }
        catch
        {
            return (ISerializable)RuntimeHelpers.GetUninitializedObject(runtimeType);
        }
    }

    #endregion

    #region Internal Validation API

    internal static (string name, Type type)[] GetSlotsForValidation(Type type)
    {
        var slots = GetSlots(type);
        var arr = new (string name, Type type)[slots.Length];

        for (var i = 0; i < slots.Length; i++)
            arr[i] = (slots[i].name, slots[i].type);

        return arr;
    }

    #endregion

    #region Slot Cache

    private sealed record MemberSlot(
        string name,
        Type type,
        Func<object, object?> getter,
        Action<object, object?> setter,
        PropertyVisibility visibility,
        long sortKey);

    private static readonly Lock SLOT_CACHE_SYNC = new();
    private static readonly Dictionary<int, MemberSlot[]> SLOT_CACHE = [];
    private static readonly Lock ACTIVATOR_CACHE_SYNC = new();
    private static readonly Dictionary<int, Func<object>> ACTIVATOR_CACHE = [];

    private static MemberSlot[] GetSlots(Type type)
    {
        int typeId = TypeIdentityRegistry.GetOrAddRuntimeTypeId(type);
        lock (SLOT_CACHE_SYNC)
        {
            if (SLOT_CACHE.TryGetValue(typeId, out MemberSlot[]? slots))
            {
                return slots;
            }

            slots = BuildSlots(type);
            SLOT_CACHE[typeId] = slots;
            return slots;
        }
    }

    // Cache generated order map per declaring type (so BuildSlots is cheap).
    private static readonly Lock DECL_ORDER_CACHE_SYNC = new();
    private static readonly Dictionary<int, IReadOnlyDictionary<string, int>> DECL_ORDER_CACHE = [];

    private static IReadOnlyDictionary<string, int> GetDeclOrderMap(Type declaringType)
    {
        int typeId = TypeIdentityRegistry.GetOrAddRuntimeTypeId(declaringType);
        lock (DECL_ORDER_CACHE_SYNC)
        {
            if (DECL_ORDER_CACHE.TryGetValue(typeId, out IReadOnlyDictionary<string, int>? existing))
            {
                return existing;
            }

            // Try generator registry first
            if (GeneratedOrderRegistry.TryGetOrder(declaringType, out var orderList))
            {
                var map = new Dictionary<string, int>(orderList.Length, StringComparer.Ordinal);
                for (var i = 0; i < orderList.Length; i++)
                {
                    // if duplicates (shouldn't happen), keep first
                    if (!map.ContainsKey(orderList[i]))
                        map[orderList[i]] = i;
                }

                DECL_ORDER_CACHE[typeId] = map;
                return map;
            }

            var empty = new Dictionary<string, int>(0, StringComparer.Ordinal);
            DECL_ORDER_CACHE[typeId] = empty;
            return empty;
        }
    }

    private static int GetDeclOrderIndex(Type declaringType, string memberName)
    {
        var map = GetDeclOrderMap(declaringType);
        return map.TryGetValue(memberName, out var idx) ? idx : int.MaxValue;
    }

    private static MemberSlot[] BuildSlots(Type type)
    {
        const BindingFlags c_declared = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        var chain = new List<Type>(8);
        for (var t = type; t != null && t != typeof(object); t = t.BaseType)
            chain.Add(t);
        chain.Reverse();

        var chainIndex = chain
            .Select((t, i) => (t, i))
            .ToDictionary(x => x.t, x => x.i);

        var list = new List<MemberSlot>(64);

        foreach (var t in chain)
        {
            var depth = chainIndex[t];
            var declMap = GetDeclOrderMap(t);

            // Properties
            foreach (var p in t.GetProperties(c_declared))
            {
                var attr = p.GetCustomAttribute<SerializablePropertyAttribute>(inherit: true);
                if (attr == null)
                    continue;

                if (p.GetIndexParameters().Length != 0)
                    throw new InvalidOperationException($"{type.FullName}.{p.Name} cannot be an indexer to be [SerializableProperty].");

                if (!p.CanRead)
                    throw new InvalidOperationException($"{type.FullName}.{p.Name} must be readable to be [SerializableProperty].");

                var noSetterAllowed = (attr.propertyVisibility & PropertyVisibility.Deserialize) == 0;
                var setMethod = p.GetSetMethod(nonPublic: true);
                Action<object, object?> setter;
                if (setMethod == null)
                {
                    setter = (_, _) => Log.Error($"SerializedProperty {p.Name} has no setter defined.");
                }
                else
                {
                    setter = setMethod.IsPublic
                        ? (obj, value) => p.SetValue(obj, value)
                        : (obj, value) => setMethod.Invoke(obj, [value]);
                }

                if (!noSetterAllowed && setMethod == null)
                    throw new InvalidOperationException($"{type.FullName}.{p.Name} must have setter for its [SerializableProperty].");

                SerializableGraph.ValidateAllowedTypeGraph(p.PropertyType, $"{type.FullName}.{p.Name}");

                var declOrder = declMap.TryGetValue(p.Name, out var idx) ? idx : int.MaxValue;

                // sortKey priority:
                // 1) depth (base -> derived)
                // 2) declaration order within declaring type (field + property mixed)
                // 3) metadata token tie-break (deterministic)
                long sortKey =
                    (((long)depth) << 48) |
                    (((long)(uint)declOrder) << 16) |
                    (uint)p.MetadataToken;

                list.Add(new MemberSlot(
                    p.Name,
                    p.PropertyType,
                    obj => p.GetValue(obj),
                    setter,
                    attr.propertyVisibility,
                    sortKey));
            }

            // Fields
            foreach (var f in t.GetFields(c_declared))
            {
                var attr = f.GetCustomAttribute<SerializablePropertyAttribute>(inherit: true);
                if (attr == null)
                    continue;

                var noSetterAllowed =
                    (attr.propertyVisibility & PropertyVisibility.Deserialize) == 0;

                Action<object, object?> setter;
                if (f.IsInitOnly)
                {
                    setter = (_, _) => Log.Error($"SerializedProperty {f.Name} is initialized only.");
                }
                else
                {
                    setter = f.SetValue;
                }

                if (!noSetterAllowed && f.IsInitOnly)
                    throw new InvalidOperationException($"{type.FullName}.{f.Name} is readonly; it must be writable for its [SerializableProperty].");

                SerializableGraph.ValidateAllowedTypeGraph(f.FieldType, $"{type.FullName}.{f.Name}");

                var declOrder = declMap.TryGetValue(f.Name, out var idx) ? idx : int.MaxValue;

                long sortKey =
                    (((long)depth) << 48) |
                    (((long)(uint)declOrder) << 16) |
                    (uint)f.MetadataToken;

                list.Add(new MemberSlot(
                    f.Name,
                    f.FieldType,
                    obj => f.GetValue(obj),
                    setter,
                    attr.propertyVisibility,
                    sortKey));
            }
        }

        // Same-name resolution: keep the one with greatest sortKey (derived overrides base).
        var latestByName = new Dictionary<string, MemberSlot>(list.Count, StringComparer.Ordinal);
        for (var i = 0; i < list.Count; i++)
        {
            var slot = list[i];
            if (!latestByName.TryGetValue(slot.name, out var existing) || slot.sortKey > existing.sortKey)
                latestByName[slot.name] = slot;
        }

        var result = latestByName.Values.ToArray();
        Array.Sort(result, static (a, b) => a.sortKey.CompareTo(b.sortKey));
        return result;
    }

    #endregion

    #region Value Conversions

    private sealed class SequenceFactory
    {
        public required Type elementType { get; init; }
        public required Func<object?[], object> construct { get; init; }
    }

    private sealed class MapFactory
    {
        public required Type keyType { get; init; }
        public required Type valueType { get; init; }
        public required Func<KeyValuePair<object?, object?>[], object> construct { get; init; }
    }

    private static readonly Lock SEQUENCE_FACTORY_CACHE_SYNC = new();
    private static readonly Dictionary<int, SequenceFactory> SEQUENCE_FACTORY_CACHE = [];
    private static readonly Lock MAP_FACTORY_CACHE_SYNC = new();
    private static readonly Dictionary<int, MapFactory> MAP_FACTORY_CACHE = [];

    private static object? CaptureValue(object? value, Type declaredType)
    {
        if (value == null) return null;

        var t = Nullable.GetUnderlyingType(declaredType) ?? declaredType;

        if (t.IsEnum) return Convert.ToInt64(value);
        if (SerializableGraph.IsAllowedPrimitive(t)) return value;
        if (SerializableGraph.IsSerializingState(t)) return value;

        if (t.IsArray)
        {
            var elemType = t.GetElementType()!;
            var arr = (Array)value;
            var list = new List<object?>(arr.Length);
            for (var i = 0; i < arr.Length; i++)
                list.Add(CaptureValue(arr.GetValue(i), elemType));
            return list;
        }

        if (SerializableGraph.TryGetDictionaryTypes(t, out var keyType, out var valueType))
        {
            if (!SerializableGraph.TryEnumerateDictionaryEntries(value, value.GetType(), out var entries))
            {
                throw new InvalidOperationException(
                    $"CaptureValue: Declared type is map-like ('{t.FullName}') but runtime value '{value.GetType().FullName}' cannot be enumerated as key-value entries.");
            }

            var node = new Dictionary<object, object?>(entries.Count);
            for (var i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                node[CaptureValue(entry.Key, keyType)!] = CaptureValue(entry.Value, valueType);
            }

            return node;
        }

        if (SerializableGraph.TryGetListElementType(t, out var listElem))
        {
            if (value is not IEnumerable enumerable)
            {
                throw new InvalidOperationException(
                    $"CaptureValue: Declared type is sequence-like ('{t.FullName}') but runtime value '{value.GetType().FullName}' is not IEnumerable.");
            }

            var count = TryGetEnumerableCount(value);
            var result = count > 0 ? new List<object?>(count) : new List<object?>();
            foreach (var it in enumerable)
                result.Add(CaptureValue(it, listElem));
            return result;
        }

        if (value is ISerializable s)
        {
            Type runtimeType = s.GetType();
            int runtimeTypeId = TypeIdentityRegistry.GetOrAddRuntimeTypeId(runtimeType);
            return new Dictionary<string, object?>(2, StringComparer.Ordinal)
            {
                ["__runtimeTypeId"] = runtimeTypeId,
                ["__stableTypeId"] = TypeIdentityRegistry.TryGetStableTypeId(runtimeType, out Guid stableTypeId)
                    ? stableTypeId.ToString("D")
                    : null,
                ["data"] = s.CaptureState()
            };
        }

        if (t.IsValueType) return value;

        throw new InvalidOperationException($"Unsupported CaptureValue type: {t.FullName}");
    }

    private static object? RestoreValue(object? raw, Type declaredType)
    {
        var isNullable = Nullable.GetUnderlyingType(declaredType) != null;
        var t = Nullable.GetUnderlyingType(declaredType) ?? declaredType;

        if (raw == null)
        {
            if (!t.IsValueType || isNullable) return null;
            throw new InvalidOperationException($"RestoreValue failed: value-type '{t.FullName}' cannot be null.");
        }

        if (t.IsEnum) return Enum.ToObject(t, Convert.ToInt64(raw));

        if (SerializableGraph.IsAllowedPrimitive(t))
        {
            if (t == typeof(string)) return raw as string;
            if (t == typeof(Guid)) return raw is Guid g ? g : Guid.Parse(raw.ToString() ?? Guid.Empty.ToString());

            if (t == typeof(bool)) return Convert.ToBoolean(raw);
            if (t == typeof(byte)) return Convert.ToByte(raw);
            if (t == typeof(sbyte)) return Convert.ToSByte(raw);
            if (t == typeof(short)) return Convert.ToInt16(raw);
            if (t == typeof(ushort)) return Convert.ToUInt16(raw);
            if (t == typeof(int)) return Convert.ToInt32(raw);
            if (t == typeof(uint)) return Convert.ToUInt32(raw);
            if (t == typeof(long)) return Convert.ToInt64(raw);
            if (t == typeof(ulong)) return Convert.ToUInt64(raw);
            if (t == typeof(float)) return Convert.ToSingle(raw);
            if (t == typeof(double)) return Convert.ToDouble(raw);
            if (t == typeof(decimal)) return Convert.ToDecimal(raw);

            throw new InvalidOperationException($"Unexpected primitive type: {t.FullName}");
        }

        if (SerializableGraph.IsSerializingState(t))
        {
            if (raw is SerializingState ss) return ss;
            throw new InvalidOperationException($"RestoreValue failed: expected SerializingState node for '{t.FullName}', got '{raw.GetType().FullName}'.");
        }

        if (t.IsArray)
        {
            if (raw is not IReadOnlyList<object?> list)
                throw new InvalidOperationException($"Array node must be a list. Got: {raw.GetType().FullName}");

            var elemType = t.GetElementType()!;
            var arr = Array.CreateInstance(elemType, list.Count);
            for (var i = 0; i < list.Count; i++)
                arr.SetValue(RestoreValue(list[i], elemType), i);

            return arr;
        }

        if (SerializableGraph.TryGetDictionaryTypes(t, out _, out _))
        {
            if (!TryReadMapEntries(raw, out var entries))
                throw new InvalidOperationException($"Map node must be IDictionary or key-value sequence. Got: {raw.GetType().FullName}");

            MapFactory factory = GetMapFactory(t);
            var restoredEntries = new KeyValuePair<object?, object?>[entries.Count];
            for (var i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                restoredEntries[i] = new KeyValuePair<object?, object?>(
                    RestoreValue(entry.Key, factory.keyType),
                    RestoreValue(entry.Value, factory.valueType));
            }

            return factory.construct(restoredEntries);
        }

        if (SerializableGraph.TryGetListElementType(t, out _))
        {
            if (!TryReadSequenceItems(raw, out var items))
                throw new InvalidOperationException($"Sequence node must be a list-like value. Got: {raw.GetType().FullName}");

            SequenceFactory factory = GetSequenceFactory(t);
            var restoredItems = new object?[items.Count];
            for (var i = 0; i < items.Count; i++)
                restoredItems[i] = RestoreValue(items[i], factory.elementType);

            return factory.construct(restoredItems);
        }

        if (typeof(ISerializable).IsAssignableFrom(t))
        {
            var wrapper = CoerceToStringKeyDictionary(raw);

            if (!wrapper.TryGetValue("data", out var dataObj) || dataObj is not SerializingState data)
                throw new InvalidOperationException("Serializable wrapper missing 'data' (SerializingState).");

            var runtimeType = t;
            if (wrapper.TryGetValue("__stableTypeId", out var stableTypeObj) &&
                stableTypeObj is string stableTypeText &&
                Guid.TryParse(stableTypeText, out Guid stableTypeId) &&
                TypeIdentityRegistry.TryResolveType(stableTypeId, out Type? stableResolved) &&
                stableResolved is not null &&
                t.IsAssignableFrom(stableResolved))
            {
                runtimeType = stableResolved;
            }
            else if (wrapper.TryGetValue("__runtimeTypeId", out var runtimeTypeObj) &&
                     TryReadRuntimeTypeId(runtimeTypeObj, out int runtimeTypeId) &&
                     TypeIdentityRegistry.TryResolveRuntimeType(runtimeTypeId, out Type? runtimeResolved) &&
                     runtimeResolved is not null &&
                     t.IsAssignableFrom(runtimeResolved))
            {
                runtimeType = runtimeResolved;
            }

            var inst = CreateSerializableInstance(runtimeType);
            inst.RestoreState(data);
            return inst;
        }

        if (t.IsValueType) return raw;

        throw new InvalidOperationException($"Unsupported RestoreValue type: {t.FullName}");
    }

    private static Dictionary<string, object?> CoerceToStringKeyDictionary(object raw)
    {
        if (raw is Dictionary<string, object?> sdict) return sdict;

        if (raw is IDictionary dict)
        {
            var result = new Dictionary<string, object?>(dict.Count, StringComparer.Ordinal);

            foreach (DictionaryEntry e in dict)
            {
                if (e.Key is not string k)
                    throw new InvalidOperationException($"Serializable wrapper dict keys must be strings. Got key type: {e.Key?.GetType().FullName ?? "null"}");

                result[k] = e.Value;
            }

            return result;
        }

        throw new InvalidOperationException($"Serializable wrapper must be a dictionary. Got: {raw.GetType().FullName}");
    }

    private static bool TryReadRuntimeTypeId(object? raw, out int runtimeTypeId)
    {
        switch (raw)
        {
            case int i:
                runtimeTypeId = i;
                return true;
            case long l when l is >= int.MinValue and <= int.MaxValue:
                runtimeTypeId = (int)l;
                return true;
            case string s when int.TryParse(s, out int parsed):
                runtimeTypeId = parsed;
                return true;
            default:
                runtimeTypeId = default;
                return false;
        }
    }

    private static SequenceFactory GetSequenceFactory(Type targetType)
    {
        int typeId = TypeIdentityRegistry.GetOrAddRuntimeTypeId(targetType);
        lock (SEQUENCE_FACTORY_CACHE_SYNC)
        {
            if (SEQUENCE_FACTORY_CACHE.TryGetValue(typeId, out SequenceFactory? existing))
            {
                return existing;
            }

            SequenceFactory created = BuildSequenceFactory(targetType);
            SEQUENCE_FACTORY_CACHE[typeId] = created;
            return created;
        }
    }

    private static MapFactory GetMapFactory(Type targetType)
    {
        int typeId = TypeIdentityRegistry.GetOrAddRuntimeTypeId(targetType);
        lock (MAP_FACTORY_CACHE_SYNC)
        {
            if (MAP_FACTORY_CACHE.TryGetValue(typeId, out MapFactory? existing))
            {
                return existing;
            }

            MapFactory created = BuildMapFactory(targetType);
            MAP_FACTORY_CACHE[typeId] = created;
            return created;
        }
    }

    private static SequenceFactory BuildSequenceFactory(Type targetType)
    {
        if (!SerializableGraph.TryGetListElementType(targetType, out var elementType))
            throw new InvalidOperationException($"Type '{targetType.FullName}' is not a sequence-like type.");

        var listType = typeof(List<>).MakeGenericType(elementType);
        var enumerableType = typeof(IEnumerable<>).MakeGenericType(elementType);

        if (targetType.IsAssignableFrom(listType))
        {
            return new SequenceFactory
            {
                elementType = elementType,
                construct = items => BuildTypedList(elementType, items)
            };
        }

        var constructor = FindSingleArgConstructor(targetType, listType, enumerableType);
        if (constructor != null)
        {
            return new SequenceFactory
            {
                elementType = elementType,
                construct = items => constructor.Invoke(new[] { BuildTypedList(elementType, items) })
            };
        }

        var staticFactory = targetType
            .GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            .FirstOrDefault(m =>
                (m.Name == "CreateRange" || m.Name == "Create") &&
                targetType.IsAssignableFrom(m.ReturnType) &&
                m.GetParameters().Length == 1 &&
                m.GetParameters()[0].ParameterType.IsAssignableFrom(listType));

        if (staticFactory != null)
        {
            return new SequenceFactory
            {
                elementType = elementType,
                construct = items => staticFactory.Invoke(null, new[] { BuildTypedList(elementType, items) })!
            };
        }

        if (!targetType.IsAbstract && ResolveSequenceAddMethod(targetType, elementType) is MethodInfo addMethod)
        {
            return new SequenceFactory
            {
                elementType = elementType,
                construct = items =>
                {
                    var instance = CreateCachedInstance(targetType);
                    for (var i = 0; i < items.Length; i++)
                        addMethod.Invoke(instance, new[] { items[i] });
                    return instance;
                }
            };
        }

        throw new InvalidOperationException(
            $"Cannot construct sequence type '{targetType.FullName}'. Provide ctor(IEnumerable<T>), static CreateRange/Create, or Add(T).");
    }

    private static MapFactory BuildMapFactory(Type targetType)
    {
        if (!SerializableGraph.TryGetDictionaryTypes(targetType, out var keyType, out var valueType))
            throw new InvalidOperationException($"Type '{targetType.FullName}' is not a map-like type.");

        var dictionaryType = typeof(Dictionary<,>).MakeGenericType(keyType, valueType);
        var kvType = typeof(KeyValuePair<,>).MakeGenericType(keyType, valueType);
        var kvListType = typeof(List<>).MakeGenericType(kvType);
        var enumerableType = typeof(IEnumerable<>).MakeGenericType(kvType);

        if (targetType.IsAssignableFrom(dictionaryType))
        {
            return new MapFactory
            {
                keyType = keyType,
                valueType = valueType,
                construct = entries => BuildTypedDictionary(keyType, valueType, entries)
            };
        }

        var constructor = FindSingleArgConstructor(targetType, kvListType, enumerableType);
        if (constructor != null)
        {
            return new MapFactory
            {
                keyType = keyType,
                valueType = valueType,
                construct = entries => constructor.Invoke(new[] { BuildTypedKeyValueList(kvType, entries) })
            };
        }

        var staticFactory = targetType
            .GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            .FirstOrDefault(m =>
                (m.Name == "CreateRange" || m.Name == "Create") &&
                targetType.IsAssignableFrom(m.ReturnType) &&
                m.GetParameters().Length == 1 &&
                m.GetParameters()[0].ParameterType.IsAssignableFrom(kvListType));

        if (staticFactory != null)
        {
            return new MapFactory
            {
                keyType = keyType,
                valueType = valueType,
                construct = entries => staticFactory.Invoke(null, new[] { BuildTypedKeyValueList(kvType, entries) })!
            };
        }

        if (!targetType.IsAbstract && ResolveMapAddMethod(targetType, keyType, valueType) is MethodInfo addMethod)
        {
            return new MapFactory
            {
                keyType = keyType,
                valueType = valueType,
                construct = entries =>
                {
                    var instance = CreateCachedInstance(targetType);
                    for (var i = 0; i < entries.Length; i++)
                    {
                        var entry = entries[i];
                        addMethod.Invoke(instance, new[] { entry.Key, entry.Value });
                    }
                    return instance;
                }
            };
        }

        throw new InvalidOperationException(
            $"Cannot construct map type '{targetType.FullName}'. Provide ctor(IEnumerable<KeyValuePair<K,V>>), static CreateRange/Create, or Add(K,V).");
    }

    private static MethodInfo? ResolveSequenceAddMethod(Type targetType, Type elementType)
    {
        var direct = targetType.GetMethod(
            "Add",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            types: new[] { elementType },
            modifiers: null);
        if (direct != null)
            return direct;

        var collectionIface = typeof(ICollection<>).MakeGenericType(elementType);
        return collectionIface.IsAssignableFrom(targetType)
            ? collectionIface.GetMethod("Add", new[] { elementType })
            : null;
    }

    private static MethodInfo? ResolveMapAddMethod(Type targetType, Type keyType, Type valueType)
    {
        var direct = targetType.GetMethod(
            "Add",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            types: new[] { keyType, valueType },
            modifiers: null);
        if (direct != null)
            return direct;

        var dictionaryIface = typeof(IDictionary<,>).MakeGenericType(keyType, valueType);
        return dictionaryIface.IsAssignableFrom(targetType)
            ? dictionaryIface.GetMethod("Add", new[] { keyType, valueType })
            : null;
    }

    private static object BuildTypedList(Type elementType, object?[] items)
    {
        var listType = typeof(List<>).MakeGenericType(elementType);
        var list = (IList)CreateCachedInstance(listType);
        for (var i = 0; i < items.Length; i++)
            list.Add(items[i]);
        return list;
    }

    private static object BuildTypedDictionary(Type keyType, Type valueType, KeyValuePair<object?, object?>[] entries)
    {
        var dictType = typeof(Dictionary<,>).MakeGenericType(keyType, valueType);
        var dict = (IDictionary)CreateCachedInstance(dictType);
        for (var i = 0; i < entries.Length; i++)
        {
            var entry = entries[i];
            dict.Add(entry.Key!, entry.Value);
        }
        return dict;
    }

    private static object BuildTypedKeyValueList(Type kvType, KeyValuePair<object?, object?>[] entries)
    {
        var listType = typeof(List<>).MakeGenericType(kvType);
        var list = (IList)CreateCachedInstance(listType);

        for (var i = 0; i < entries.Length; i++)
        {
            var entry = entries[i];
            var kv = Activator.CreateInstance(kvType, entry.Key, entry.Value)!;
            list.Add(kv);
        }

        return list;
    }

    private static ConstructorInfo? FindSingleArgConstructor(Type targetType, params Type[] candidateArgTypes)
    {
        var constructors = targetType.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        for (var i = 0; i < constructors.Length; i++)
        {
            var ctor = constructors[i];
            var parameters = ctor.GetParameters();
            if (parameters.Length != 1)
                continue;

            var parameterType = parameters[0].ParameterType;
            for (var c = 0; c < candidateArgTypes.Length; c++)
            {
                if (parameterType.IsAssignableFrom(candidateArgTypes[c]))
                    return ctor;
            }
        }

        return null;
    }

    private static bool TryReadSequenceItems(object raw, out List<object?> items)
    {
        if (raw is IReadOnlyList<object?> roList)
        {
            items = new List<object?>(roList.Count);
            for (var i = 0; i < roList.Count; i++)
                items.Add(roList[i]);
            return true;
        }

        if (raw is IEnumerable enumerable && raw is not string)
        {
            items = new List<object?>();
            foreach (var item in enumerable)
                items.Add(item);
            return true;
        }

        items = new List<object?>();
        return false;
    }

    private static bool TryReadMapEntries(object raw, out List<KeyValuePair<object?, object?>> entries)
    {
        if (raw is IDictionary dict)
        {
            entries = new List<KeyValuePair<object?, object?>>(dict.Count);
            foreach (DictionaryEntry entry in dict)
                entries.Add(new KeyValuePair<object?, object?>(entry.Key, entry.Value));
            return true;
        }

        if (raw is not IEnumerable enumerable)
        {
            entries = new List<KeyValuePair<object?, object?>>();
            return false;
        }

        entries = new List<KeyValuePair<object?, object?>>();
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

    private static object CreateCachedInstance(Type concreteType)
    {
        int typeId = TypeIdentityRegistry.GetOrAddRuntimeTypeId(concreteType);
        Func<object> factory;
        lock (ACTIVATOR_CACHE_SYNC)
        {
            if (!ACTIVATOR_CACHE.TryGetValue(typeId, out factory!))
            {
                factory = BuildActivator(concreteType);
                ACTIVATOR_CACHE[typeId] = factory;
            }
        }

        return factory();
    }

    private static Func<object> BuildActivator(Type concreteType)
    {
        var ctor = concreteType.GetConstructor(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            types: Type.EmptyTypes,
            modifiers: null);
        if (ctor == null)
            return () => Activator.CreateInstance(concreteType, nonPublic: true)!;

        try
        {
            var body = Expression.Convert(Expression.New(ctor), typeof(object));
            return Expression.Lambda<Func<object>>(body).Compile();
        }
        catch
        {
            return () => ctor.Invoke(null)!;
        }
    }

    private static int TryGetEnumerableCount(object value)
    {
        if (value is Array array)
            return array.Length;

        if (value is ICollection collection)
            return collection.Count;

        var roCollection = value.GetType()
            .GetInterfaces()
            .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IReadOnlyCollection<>));
        if (roCollection != null)
            return (int)(roCollection.GetProperty("Count")!.GetValue(value) ?? 0);

        return 0;
    }

    #endregion

    #region Restore Hooks

    private void InvokeAfterRestoreHooks()
    {
        const BindingFlags c_declared = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        var type = GetType();

        var chain = new List<Type>(8);
        for (var t = type; t != null && t != typeof(object); t = t.BaseType)
            chain.Add(t);
        chain.Reverse();

        foreach (var t in chain)
        {
            foreach (var m in t.GetMethods(c_declared))
            {
                if (m.GetCustomAttribute<OnSerializableRestored>(inherit: true) == null)
                    continue;

                if (m.GetParameters().Length != 0)
                    throw new InvalidOperationException($"{type.FullName}.{m.Name} must have 0 parameters.");
                if (m.ReturnType != typeof(void))
                    throw new InvalidOperationException($"{type.FullName}.{m.Name} must return void.");

                m.Invoke(this, null);
            }
        }
    }

    #endregion

    #region Generated Order Registry Bridge (internal, no API exposure)

    /// <summary>
    /// Bridge to source-generated declaration-order registry.
    /// If generator is not referenced, it simply returns false and we fallback to deterministic ordering.
    /// </summary>
    private static class GeneratedOrderRegistry
    {
        // We avoid compile-time dependency on generated types by using reflection once.
        private static readonly Func<Type, (bool ok, string[]? order)> s_tryGetOrder = BuildResolver();

        public static bool TryGetOrder(Type type, out string[] order)
        {
            var (ok, arr) = s_tryGetOrder(type);
            if (ok && arr != null)
            {
                order = arr;
                return true;
            }

            order = Array.Empty<string>();
            return false;
        }

        private static Func<Type, (bool ok, string[]? order)> BuildResolver()
        {
            try
            {
                // Generator emits:
                // internal static class Inno.Core.Serialization.Generated.SerializableDeclOrderRegistry
                // {
                //     internal static bool TryGetOrder(Type t, out string[] order) { ... }
                // }
                var registryType = Type.GetType("Inno.Core.Serialization.Generated.SerializableDeclOrderRegistry");
                if (registryType == null)
                    return _ => (false, null);

                var mi = registryType.GetMethod(
                    "TryGetOrder",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                    binder: null,
                    types: new[] { typeof(Type), typeof(string[]).MakeByRefType() },
                    modifiers: null);

                if (mi == null)
                    return _ => (false, null);

                return t =>
                {
                    object?[] args = { t, null! };
                    var ok = (bool)mi.Invoke(null, args)!;
                    return ok ? (true, (string[])args[1]!) : (false, null);
                };
            }
            catch
            {
                return _ => (false, null);
            }
        }
    }

    #endregion
}
