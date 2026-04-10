using System;
using System.Collections.Generic;
using System.Linq;
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
    private static readonly AsyncLocal<HashSet<object>?> s_capturePathContext = new();

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
        HashSet<object>? existingPath = s_capturePathContext.Value;
        bool isRootCapture = existingPath is null;
        HashSet<object> capturePath = existingPath ?? new HashSet<object>(ReferenceObjectComparer.Instance);
        if (isRootCapture)
            s_capturePathContext.Value = capturePath;

        var slots = GetSlots(GetType());
        var node = new Dictionary<string, object?>(slots.Length, StringComparer.Ordinal);
        try
        {
            foreach (var s in slots)
            {
                if ((s.visibility & PropertyVisibility.Serialize) == 0)
                {
                    continue;
                }

                node[s.name] = CaptureValue(s.getter(this), s.type, capturePath);
            }

            return new SerializingState(node);
        }
        finally
        {
            if (isRootCapture)
                s_capturePathContext.Value = null;
        }
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
    private static readonly Lock LOCAL_TYPE_KEY_SYNC = new();
    private static readonly Dictionary<Type, int> LOCAL_TYPE_KEYS = [];
    private static int s_nextLocalTypeKey = int.MinValue;

    private static MemberSlot[] GetSlots(Type type)
    {
        int typeId = GetTypeCacheKey(type);
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
        int typeId = GetTypeCacheKey(declaringType);
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

    private static object? CaptureValue(object? value, Type declaredType, HashSet<object> capturePath)
    {
        if (value == null)
            return null;

        bool tracked = false;
        if (value is not string && !value.GetType().IsValueType)
        {
            if (!capturePath.Add(value))
            {
                throw new InvalidOperationException(
                    $"Cycle reference detected while serializing type '{value.GetType().FullName}'.");
            }

            tracked = true;
        }

        Type normalizedType = Nullable.GetUnderlyingType(declaredType) ?? declaredType;
        try
        {
            if (SerializableManager.TrySerialize(
                    value,
                    normalizedType,
                    (nestedValue, nestedType) => CaptureValue(nestedValue, nestedType, capturePath),
                    out object? node))
                return node;
        }
        finally
        {
            if (tracked)
                capturePath.Remove(value);
        }

        throw new InvalidOperationException($"No serialization codec found for '{normalizedType.FullName}'.");
    }

    private static object? RestoreValue(object? raw, Type declaredType)
    {
        bool isNullable = Nullable.GetUnderlyingType(declaredType) is not null;
        Type normalizedType = Nullable.GetUnderlyingType(declaredType) ?? declaredType;
        if (raw == null)
        {
            if (!normalizedType.IsValueType || isNullable)
                return null;

            throw new InvalidOperationException($"RestoreValue failed: value-type '{normalizedType.FullName}' cannot be null.");
        }

        if (SerializableManager.TryDeserialize(
                raw,
                normalizedType,
                static (nestedNode, nestedType) => RestoreValue(nestedNode, nestedType),
                out object? restored))
            return restored;

        throw new InvalidOperationException($"No deserialization codec found for '{normalizedType.FullName}'.");
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

    private sealed class ReferenceObjectComparer : IEqualityComparer<object>
    {
        internal static ReferenceObjectComparer Instance { get; } = new();

        public new bool Equals(object? x, object? y)
            => ReferenceEquals(x, y);

        public int GetHashCode(object obj)
            => RuntimeHelpers.GetHashCode(obj);
    }

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
