using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Inno.Core.Serialization;

internal static class BinarySerializationFormat
{
    private const string C_MAGIC = "INNO-BINARY-CURRENT";
    private const int C_MAX_COLLECTION_COUNT = 16_777_216;

    internal static byte[] Encode(SerializationNode root)
    {
        using var stream = new MemoryStream(16 * 1024);
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(C_MAGIC);
        WriteNode(writer, root);
        writer.Flush();
        return stream.ToArray();
    }

    internal static SerializationNode Decode(ReadOnlySpan<byte> bytes)
    {
        try
        {
            using var stream = new MemoryStream(bytes.ToArray(), writable: false);
            using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
            string magic = reader.ReadString();
            if (!string.Equals(magic, C_MAGIC, StringComparison.Ordinal))
                throw new InvalidDataException($"Invalid serialization magic '{magic}'.");

            SerializationNode root = ReadNode(reader, "$");
            if (stream.Position != stream.Length)
                throw new InvalidDataException("Serialization payload at '$' contains trailing data.");
            return root;
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception exception) when (exception is EndOfStreamException or IOException or DecoderFallbackException)
        {
            throw new InvalidDataException("Serialization payload is truncated or malformed.", exception);
        }
    }

    private static void WriteNode(BinaryWriter writer, SerializationNode node)
    {
        switch (node)
        {
            case NullSerializationNode:
                writer.Write((byte)NodeKind.Null);
                return;
            case ScalarSerializationNode scalar:
                WriteScalar(writer, scalar.value);
                return;
            case BinarySerializationNode binary:
                writer.Write((byte)NodeKind.Binary);
                writer.Write(binary.value.Length);
                writer.Write(binary.value);
                return;
            case ObjectSerializationNode objectNode:
                writer.Write((byte)NodeKind.Object);
                string[] names = [.. objectNode.values.Keys.OrderBy(static name => name, StringComparer.Ordinal)];
                writer.Write(names.Length);
                for (int i = 0; i < names.Length; i++)
                {
                    writer.Write(names[i]);
                    WriteNode(writer, objectNode.values[names[i]]);
                }
                return;
            case ArraySerializationNode array:
                writer.Write((byte)NodeKind.Array);
                writer.Write(array.values.Count);
                for (int i = 0; i < array.values.Count; i++)
                    WriteNode(writer, array.values[i]);
                return;
            case MapSerializationNode map:
                writer.Write((byte)NodeKind.Map);
                KeyValuePair<SerializationNode, SerializationNode>[] entries = [.. map.values];
                Array.Sort(entries, static (left, right) => CompareBytes(EncodeNode(left.Key), EncodeNode(right.Key)));
                writer.Write(entries.Length);
                for (int i = 0; i < entries.Length; i++)
                {
                    WriteNode(writer, entries[i].Key);
                    WriteNode(writer, entries[i].Value);
                }
                return;
            default:
                throw new InvalidDataException($"Unsupported serialization node '{node.GetType().FullName}'.");
        }
    }

    private static SerializationNode ReadNode(BinaryReader reader, string path)
    {
        NodeKind kind = (NodeKind)reader.ReadByte();
        return kind switch
        {
            NodeKind.Null => NullSerializationNode.instance,
            NodeKind.Boolean => new ScalarSerializationNode(reader.ReadBoolean()),
            NodeKind.Byte => new ScalarSerializationNode(reader.ReadByte()),
            NodeKind.SByte => new ScalarSerializationNode(reader.ReadSByte()),
            NodeKind.Int16 => new ScalarSerializationNode(reader.ReadInt16()),
            NodeKind.UInt16 => new ScalarSerializationNode(reader.ReadUInt16()),
            NodeKind.Int32 => new ScalarSerializationNode(reader.ReadInt32()),
            NodeKind.UInt32 => new ScalarSerializationNode(reader.ReadUInt32()),
            NodeKind.Int64 => new ScalarSerializationNode(reader.ReadInt64()),
            NodeKind.UInt64 => new ScalarSerializationNode(reader.ReadUInt64()),
            NodeKind.Single => new ScalarSerializationNode(reader.ReadSingle()),
            NodeKind.Double => new ScalarSerializationNode(reader.ReadDouble()),
            NodeKind.Decimal => new ScalarSerializationNode(reader.ReadDecimal()),
            NodeKind.String => new ScalarSerializationNode(reader.ReadString()),
            NodeKind.Guid => new ScalarSerializationNode(ReadGuid(reader, path)),
            NodeKind.Binary => new BinarySerializationNode(ReadBytes(reader, path)),
            NodeKind.Object => ReadObject(reader, path),
            NodeKind.Array => ReadArray(reader, path),
            NodeKind.Map => ReadMap(reader, path),
            _ => throw new InvalidDataException($"Unknown serialization node kind '{(byte)kind}' at '{path}'.")
        };
    }

