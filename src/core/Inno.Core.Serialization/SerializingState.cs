using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Inno.Core.Serialization;

/// <summary>
/// Represents a deterministic, serializable state tree used by the engine serialization layer.
/// </summary>
public sealed record SerializingState
{
    #region Constants

    private const string INNO_MAGIC_HEADER = "INNO";
    private const int SERIALIZATION_VERSION = 1;

    #endregion

    #region Public State

    /// <summary>
    /// Gets the root value map of this state.
    /// </summary>
    public IReadOnlyDictionary<string, object?> values { get; }

    #endregion

    #region Construction

    /// <summary>
    /// Initializes a new <see cref="SerializingState"/> instance.
    /// </summary>
    /// <param name="values">The root key/value map.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="values"/> is null.</exception>
    public SerializingState(IReadOnlyDictionary<string, object?> values)
    {
        this.values = values ?? throw new ArgumentNullException(nameof(values));
    }

    #endregion

    #region Public Queries

    /// <summary>
    /// Tests whether a key exists in the root map.
    /// </summary>
    /// <param name="key">The key to look up.</param>
    /// <returns><see langword="true"/> if the key exists; otherwise <see langword="false"/>.</returns>
    public bool Contains(string key) => values.ContainsKey(key);

    /// <summary>
    /// Attempts to get a value from the root map.
    /// </summary>
    /// <param name="key">The key to look up.</param>
    /// <param name="value">The resolved value.</param>
    /// <returns><see langword="true"/> if found; otherwise <see langword="false"/>.</returns>
    public bool TryGetValue(string key, out object? value) => values.TryGetValue(key, out value);

    /// <summary>
    /// Gets a typed value from the root map.
    /// </summary>
    /// <typeparam name="T">The expected value type.</typeparam>
    /// <param name="key">The key to look up.</param>
    /// <returns>The value cast to <typeparamref name="T"/>.</returns>
    /// <exception cref="KeyNotFoundException">Thrown when the key is missing.</exception>
    /// <exception cref="InvalidCastException">Thrown when the stored value is not assignable to <typeparamref name="T"/>.</exception>
    /// <example>
    /// <code>
    /// var sceneName = state.GetValue&lt;string&gt;("sceneName");
    /// </code>
    /// </example>
    public T GetValue<T>(string key)
    {
        if (!values.TryGetValue(key, out var v))
            throw new KeyNotFoundException(key);

        if (v is T t)
            return t;

        throw new InvalidCastException($"State value '{key}' is {v?.GetType().FullName}, expected {typeof(T).FullName}");
    }

    #endregion

    #region Public Binary API

    /// <summary>
    /// Serializes a state into a deterministic binary representation.
    /// </summary>
    /// <param name="state">The state to serialize.</param>
    /// <returns>The encoded bytes.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="state"/> is null.</exception>
    /// <example>
    /// <code>
    /// var bytes = SerializingState.Serialize(state);
    /// File.WriteAllBytes(path, bytes);
    /// </code>
    /// </example>
    public static byte[] Serialize(SerializingState state)
    {
        if (state == null) throw new ArgumentNullException(nameof(state));

        using var ms = new MemoryStream(16 * 1024);
        using var bw = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true);

        bw.Write(INNO_MAGIC_HEADER);
        bw.Write(SERIALIZATION_VERSION);

        BinaryCodec.WriteState(bw, state);

