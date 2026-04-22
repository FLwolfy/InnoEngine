using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;

namespace Inno.Assets.Core;

internal static class AssetBinaryCodec
{
    private const uint MAGIC = 0x4E424149u; // "IABN"
    private const ushort VERSION = 1;

    private enum ValueTag : byte
    {
        Bool = 1,
        Int8 = 2,
        UInt8 = 3,
        Int16 = 4,
        UInt16 = 5,
        Int32 = 6,
        UInt32 = 7,
        Int64 = 8,
        UInt64 = 9,
        Float32 = 10,
        Float64 = 11,
        String = 12,
        Guid = 13,
        EnumSigned = 14,
        EnumUnsigned = 15
    }

    internal static byte[] Write(InnoAsset asset, byte[] payload)
    {
        if (asset == null) throw new ArgumentNullException(nameof(asset));
        if (payload == null) throw new ArgumentNullException(nameof(payload));

        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true);

        bw.Write(MAGIC);
        bw.Write(VERSION);

        WriteString(bw, asset.GetType().AssemblyQualifiedName ?? asset.GetType().FullName ?? asset.GetType().Name);

        var members = GetAssetMembers(asset.GetType());
        bw.Write(members.Count);

        for (int i = 0; i < members.Count; i++)
        {
            var m = members[i];
            WriteString(bw, m.name);
            WriteValue(bw, m.GetValue(asset), m.type);
        }

        bw.Write(payload.Length);
        bw.Write(payload);
        bw.Flush();

