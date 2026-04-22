using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;

using Inno.Core.Serialization;

namespace Inno.Assets.Yaml;

/// <summary>
/// Lossless YAML codec for SerializingState.
/// Guarantees: YAML -> SerializingState reconstructs the same state tree shape,
/// and preserves CLR primitive leaf types by storing ($kind,$type,$value) for each leaf.
/// Does NOT require any changes to SerializingState.
/// </summary>
internal static class SerializingStateYamlCodec
{
    // tagged node keys
    private const string K_KIND   = "$kind";
    private const string K_TYPE   = "$type";
    private const string K_VALUE  = "$value";
    private const string K_ITEMS  = "$items";
    private const string K_FIELDS = "$fields";
    private const string K_K      = "$k";
    private const string K_V      = "$v";

    // node kinds
    private const string KIND_NULL    = "null";
    private const string KIND_PRIM    = "prim";
    private const string KIND_ENUM    = "enum";
    private const string KIND_LIST    = "list";
    private const string KIND_DICT    = "dict";
    private const string KIND_STATE   = "state";
    private const string KIND_STRUCT  = "struct";
    private const string KIND_WRAPPER = "serializableWrapper"; // wrapper: { "__type", "data": SerializingState }

    // wrapper keys (your in-memory shape expected by ISerializable.RestoreValue)
    private const string WRAP_TYPE_KEY = "__type";
    private const string WRAP_DATA_KEY = "data";

    // ------------------------------------------------------------
    // Public API (tree-level)
    // ------------------------------------------------------------

