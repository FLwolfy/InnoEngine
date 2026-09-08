using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace Inno.Editor.Scene;

internal sealed record SceneScalarHistoryData(
    Guid targetId,
    SceneScalarKind scalarKind,
    string before,
    string after,
    long timestamp)
{
    internal byte[] Encode()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(targetId.ToByteArray());
        writer.Write((byte)scalarKind);
        writer.Write(before);
        writer.Write(after);
        writer.Write(timestamp);
        writer.Flush();
        return stream.ToArray();
    }

    internal static SceneScalarHistoryData Create(
        Guid targetId,
        SceneScalarKind scalarKind,
        string before,
        string after)
        => new(targetId, scalarKind, before, after, Stopwatch.GetTimestamp());

    internal static SceneScalarHistoryData Decode(ReadOnlySpan<byte> bytes)
    {
        using var stream = new MemoryStream(bytes.ToArray(), writable: false);
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: false);
        Guid targetId = new(reader.ReadBytes(16));
        var scalarKind = (SceneScalarKind)reader.ReadByte();
        if (!Enum.IsDefined(scalarKind))
            throw new InvalidDataException($"Unknown scene scalar history kind '{scalarKind}'.");
        string before = reader.ReadString();
        string after = reader.ReadString();
        long timestamp = reader.ReadInt64();
        if (stream.Position != stream.Length)
            throw new InvalidDataException("Scene scalar history payload contains trailing data.");
        return new SceneScalarHistoryData(targetId, scalarKind, before, after, timestamp);
    }
}