        return ms.ToArray();
    }

    internal static bool TryRead(ReadOnlySpan<byte> data, out string typeName, out Dictionary<string, object?> properties, out byte[] payload)
    {
        properties = new Dictionary<string, object?>(StringComparer.Ordinal);
        payload = Array.Empty<byte>();
        typeName = string.Empty;

        if (data.Length < 6)
            return false;

        uint magic = BitConverter.ToUInt32(data.Slice(0, 4));
        if (magic != MAGIC)
            return false;

        using var ms = new MemoryStream(data.ToArray(), writable: false);
        using var br = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true);

        _ = br.ReadUInt32(); // magic
        ushort version = br.ReadUInt16();
        if (version != VERSION)
            throw new InvalidDataException($"Unsupported asset bin version: {version}");

        typeName = ReadString(br) ?? throw new InvalidDataException("Asset type name missing.");

        int propCount = br.ReadInt32();
        if (propCount < 0) throw new InvalidDataException("Invalid property count.");

        for (int i = 0; i < propCount; i++)
        {
            string name = ReadString(br) ?? throw new InvalidDataException("Property name missing.");
            object? value = ReadValue(br, out _);
            properties[name] = value;
        }

        int payloadLen = br.ReadInt32();
        if (payloadLen < 0) throw new InvalidDataException("Invalid payload length.");

        payload = br.ReadBytes(payloadLen);
        if (payload.Length != payloadLen)
            throw new EndOfStreamException("Asset payload truncated.");

        return true;
    }

    internal static bool TryReadPayload(ReadOnlySpan<byte> data, out byte[] payload)
    {
        if (!TryRead(data, out _, out _, out payload))
        {
            payload = data.ToArray();
            return false;
        }

        return true;
    }

    internal static InnoAsset ReadAsset(ReadOnlySpan<byte> data)
    {
        if (!TryRead(data, out var typeName, out var props, out var payload))
            throw new InvalidDataException("Asset binary header not found.");

        var type = Type.GetType(typeName, throwOnError: true)
                   ?? throw new InvalidDataException($"Asset type not found: {typeName}");

        var asset = (InnoAsset)RuntimeHelpers.GetUninitializedObject(type);
        ApplyProperties(asset, props);
        asset.assetBinaries = payload;
        return asset;
    }

    private static void ApplyProperties(InnoAsset asset, Dictionary<string, object?> props)
    {
        var members = GetAssetMembers(asset.GetType());
        for (int i = 0; i < members.Count; i++)
        {
            var m = members[i];
            if (!props.TryGetValue(m.name, out var value))
                continue;

            object? converted = ConvertValue(value, m.type);
            m.SetValue(asset, converted);
        }
    }

    private static object? ConvertValue(object? value, Type targetType)
    {
        if (value == null)
            return targetType.IsValueType ? Activator.CreateInstance(targetType) : null;

        if (targetType.IsAssignableFrom(value.GetType()))
            return value;

        if (targetType.IsEnum)
        {
            if (value is ulong ul)
                return Enum.ToObject(targetType, ul);
            if (value is long l)
                return Enum.ToObject(targetType, l);
        }

        return Convert.ChangeType(value, targetType);
    }

    private static void WriteValue(BinaryWriter bw, object? value, Type valueType)
    {
        if (valueType == typeof(bool))
        {
            bw.Write((byte)ValueTag.Bool);
            bw.Write(value != null && (bool)value);
            return;
        }

        if (valueType == typeof(sbyte))
        {
            bw.Write((byte)ValueTag.Int8);
            bw.Write((sbyte)(value ?? default(sbyte)));
            return;
        }

        if (valueType == typeof(byte))
        {
            bw.Write((byte)ValueTag.UInt8);
            bw.Write((byte)(value ?? default(byte)));
            return;
        }

        if (valueType == typeof(short))
        {
            bw.Write((byte)ValueTag.Int16);
            bw.Write((short)(value ?? default(short)));
            return;
        }

        if (valueType == typeof(ushort))
        {
            bw.Write((byte)ValueTag.UInt16);
            bw.Write((ushort)(value ?? default(ushort)));
            return;
        }

        if (valueType == typeof(int))
        {
            bw.Write((byte)ValueTag.Int32);
            bw.Write((int)(value ?? default(int)));
            return;
        }

        if (valueType == typeof(uint))
        {
            bw.Write((byte)ValueTag.UInt32);
            bw.Write((uint)(value ?? default(uint)));
            return;
        }

        if (valueType == typeof(long))
        {
            bw.Write((byte)ValueTag.Int64);
            bw.Write((long)(value ?? default(long)));
            return;
        }

        if (valueType == typeof(ulong))
        {
            bw.Write((byte)ValueTag.UInt64);
            bw.Write((ulong)(value ?? default(ulong)));
            return;
        }

        if (valueType == typeof(float))
        {
            bw.Write((byte)ValueTag.Float32);
            bw.Write((float)(value ?? default(float)));
            return;
        }

        if (valueType == typeof(double))
        {
            bw.Write((byte)ValueTag.Float64);
            bw.Write((double)(value ?? default(double)));
            return;
        }

        if (valueType == typeof(string))
        {
            bw.Write((byte)ValueTag.String);
            WriteString(bw, (string?)value);
            return;
        }

        if (valueType == typeof(Guid))
        {
            bw.Write((byte)ValueTag.Guid);
            var g = value is Guid guid ? guid : Guid.Empty;
            bw.Write(g.ToByteArray());
            return;
        }

        if (valueType.IsEnum)
        {
            var underlying = Enum.GetUnderlyingType(valueType);
            bool isUnsigned = underlying == typeof(byte) || underlying == typeof(ushort) ||
                              underlying == typeof(uint) || underlying == typeof(ulong);

            if (isUnsigned)
            {
                bw.Write((byte)ValueTag.EnumUnsigned);
                bw.Write(Convert.ToUInt64(value));
            }
            else
            {
                bw.Write((byte)ValueTag.EnumSigned);
                bw.Write(Convert.ToInt64(value));
            }
            return;
        }

        throw new NotSupportedException($"Unsupported asset property type: {valueType.FullName}");
    }

    private static object? ReadValue(BinaryReader br, out ValueTag tag)
    {
        tag = (ValueTag)br.ReadByte();
        switch (tag)
        {
            case ValueTag.Bool: return br.ReadBoolean();
            case ValueTag.Int8: return br.ReadSByte();
            case ValueTag.UInt8: return br.ReadByte();
            case ValueTag.Int16: return br.ReadInt16();
            case ValueTag.UInt16: return br.ReadUInt16();
            case ValueTag.Int32: return br.ReadInt32();
            case ValueTag.UInt32: return br.ReadUInt32();
            case ValueTag.Int64: return br.ReadInt64();
            case ValueTag.UInt64: return br.ReadUInt64();
            case ValueTag.Float32: return br.ReadSingle();
            case ValueTag.Float64: return br.ReadDouble();
            case ValueTag.String: return ReadString(br);
            case ValueTag.Guid: return new Guid(br.ReadBytes(16));
            case ValueTag.EnumSigned: return br.ReadInt64();
            case ValueTag.EnumUnsigned: return br.ReadUInt64();
            default: throw new InvalidDataException($"Unknown asset property tag: {tag}");
        }
    }

    private static void WriteString(BinaryWriter bw, string? value)
    {
        if (value == null)
        {
            bw.Write(-1);
            return;
        }

        byte[] bytes = Encoding.UTF8.GetBytes(value);
        bw.Write(bytes.Length);
        bw.Write(bytes);
    }

    private static string? ReadString(BinaryReader br)
    {
        int len = br.ReadInt32();
        if (len < 0) return null;
        if (len == 0) return string.Empty;

        var bytes = br.ReadBytes(len);
        if (bytes.Length != len)
            throw new EndOfStreamException("String truncated.");

        return Encoding.UTF8.GetString(bytes);
    }

    private readonly struct AssetMember
    {
        public readonly string name;
        public readonly Type type;
        private readonly Func<object, object?> m_get;
        private readonly Action<object, object?> m_set;

        public AssetMember(string name, Type type, Func<object, object?> getter, Action<object, object?> setter)
        {
            this.name = name;
            this.type = type;
            m_get = getter;
            m_set = setter;
        }

        public object? GetValue(object instance) => m_get(instance);
        public void SetValue(object instance, object? value) => m_set(instance, value);
    }

    private static List<AssetMember> GetAssetMembers(Type assetType)
    {
        var list = new List<AssetMember>();
        var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        foreach (var prop in assetType.GetProperties(flags))
        {
            if (!prop.IsDefined(typeof(AssetPropertyAttribute), inherit: true)) continue;
            if (prop.GetMethod == null || prop.SetMethod == null) continue;

            string name = prop.Name;
            list.Add(new AssetMember(
                name,
                prop.PropertyType,
                obj => prop.GetValue(obj),
                (obj, val) => prop.SetValue(obj, val)
            ));
        }

        foreach (var field in assetType.GetFields(flags))
        {
            if (!field.IsDefined(typeof(AssetPropertyAttribute), inherit: true)) continue;

            string name = field.Name;
            list.Add(new AssetMember(
                name,
                field.FieldType,
                obj => field.GetValue(obj),
                (obj, val) => field.SetValue(obj, val)
            ));
        }

        return list
            .GroupBy(m => m.name, StringComparer.Ordinal)
            .Select(g => g.First())
            .OrderBy(m => m.name, StringComparer.Ordinal)
            .ToList();
    }
}
