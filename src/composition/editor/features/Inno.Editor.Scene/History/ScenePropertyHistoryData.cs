using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace Inno.Editor.Scene;

internal sealed record ScenePropertyValueDelta(
    string propertyName,
    byte[] before,
    byte[] after);

internal sealed record ScenePropertyHistoryData(
    Guid targetId,
    string propertyName,
    ScenePropertyValueDelta[] deltas,
    long timestamp)
{
    internal byte[] Encode()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(targetId.ToByteArray());
        writer.Write(propertyName);
        writer.Write(deltas.Length);
        for (int index = 0; index < deltas.Length; index++)
        {
            ScenePropertyValueDelta delta = deltas[index];
            writer.Write(delta.propertyName);
            WriteBytes(writer, delta.before);
            WriteBytes(writer, delta.after);
        }
        writer.Write(timestamp);
        writer.Flush();
        return stream.ToArray();
    }

    internal static ScenePropertyHistoryData Create(
        Guid targetId,
        string propertyName,
        IReadOnlyList<ScenePropertyValueDelta> deltas)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);
        ArgumentNullException.ThrowIfNull(deltas);
        if (deltas.Count == 0)
            throw new ArgumentException("A scene property history change requires at least one delta.", nameof(deltas));
        return new ScenePropertyHistoryData(
            targetId,
            propertyName,
            [.. deltas],
            Stopwatch.GetTimestamp());
    }

    internal static ScenePropertyHistoryData Decode(ReadOnlySpan<byte> bytes)
    {
        using var stream = new MemoryStream(bytes.ToArray(), writable: false);
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: false);
        Guid targetId = new(reader.ReadBytes(16));
        string propertyName = reader.ReadString();
        int count = reader.ReadInt32();
        if (count <= 0)
            throw new InvalidDataException("Scene property history delta count must be positive.");
        var deltas = new ScenePropertyValueDelta[count];
        var names = new HashSet<string>(StringComparer.Ordinal);
        for (int index = 0; index < count; index++)
        {
            string affectedProperty = reader.ReadString();
            if (string.IsNullOrWhiteSpace(affectedProperty) || !names.Add(affectedProperty))
                throw new InvalidDataException("Scene property history contains an invalid or duplicate property name.");
            deltas[index] = new ScenePropertyValueDelta(
                affectedProperty,
                ReadBytes(reader, $"{affectedProperty} before"),
                ReadBytes(reader, $"{affectedProperty} after"));
        }
        long timestamp = reader.ReadInt64();
        if (stream.Position != stream.Length)
            throw new InvalidDataException("Scene property history payload contains trailing data.");
        return new ScenePropertyHistoryData(targetId, propertyName, deltas, timestamp);
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
