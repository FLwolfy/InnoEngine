using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Inno.Core.Serialization;

internal static class PropertySnapshotBinaryFormat
{
    private const string C_MAGIC = "INNO-PROPERTY-SNAPSHOT-CURRENT";

    internal static byte[] Encode(IReadOnlyList<SerializationPropertySnapshot> snapshots)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(C_MAGIC);
        writer.Write(snapshots.Count);
        for (int i = 0; i < snapshots.Count; i++)
        {
            SerializationPropertySnapshot snapshot = snapshots[i];
            writer.Write(snapshot.name);
            writer.Write(snapshot.data.Length);
            writer.Write(snapshot.dataSpan);
        }
        writer.Flush();
        return stream.ToArray();
    }

    internal static IReadOnlyList<SerializationPropertySnapshot> Decode(ReadOnlySpan<byte> bytes)
    {
        using var stream = new MemoryStream(bytes.ToArray(), writable: false);
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: false);
        string magic = reader.ReadString();
        if (!string.Equals(magic, C_MAGIC, StringComparison.Ordinal))
            throw new InvalidDataException($"Invalid property snapshot magic '{magic}'.");
        int count = reader.ReadInt32();
        if (count < 0)
            throw new InvalidDataException("Property snapshot count cannot be negative.");
        var snapshots = new SerializationPropertySnapshot[count];
        for (int i = 0; i < count; i++)
        {
            string name = reader.ReadString();
            int length = reader.ReadInt32();
            if (length < 0 || length > stream.Length - stream.Position)
                throw new InvalidDataException($"Property snapshot '{name}' length is invalid.");
            byte[] data = reader.ReadBytes(length);
            if (data.Length != length)
                throw new EndOfStreamException($"Property snapshot '{name}' is truncated.");
            snapshots[i] = new SerializationPropertySnapshot(name, typeof(object), data);
        }
        if (stream.Position != stream.Length)
            throw new InvalidDataException("Property snapshot data contains trailing bytes.");
        return snapshots;
    }
}