    public static object EncodeState(SerializingState state)
    {
        if (state == null) throw new ArgumentNullException(nameof(state));

        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            [K_KIND]  = KIND_STATE,
            [K_VALUE] = EncodeMap(state.values),
        };
    }

    public static SerializingState DecodeState(object yamlRoot)
    {
        yamlRoot = NormalizeYamlObject(yamlRoot)
                   ?? throw new InvalidOperationException("YAML root is null.");

        if (yamlRoot is not Dictionary<string, object?> root)
            throw new InvalidOperationException("State YAML root must be a mapping.");

        // NOTE: YAML implicit typing can turn "null" into null if unquoted.
        // So we must read $kind as scalar, not string-only.
        string kind = RequireScalarString(root, K_KIND);
        if (!string.Equals(kind, KIND_STATE, StringComparison.Ordinal))
            throw new InvalidOperationException("State YAML root must be a { $kind: state } node.");

        if (!root.TryGetValue(K_VALUE, out var mapObj))
            throw new InvalidOperationException("State node missing $value mapping.");

        mapObj = NormalizeYamlObject(mapObj);

        if (mapObj is not Dictionary<string, object?> map)
            throw new InvalidOperationException("State node $value must be a mapping.");

        return new SerializingState(DecodeMap(map));
    }

    // ------------------------------------------------------------
    // Normalization (YamlDotNet outputs Dictionary<object,object> etc.)
    // ------------------------------------------------------------

    public static object? NormalizeYamlObject(object? node)
    {
        if (node == null) return null;

        if (node is IDictionary dict)
        {
            var m = new Dictionary<string, object?>(dict.Count, StringComparer.Ordinal);
            foreach (DictionaryEntry e in dict)
            {
                if (e.Key is not string key || key.Length == 0)
                    throw new InvalidOperationException("YAML mapping keys must be strings (tagged nodes expected).");

                m[key] = NormalizeYamlObject(e.Value);
            }
            return m;
        }

        if (node is IList list && node is not string)
        {
            var l = new List<object?>(list.Count);
            for (int i = 0; i < list.Count; i++)
                l.Add(NormalizeYamlObject(list[i]));
            return l;
        }

        return node;
    }

    // ------------------------------------------------------------
    // Encode / Decode maps
    // ------------------------------------------------------------

    private static Dictionary<string, object?> EncodeMap(IReadOnlyDictionary<string, object?> map)
    {
        var outMap = new Dictionary<string, object?>(map.Count, StringComparer.Ordinal);
        foreach (var kv in map)
            outMap[kv.Key] = EncodeNode(kv.Value);
        return outMap;
    }

    private static Dictionary<string, object?> DecodeMap(Dictionary<string, object?> map)
    {
        var outMap = new Dictionary<string, object?>(map.Count, StringComparer.Ordinal);
        foreach (var kv in map)
            outMap[kv.Key] = DecodeNode(kv.Value);
        return outMap;
    }

    // ------------------------------------------------------------
    // Encode nodes
    // ------------------------------------------------------------

    private static object EncodeNode(object? v)
    {
        if (v == null)
            return new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                // IMPORTANT:
                // If this is emitted as bare YAML (unquoted), "null" becomes YAML null on load.
                // Decoder is defensive (handles null), but you may also choose to quote strings in serializer.
                [K_KIND] = KIND_NULL
            };

        if (v is SerializingState ss)
        {
            return new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [K_KIND]  = KIND_STATE,
                [K_VALUE] = EncodeMap(ss.values),
            };
        }

        // ----------------------------
        // WRAPPER MUST COME BEFORE IDictionary
        // ----------------------------
        // wrapper shape for ISerializable in your current in-memory state graph:
        // { "__type": "...", "data": SerializingState }
        if (v is Dictionary<string, object?> wrap
            && wrap.TryGetValue(WRAP_TYPE_KEY, out var typeObj) && typeObj is string typeStr
            && wrap.TryGetValue(WRAP_DATA_KEY, out var dataObj) && dataObj is SerializingState dataState)
        {
            return new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [K_KIND]  = KIND_WRAPPER,
                [K_TYPE]  = typeStr,
                [K_VALUE] = EncodeState(dataState),
            };
        }

        var t = v.GetType();

        // enum: preserve runtime enum type + int64
        if (t.IsEnum)
        {
            return new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [K_KIND]  = KIND_ENUM,
                [K_TYPE]  = t.AssemblyQualifiedName ?? t.FullName ?? t.Name,
                [K_VALUE] = Convert.ToInt64(v, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture),
            };
        }

        // primitives: preserve exact CLR primitive type
        if (IsAllowedPrimitive(t))
        {
            return new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [K_KIND]  = KIND_PRIM,
                [K_TYPE]  = t.AssemblyQualifiedName ?? t.FullName ?? t.Name,
                [K_VALUE] = EncodePrimitiveToString(v, t),
            };
        }

        // list: SerializingState containers are List<object?>
        if (v is List<object?> list)
        {
            var items = new List<object?>(list.Count);
            for (int i = 0; i < list.Count; i++)
                items.Add(EncodeNode(list[i]));

            return new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [K_KIND]  = KIND_LIST,
                [K_ITEMS] = items,
            };
        }

        // dictionary: encode as list of pairs to preserve non-string keys losslessly
        if (v is IDictionary dict)
        {
            var items = new List<object?>(dict.Count);
            foreach (DictionaryEntry e in dict)
            {
                items.Add(new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    [K_K] = EncodeNode(e.Key),
                    [K_V] = EncodeNode(e.Value),
                });
            }

            return new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [K_KIND]  = KIND_DICT,
                [K_ITEMS] = items,
            };
        }

        // struct boxed: preserve runtime type + public members (F:/P:) recursively
        if (t.IsValueType && !t.IsEnum)
        {
            var fields = new Dictionary<string, object?>(StringComparer.Ordinal);

            foreach (var f in t.GetFields(BindingFlags.Instance | BindingFlags.Public))
                fields["F:" + f.Name] = EncodeNode(f.GetValue(v));

            foreach (var p in t.GetProperties(BindingFlags.Instance | BindingFlags.Public))
            {
                if (!p.CanRead) continue;
                if (p.GetIndexParameters().Length != 0) continue;
                if (!p.CanWrite) continue; // keep symmetric with your struct selection rules
                fields["P:" + p.Name] = EncodeNode(p.GetValue(v));
            }

            return new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [K_KIND]   = KIND_STRUCT,
                [K_TYPE]   = t.AssemblyQualifiedName ?? t.FullName ?? t.Name,
                [K_FIELDS] = fields,
            };
        }

        throw new InvalidOperationException($"SerializingState YAML cannot encode node type: {t.FullName}");
    }

    // ------------------------------------------------------------
    // Decode nodes
    // ------------------------------------------------------------

    private static object? DecodeNode(object? node)
    {
        node = NormalizeYamlObject(node);

        if (node == null) return null;

        if (node is not Dictionary<string, object?> m)
            throw new InvalidOperationException("Invalid state YAML node (expected tagged mapping with $kind).");

        if (!m.TryGetValue(K_KIND, out var kindObj))
            throw new InvalidOperationException("Invalid state YAML node (expected tagged mapping with $kind).");

        // YAML implicit typing fix:
        // - "$kind: null" -> null
        // - "$value: true" -> bool
        // We always treat these scalars as strings in our codec contract.
        string kind = ScalarToString(kindObj, treatNullAsLiteralNull: true);

        switch (kind)
        {
            case KIND_NULL:
                return null;

            case KIND_PRIM:
            {
                string typeStr = RequireScalarString(m, K_TYPE);
                string valStr  = RequireScalarString(m, K_VALUE);

                var t = Type.GetType(typeStr) ?? throw new InvalidOperationException($"Cannot resolve prim type: {typeStr}");
                return DecodePrimitiveFromString(valStr, t);
            }

            case KIND_ENUM:
            {
                string typeStr = RequireScalarString(m, K_TYPE);
                string valStr  = RequireScalarString(m, K_VALUE);

                var enumType = Type.GetType(typeStr) ?? throw new InvalidOperationException($"Cannot resolve enum type: {typeStr}");
                var n = long.Parse(valStr, CultureInfo.InvariantCulture);
                return Enum.ToObject(enumType, n);
            }

            case KIND_LIST:
            {
                if (!m.TryGetValue(K_ITEMS, out var itemsObj))
                    throw new InvalidOperationException("list node missing $items.");

                itemsObj = NormalizeYamlObject(itemsObj);

                if (itemsObj is not List<object?> items)
                    throw new InvalidOperationException("list node $items must be a list.");

                var list = new List<object?>(items.Count);
                for (int i = 0; i < items.Count; i++)
                    list.Add(DecodeNode(items[i]));
                return list;
            }

            case KIND_DICT:
            {
                if (!m.TryGetValue(K_ITEMS, out var itemsObj))
                    throw new InvalidOperationException("dict node missing $items.");

                itemsObj = NormalizeYamlObject(itemsObj);

                if (itemsObj is not List<object?> items)
                    throw new InvalidOperationException("dict node $items must be a list.");

                var dict = new Dictionary<object?, object?>(items.Count);

                for (int i = 0; i < items.Count; i++)
                {
                    var pairObj = NormalizeYamlObject(items[i]);
                    if (pairObj is not Dictionary<string, object?> pair)
                        throw new InvalidOperationException("dict item must be a mapping with $k/$v.");

                    if (!pair.TryGetValue(K_K, out var kObj))
                        throw new InvalidOperationException("dict item missing $k.");
                    if (!pair.TryGetValue(K_V, out var vObj))
                        throw new InvalidOperationException("dict item missing $v.");

                    var k = DecodeNode(kObj);
                    var v = DecodeNode(vObj);

                    if (k == null)
                        throw new InvalidOperationException("dict item $k decoded to null; dictionary keys cannot be null.");

                    dict[k] = v;
                }

                // ----------------------------
                // Back-compat:
                // Older buggy encoder wrote wrapper dictionaries as KIND_DICT.
                // Detect dict that looks like { "__type": string, "data": SerializingState } and convert.
                // ----------------------------
                if (LooksLikeWrapperDict(dict, out var typeStr, out var dataState))
                {
                    return new Dictionary<string, object?>(2, StringComparer.Ordinal)
                    {
                        [WRAP_TYPE_KEY] = typeStr,
                        [WRAP_DATA_KEY] = dataState
                    };
                }

                return dict;
            }

            case KIND_STATE:
            {
                if (!m.TryGetValue(K_VALUE, out var mapObj))
                    throw new InvalidOperationException("state node missing $value mapping.");

                mapObj = NormalizeYamlObject(mapObj);

                if (mapObj is not Dictionary<string, object?> map)
                    throw new InvalidOperationException("state node $value must be a mapping.");

                return new SerializingState(DecodeMap(map));
            }

            case KIND_WRAPPER:
            {
                string typeStr = RequireScalarString(m, K_TYPE);

                if (!m.TryGetValue(K_VALUE, out var stObj))
                    throw new InvalidOperationException("wrapper node missing $value (state).");

                var decoded = DecodeNode(stObj);
                if (decoded is not SerializingState ss)
                    throw new InvalidOperationException("wrapper $value must decode to SerializingState.");

                // Rebuild the SAME wrapper shape used in your state graph
                return new Dictionary<string, object?>(2, StringComparer.Ordinal)
                {
                    [WRAP_TYPE_KEY] = typeStr,
                    [WRAP_DATA_KEY] = ss
                };
            }

            case KIND_STRUCT:
            {
                string typeStr = RequireScalarString(m, K_TYPE);

                if (!m.TryGetValue(K_FIELDS, out var fieldsObj))
                    throw new InvalidOperationException("struct node missing $fields.");

                fieldsObj = NormalizeYamlObject(fieldsObj);

                if (fieldsObj is not Dictionary<string, object?> fields)
                    throw new InvalidOperationException("struct node $fields must be a mapping.");

                var t = Type.GetType(typeStr) ?? throw new InvalidOperationException($"Cannot resolve struct type: {typeStr}");
                if (!t.IsValueType || t.IsEnum)
                    throw new InvalidOperationException($"struct $type is not a struct: {t.FullName}");

                object boxed = Activator.CreateInstance(t)!;

                foreach (var kv in fields)
                {
                    var key = kv.Key;
                    var decodedVal = DecodeNode(kv.Value);

                    if (key.StartsWith("F:", StringComparison.Ordinal))
                    {
                        var name = key.Substring(2);
                        var fi = t.GetField(name, BindingFlags.Instance | BindingFlags.Public);
                        if (fi != null) fi.SetValue(boxed, decodedVal);
                        continue;
                    }

                    if (key.StartsWith("P:", StringComparison.Ordinal))
                    {
                        var name = key.Substring(2);
                        var pi = t.GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
                        if (pi != null && pi.CanWrite) pi.SetValue(boxed, decodedVal);
                        continue;
                    }
                }

                return boxed;
            }

            default:
                throw new InvalidOperationException($"Unknown state yaml node kind: {kind}");
        }
    }

    private static bool LooksLikeWrapperDict(
        Dictionary<object?, object?> dict,
        out string typeStr,
        out SerializingState dataState)
    {
        typeStr = string.Empty;
        dataState = null!;

        if (dict.Count != 2) return false;

        if (!dict.TryGetValue(WRAP_TYPE_KEY, out var tObj) || tObj is not string ts) return false;
        if (!dict.TryGetValue(WRAP_DATA_KEY, out var dObj) || dObj is not SerializingState ss) return false;

        typeStr = ts;
        dataState = ss;
        return true;
    }

    // ------------------------------------------------------------
    // Scalar helpers (YAML implicit typing)
    // ------------------------------------------------------------

    private static string RequireScalarString(Dictionary<string, object?> m, string key)
    {
        if (!m.TryGetValue(key, out var obj))
            throw new InvalidOperationException($"node missing {key}.");

        // For most keys, null is not allowed; treatNullAsLiteralNull=false
        return ScalarToString(obj, treatNullAsLiteralNull: false);
    }

    private static string ScalarToString(object? obj, bool treatNullAsLiteralNull)
    {
        return obj switch
        {
            string s => s,
            null => treatNullAsLiteralNull ? KIND_NULL : throw new InvalidOperationException("Scalar value is null."),
            bool b => b ? "true" : "false",
            byte v => v.ToString(CultureInfo.InvariantCulture),
            sbyte v => v.ToString(CultureInfo.InvariantCulture),
            short v => v.ToString(CultureInfo.InvariantCulture),
            ushort v => v.ToString(CultureInfo.InvariantCulture),
            int v => v.ToString(CultureInfo.InvariantCulture),
            uint v => v.ToString(CultureInfo.InvariantCulture),
            long v => v.ToString(CultureInfo.InvariantCulture),
            ulong v => v.ToString(CultureInfo.InvariantCulture),
            float v => v.ToString("R", CultureInfo.InvariantCulture),
            double v => v.ToString("R", CultureInfo.InvariantCulture),
            decimal v => v.ToString(CultureInfo.InvariantCulture),
            _ => throw new InvalidOperationException($"Scalar value must be a YAML scalar. Got: {obj.GetType().FullName}")
        };
    }

    // ------------------------------------------------------------
    // Primitive helpers
    // ------------------------------------------------------------

    private static bool IsAllowedPrimitive(Type t)
    {
        return t == typeof(bool)
               || t == typeof(byte) || t == typeof(sbyte)
               || t == typeof(short) || t == typeof(ushort)
               || t == typeof(int) || t == typeof(uint)
               || t == typeof(long) || t == typeof(ulong)
               || t == typeof(float) || t == typeof(double)
               || t == typeof(decimal)
               || t == typeof(string)
               || t == typeof(Guid);
    }

    private static string EncodePrimitiveToString(object v, Type t)
    {
        if (t == typeof(string)) return (string)v;
        if (t == typeof(Guid)) return ((Guid)v).ToString("D");
        if (t == typeof(bool)) return ((bool)v) ? "true" : "false";

        if (t == typeof(byte) || t == typeof(sbyte) ||
            t == typeof(short) || t == typeof(ushort) ||
            t == typeof(int) || t == typeof(uint) ||
            t == typeof(long) || t == typeof(ulong))
            return Convert.ToString(v, CultureInfo.InvariantCulture) ?? "0";

        if (t == typeof(float))  return ((float)v).ToString("R", CultureInfo.InvariantCulture);
        if (t == typeof(double)) return ((double)v).ToString("R", CultureInfo.InvariantCulture);

        if (t == typeof(decimal)) return ((decimal)v).ToString(CultureInfo.InvariantCulture);

        throw new InvalidOperationException($"Unsupported primitive type: {t.FullName}");
    }

    private static object DecodePrimitiveFromString(string s, Type t)
    {
        if (t == typeof(string)) return s;
        if (t == typeof(Guid)) return Guid.Parse(s);
        if (t == typeof(bool)) return string.Equals(s, "true", StringComparison.OrdinalIgnoreCase);

        if (t == typeof(byte)) return byte.Parse(s, NumberStyles.Integer, CultureInfo.InvariantCulture);
        if (t == typeof(sbyte)) return sbyte.Parse(s, NumberStyles.Integer, CultureInfo.InvariantCulture);
        if (t == typeof(short)) return short.Parse(s, NumberStyles.Integer, CultureInfo.InvariantCulture);
        if (t == typeof(ushort)) return ushort.Parse(s, NumberStyles.Integer, CultureInfo.InvariantCulture);
        if (t == typeof(int)) return int.Parse(s, NumberStyles.Integer, CultureInfo.InvariantCulture);
        if (t == typeof(uint)) return uint.Parse(s, NumberStyles.Integer, CultureInfo.InvariantCulture);
        if (t == typeof(long)) return long.Parse(s, NumberStyles.Integer, CultureInfo.InvariantCulture);
        if (t == typeof(ulong)) return ulong.Parse(s, NumberStyles.Integer, CultureInfo.InvariantCulture);

        if (t == typeof(float)) return float.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);
        if (t == typeof(double)) return double.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);
        if (t == typeof(decimal)) return decimal.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);

        throw new InvalidOperationException($"Unsupported primitive type: {t.FullName}");
    }

    // Kept for compatibility with older call sites; prefer RequireScalarString for codec fields.
    private static bool TryGetString(Dictionary<string, object?> m, string key, out string value)
    {
        value = string.Empty;
        if (!m.TryGetValue(key, out var obj) || obj is not string s) return false;
        value = s;
        return true;
    }
}