    private static void WriteScalar(BinaryWriter writer, object value)
    {
        switch (value)
        {
            case bool typed: writer.Write((byte)NodeKind.Boolean); writer.Write(typed); return;
            case byte typed: writer.Write((byte)NodeKind.Byte); writer.Write(typed); return;
            case sbyte typed: writer.Write((byte)NodeKind.SByte); writer.Write(typed); return;
            case short typed: writer.Write((byte)NodeKind.Int16); writer.Write(typed); return;
            case ushort typed: writer.Write((byte)NodeKind.UInt16); writer.Write(typed); return;
            case int typed: writer.Write((byte)NodeKind.Int32); writer.Write(typed); return;
            case uint typed: writer.Write((byte)NodeKind.UInt32); writer.Write(typed); return;
            case long typed: writer.Write((byte)NodeKind.Int64); writer.Write(typed); return;
            case ulong typed: writer.Write((byte)NodeKind.UInt64); writer.Write(typed); return;
            case float typed: writer.Write((byte)NodeKind.Single); writer.Write(typed); return;
            case double typed: writer.Write((byte)NodeKind.Double); writer.Write(typed); return;
            case decimal typed: writer.Write((byte)NodeKind.Decimal); writer.Write(typed); return;
            case string typed: writer.Write((byte)NodeKind.String); writer.Write(typed); return;
            case Guid typed: writer.Write((byte)NodeKind.Guid); writer.Write(typed.ToByteArray()); return;
            default:
                throw new InvalidDataException($"Unsupported scalar runtime type '{value.GetType().FullName}'.");
        }
    }

    private static ObjectSerializationNode ReadObject(BinaryReader reader, string path)
    {
        int count = ReadCount(reader, "object member", path);
        var node = new ObjectSerializationNode();
        for (int i = 0; i < count; i++)
        {
            string name = reader.ReadString();
            string memberPath = AppendPath(path, name);
            if (!node.values.TryAdd(name, ReadNode(reader, memberPath)))
                throw new InvalidDataException($"Serialization object at '{path}' contains duplicate key '{name}'.");
        }
        return node;
    }

    private static ArraySerializationNode ReadArray(BinaryReader reader, string path)
    {
        int count = ReadCount(reader, "array element", path);
        var node = new ArraySerializationNode();
        node.values.Capacity = count;
        for (int i = 0; i < count; i++)
            node.values.Add(ReadNode(reader, $"{path}[{i}]"));
        return node;
    }

    private static MapSerializationNode ReadMap(BinaryReader reader, string path)
    {
        int count = ReadCount(reader, "map entry", path);
        var node = new MapSerializationNode();
        node.values.Capacity = count;
        for (int i = 0; i < count; i++)
        {
            node.values.Add(new KeyValuePair<SerializationNode, SerializationNode>(
                ReadNode(reader, $"{path}[{i}].key"),
                ReadNode(reader, $"{path}[{i}].value")));
        }
        return node;
    }

    private static byte[] ReadBytes(BinaryReader reader, string path)
    {
        int count = ReadCount(reader, "binary byte", path);
        byte[] bytes = reader.ReadBytes(count);
        if (bytes.Length != count)
            throw new InvalidDataException($"Serialization binary value at '{path}' is truncated.");
        return bytes;
    }

    private static Guid ReadGuid(BinaryReader reader, string path)
    {
        byte[] bytes = reader.ReadBytes(16);
        if (bytes.Length != 16)
            throw new InvalidDataException($"Serialization Guid value at '{path}' is truncated.");
        return new Guid(bytes);
    }

    private static int ReadCount(BinaryReader reader, string kind, string path)
    {
        int count = reader.ReadInt32();
        if (count < 0 || count > C_MAX_COLLECTION_COUNT)
            throw new InvalidDataException($"Invalid serialization {kind} count {count} at '{path}'.");
        return count;
    }

    private static string AppendPath(string path, string name)
        => path == "$" ? $"$.{name}" : $"{path}.{name}";

    private static byte[] EncodeNode(SerializationNode node)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        WriteNode(writer, node);
        writer.Flush();
        return stream.ToArray();
    }

    private static int CompareBytes(byte[] left, byte[] right)
    {
        int length = Math.Min(left.Length, right.Length);
        for (int i = 0; i < length; i++)
        {
            int comparison = left[i].CompareTo(right[i]);
            if (comparison != 0)
                return comparison;
        }
        return left.Length.CompareTo(right.Length);
    }

    private enum NodeKind : byte
    {
        Null = 0,
        Boolean = 1,
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
        Guid = 14,
        Binary = 15,
        Object = 20,
        Array = 21,
        Map = 22
    }
}
