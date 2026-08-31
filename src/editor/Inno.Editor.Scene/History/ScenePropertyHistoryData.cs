using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace Inno.Editor.Scene;

internal sealed record ScenePropertyHistoryData(
    Guid targetId,
    string propertyName,
    byte[] before,
    byte[] after,
    long timestamp)
{
    internal byte[] Encode()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(targetId.ToByteArray());
        writer.Write(propertyName);
        WriteBytes(writer, before);
        WriteBytes(writer, after);
        writer.Write(timestamp);
        writer.Flush();
        return stream.ToArray();
    }

    internal static ScenePropertyHistoryData Create(
        Guid targetId,
        string propertyName,
        byte[] before,
        byte[] after)
        => new(targetId, propertyName, before, after, Stopwatch.GetTimestamp());

    internal static ScenePropertyHistoryData Decode(ReadOnlySpan<byte> bytes)
    {
        using var stream = new MemoryStream(bytes.ToArray(), writable: false);
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: false);
        Guid targetId = new(reader.ReadBytes(16));
        string propertyName = reader.ReadString();
        byte[] before = ReadBytes(reader, "before");
        byte[] after = ReadBytes(reader, "after");
        long timestamp = reader.ReadInt64();
        if (stream.Position != stream.Length)
            throw new InvalidDataException("Scene property history payload contains trailing data.");
        return new ScenePropertyHistoryData(targetId, propertyName, before, after, timestamp);
    }

    private static void WriteBytes(BinaryWriter writer, byte[] bytes)
    {
        writer.Write(bytes.Length);
        writer.Write(bytes);
    }

    private static byte[] ReadBytes(BinaryReader reader, string name)
    {
        int length = reader.ReadInt32();
        if (length < 0 || length > reader.BaseStream.Length - reader.BaseStream.Position)
            throw new InvalidDataException($"Scene property history {name} value length is invalid.");
        byte[] bytes = reader.ReadBytes(length);
        if (bytes.Length != length)
            throw new EndOfStreamException($"Scene property history {name} value is truncated.");
        return bytes;
    }
}