        bw.Flush();
        return ms.ToArray();
    }

    /// <summary>
    /// Deserializes a state from a binary representation.
    /// </summary>
    /// <param name="bytes">The encoded bytes.</param>
    /// <returns>The decoded state.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="bytes"/> is null.</exception>
    /// <exception cref="InvalidDataException">Thrown when the data is invalid or the version is unsupported.</exception>
    /// <example>
    /// <code>
    /// var bytes = File.ReadAllBytes(path);
    /// var state = SerializingState.Deserialize(bytes);
    /// </code>
    /// </example>
    public static SerializingState Deserialize(byte[] bytes)
    {
        if (bytes == null) throw new ArgumentNullException(nameof(bytes));

        using var ms = new MemoryStream(bytes, writable: false);
        using var br = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true);

        var magic = br.ReadString();
        if (!string.Equals(magic, INNO_MAGIC_HEADER, StringComparison.Ordinal))
            throw new InvalidDataException($"Invalid state magic header '{magic}'.");

        var ver = br.ReadInt32();
        if (ver != SERIALIZATION_VERSION)
            throw new InvalidDataException($"Unsupported state serialization version {ver} (expected {SERIALIZATION_VERSION}).");

        return BinaryCodec.ReadState(br);
    }

    #endregion

    #region Binary Codec

    private static class BinaryCodec
    {
        private enum BinKind : byte
        {
            Null = 0,
            Primitive = 1,
            Enum = 2,
            Array = 10,
            List = 11,
            Dict = 12,
            State = 20,
            Serializable = 21,
            Struct = 22
        }

        private enum PrimKind : byte
        {
            Bool = 1,
            Byte = 2,
            SByte = 3,
            Int16 = 4,
            UInt16 = 5,
            Int32 = 6,
            UInt32 = 7,
            Int64 = 8,
            UInt64 = 9,
            Single = 10,
            Double = 11,
            Decimal = 12,
            String = 13,
            Guid = 14
        }

        internal static void WriteState(BinaryWriter bw, SerializingState state)
        {
            bw.Write((byte)BinKind.State);

            var keys = state.values.Keys.ToList();
            keys.Sort(StringComparer.Ordinal);

            bw.Write(keys.Count);
            for (var i = 0; i < keys.Count; i++)
            {
                var k = keys[i];
                bw.Write(k);
                state.values.TryGetValue(k, out var v);
                WriteNode(bw, v);
            }
        }

        internal static SerializingState ReadState(BinaryReader br)
        {
            var kind = (BinKind)br.ReadByte();
            if (kind != BinKind.State)
                throw new InvalidDataException($"Expected State node, got {kind}.");

            var count = br.ReadInt32();
            var map = new Dictionary<string, object?>(System.Math.Max(0, count), StringComparer.Ordinal);

            for (var i = 0; i < count; i++)
                map[br.ReadString()] = ReadNode(br);

            return new SerializingState(map);
        }

        private static void WriteNode(BinaryWriter bw, object? value)
        {
            if (value == null)
            {
                bw.Write((byte)BinKind.Null);
                return;
            }

            if (value is SerializingState state)
            {
                WriteState(bw, state);
                return;
            }

            var t = value.GetType();

            if (t.IsEnum)
            {
                bw.Write((byte)BinKind.Enum);
                bw.Write(t.AssemblyQualifiedName ?? t.FullName ?? t.Name);
                bw.Write(Convert.ToInt64(value));
                return;
            }

            if (SerializableGraph.IsAllowedPrimitive(t))
            {
                bw.Write((byte)BinKind.Primitive);
                WritePrimitive(bw, value, t);
                return;
            }

            if (t.IsArray)
            {
                var arr = (Array)value;
                bw.Write((byte)BinKind.Array);
                bw.Write(arr.Length);
                for (var i = 0; i < arr.Length; i++)
                    WriteNode(bw, arr.GetValue(i));
                return;
            }

            if (value is IDictionary dict)
            {
                bw.Write((byte)BinKind.Dict);

                var entries = new List<DictionaryEntry>(dict.Count);
                foreach (DictionaryEntry e in dict) entries.Add(e);

                var allStringKey = entries.All(e => e.Key is string);
                if (allStringKey)
                    entries.Sort((a, b) => StringComparer.Ordinal.Compare((string)a.Key, (string)b.Key));

                bw.Write(entries.Count);
                for (var i = 0; i < entries.Count; i++)
                {
                    WriteNode(bw, entries[i].Key);
                    WriteNode(bw, entries[i].Value);
                }

                return;
            }

            if (value is IEnumerable en && value is not string)
            {
                var tmp = new List<object?>();
                foreach (var it in en) tmp.Add(it);

                bw.Write((byte)BinKind.List);
                bw.Write(tmp.Count);
                for (var i = 0; i < tmp.Count; i++)
                    WriteNode(bw, tmp[i]);

                return;
            }

            if (value is ISerializable ser)
            {
                bw.Write((byte)BinKind.Serializable);
                var rt = ser.GetType();
                bw.Write(rt.AssemblyQualifiedName ?? rt.FullName ?? rt.Name);

                SerializableGraph.ValidateAllowedTypeGraph(rt, $"BinaryWrite({rt.FullName})");

                WriteState(bw, ser.CaptureState());
                return;
            }

            if (t.IsValueType)
            {
                SerializableGraph.ValidateAllowedTypeGraph(t, $"BinaryWrite({t.FullName})");

                bw.Write((byte)BinKind.Struct);
                bw.Write(t.AssemblyQualifiedName ?? t.FullName ?? t.Name);

                var fields = SerializableGraph.GetStructSerializableFields(t);
                var props = SerializableGraph.GetStructSerializableProperties(t);

                bw.Write(fields.Length + props.Length);

                for (var i = 0; i < fields.Length; i++)
                {
                    var f = fields[i];
                    bw.Write("F:" + f.Name);
                    WriteNode(bw, f.GetValue(value));
                }

                for (var i = 0; i < props.Length; i++)
                {
                    var p = props[i];
                    bw.Write("P:" + p.Name);
                    WriteNode(bw, p.GetValue(value));
                }

                return;
            }

            throw new InvalidDataException($"Unsupported node runtime type: {t.FullName}");
        }

        private static object? ReadNode(BinaryReader br)
        {
            var kind = (BinKind)br.ReadByte();
            switch (kind)
            {
                case BinKind.Null:
                    return null;

                case BinKind.Primitive:
                    return ReadPrimitive(br);

                case BinKind.Enum:
                {
                    var enumTypeName = br.ReadString();
                    var enumType = Type.GetType(enumTypeName)
                                   ?? throw new InvalidDataException($"Could not resolve enum type '{enumTypeName}'.");
                    return Enum.ToObject(enumType, br.ReadInt64());
                }

                case BinKind.Array:
                {
                    var len = br.ReadInt32();
                    var arr = new object?[len];
                    for (var i = 0; i < len; i++)
                        arr[i] = ReadNode(br);
                    return arr;
                }

                case BinKind.List:
                {
                    var count = br.ReadInt32();
                    var list = new List<object?>(System.Math.Max(0, count));
                    for (var i = 0; i < count; i++)
                        list.Add(ReadNode(br));
                    return list;
                }

                case BinKind.Dict:
                {
                    var count = br.ReadInt32();
                    var map = new Dictionary<object?, object?>(System.Math.Max(0, count));
                    for (var i = 0; i < count; i++)
                        map[ReadNode(br)] = ReadNode(br);
                    return map;
                }

                case BinKind.State:
                    return ReadStateAfterKind(br);

                case BinKind.Serializable:
                {
                    var typeName = br.ReadString();
                    var rt = Type.GetType(typeName)
                             ?? throw new InvalidDataException($"Could not resolve ISerializable type '{typeName}'.");

                    if (!typeof(ISerializable).IsAssignableFrom(rt))
                        throw new InvalidDataException($"Type '{rt.FullName}' is not ISerializable.");

                    SerializableGraph.ValidateAllowedTypeGraph(rt, $"BinaryRead({rt.FullName})");

                    var state = ReadState(br);
                    var inst = ISerializable.CreateSerializableInstance(rt);
                    inst.RestoreState(state);
                    return inst;
                }

                case BinKind.Struct:
                    return ReadStruct(br);

                default:
                    throw new InvalidDataException($"Unknown binary node kind: {kind}");
            }
        }

        private static SerializingState ReadStateAfterKind(BinaryReader br)
        {
            var count = br.ReadInt32();
            var map = new Dictionary<string, object?>(System.Math.Max(0, count), StringComparer.Ordinal);

            for (var i = 0; i < count; i++)
                map[br.ReadString()] = ReadNode(br);

            return new SerializingState(map);
        }

        private static object ReadStruct(BinaryReader br)
        {
            var typeName = br.ReadString();
            var t = Type.GetType(typeName)
                    ?? throw new InvalidDataException($"Could not resolve struct type '{typeName}'.");

            if (!t.IsValueType || t.IsEnum)
                throw new InvalidDataException($"Type '{t.FullName}' is not a struct value-type.");

            SerializableGraph.ValidateAllowedTypeGraph(t, $"BinaryRead({t.FullName})");

            object boxed = Activator.CreateInstance(t)!;

            var memberCount = br.ReadInt32();

            var fieldMap = SerializableGraph.GetStructSerializableFields(t)
                .ToDictionary(f => "F:" + f.Name, f => f, StringComparer.Ordinal);

            var propMap = SerializableGraph.GetStructSerializableProperties(t)
                .ToDictionary(p => "P:" + p.Name, p => p, StringComparer.Ordinal);

            for (var i = 0; i < memberCount; i++)
            {
                var key = br.ReadString();
                var val = ReadNode(br);

                if (fieldMap.TryGetValue(key, out var fi))
                {
                    var vis = SerializableGraph.GetVisibilityOrShow(fi);
                    if ((vis & PropertyVisibility.Deserialize) != 0 && !fi.IsInitOnly)
                        fi.SetValue(boxed, val);
                    continue;
                }

                if (propMap.TryGetValue(key, out var pi))
                {
                    var vis = SerializableGraph.GetVisibilityOrShow(pi);
                    if ((vis & PropertyVisibility.Deserialize) != 0 && pi.CanWrite)
                        pi.SetValue(boxed, val);
                    continue;
                }
            }

            return boxed;
        }

        private static void WritePrimitive(BinaryWriter bw, object value, Type t)
        {
            if (t == typeof(bool)) { bw.Write((byte)PrimKind.Bool); bw.Write((bool)value); return; }
            if (t == typeof(byte)) { bw.Write((byte)PrimKind.Byte); bw.Write((byte)value); return; }
            if (t == typeof(sbyte)) { bw.Write((byte)PrimKind.SByte); bw.Write((sbyte)value); return; }
            if (t == typeof(short)) { bw.Write((byte)PrimKind.Int16); bw.Write((short)value); return; }
            if (t == typeof(ushort)) { bw.Write((byte)PrimKind.UInt16); bw.Write((ushort)value); return; }
            if (t == typeof(int)) { bw.Write((byte)PrimKind.Int32); bw.Write((int)value); return; }
            if (t == typeof(uint)) { bw.Write((byte)PrimKind.UInt32); bw.Write((uint)value); return; }
            if (t == typeof(long)) { bw.Write((byte)PrimKind.Int64); bw.Write((long)value); return; }
            if (t == typeof(ulong)) { bw.Write((byte)PrimKind.UInt64); bw.Write((ulong)value); return; }
            if (t == typeof(float)) { bw.Write((byte)PrimKind.Single); bw.Write((float)value); return; }
            if (t == typeof(double)) { bw.Write((byte)PrimKind.Double); bw.Write((double)value); return; }
            if (t == typeof(decimal)) { bw.Write((byte)PrimKind.Decimal); bw.Write((decimal)value); return; }
            if (t == typeof(string)) { bw.Write((byte)PrimKind.String); bw.Write((string)value); return; }
            if (t == typeof(Guid))
            {
                bw.Write((byte)PrimKind.Guid);
                bw.Write(((Guid)value).ToByteArray());
                return;
            }

            throw new InvalidDataException($"Unsupported primitive type: {t.FullName}");
        }

        private static object ReadPrimitive(BinaryReader br)
        {
            var pk = (PrimKind)br.ReadByte();
            return pk switch
            {
                PrimKind.Bool => br.ReadBoolean(),
                PrimKind.Byte => br.ReadByte(),
                PrimKind.SByte => br.ReadSByte(),
                PrimKind.Int16 => br.ReadInt16(),
                PrimKind.UInt16 => br.ReadUInt16(),
                PrimKind.Int32 => br.ReadInt32(),
                PrimKind.UInt32 => br.ReadUInt32(),
                PrimKind.Int64 => br.ReadInt64(),
                PrimKind.UInt64 => br.ReadUInt64(),
                PrimKind.Single => br.ReadSingle(),
                PrimKind.Double => br.ReadDouble(),
                PrimKind.Decimal => br.ReadDecimal(),
                PrimKind.String => br.ReadString(),
                PrimKind.Guid => new Guid(br.ReadBytes(16)),
                _ => throw new InvalidDataException($"Unknown primitive kind: {pk}")
            };
        }
    }

    #endregion
}
