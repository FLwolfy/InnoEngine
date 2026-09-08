using System;
using System.IO;
using System.Text;

namespace Inno.Editor.Scene;

internal sealed record SceneDocumentHistoryData(
    bool existsBefore,
    bool existsAfter,
    EditorSceneWorkspace.SceneDocumentSnapshot snapshot,
    Guid? activeBefore,
    Guid? activeAfter,
    Guid? selectedBefore,
    Guid? selectedAfter)
{
    internal byte[] Encode()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(existsBefore);
        writer.Write(existsAfter);
        WriteGuid(writer, activeBefore);
        WriteGuid(writer, activeAfter);
        WriteGuid(writer, selectedBefore);
        WriteGuid(writer, selectedAfter);
        WriteSnapshot(writer, snapshot);
        writer.Flush();
        return stream.ToArray();
    }

    internal static SceneDocumentHistoryData Decode(ReadOnlySpan<byte> bytes)
    {
        using var stream = new MemoryStream(bytes.ToArray(), writable: false);
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: false);
        bool existsBefore = reader.ReadBoolean();
        bool existsAfter = reader.ReadBoolean();
        Guid? activeBefore = ReadGuid(reader);
        Guid? activeAfter = ReadGuid(reader);
        Guid? selectedBefore = ReadGuid(reader);
        Guid? selectedAfter = ReadGuid(reader);
        EditorSceneWorkspace.SceneDocumentSnapshot snapshot = ReadSnapshot(reader);
        if (stream.Position != stream.Length)
            throw new InvalidDataException("Scene document history payload contains trailing data.");
        return new SceneDocumentHistoryData(
            existsBefore,
            existsAfter,
            snapshot,
            activeBefore,
            activeAfter,
            selectedBefore,
            selectedAfter);
    }

    private static void WriteSnapshot(
        BinaryWriter writer,
        EditorSceneWorkspace.SceneDocumentSnapshot snapshot)
    {
        writer.Write(snapshot.sceneId.ToByteArray());
        writer.Write(snapshot.payload.Length);
        writer.Write(snapshot.payload);
        writer.Write(snapshot.sourcePath);
        writer.Write(snapshot.sourceAssetId.ToByteArray());
        writer.Write(snapshot.savedHash.Length);
        writer.Write(snapshot.savedHash);
        writer.Write(snapshot.isDirty);
        writer.Write(snapshot.sceneIndex);
    }

    private static EditorSceneWorkspace.SceneDocumentSnapshot ReadSnapshot(BinaryReader reader)
    {
        Guid sceneId = new(reader.ReadBytes(16));
        byte[] payload = ReadBytes(reader, "scene");
        string sourcePath = reader.ReadString();
        Guid sourceAssetId = new(reader.ReadBytes(16));
        byte[] savedHash = ReadBytes(reader, "saved hash");
        bool isDirty = reader.ReadBoolean();
        int sceneIndex = reader.ReadInt32();
        return new EditorSceneWorkspace.SceneDocumentSnapshot(
            sceneId,
            payload,
            sourcePath,
            sourceAssetId,
            savedHash,
            isDirty,
            sceneIndex);
    }

    private static byte[] ReadBytes(BinaryReader reader, string name)
    {
        int length = reader.ReadInt32();
        if (length < 0 || length > reader.BaseStream.Length - reader.BaseStream.Position)
            throw new InvalidDataException($"Scene document history {name} length is invalid.");
        byte[] bytes = reader.ReadBytes(length);
        if (bytes.Length != length)
            throw new EndOfStreamException($"Scene document history {name} is truncated.");
        return bytes;
    }

    private static void WriteGuid(BinaryWriter writer, Guid? value)
    {
        writer.Write(value.HasValue);
        if (value.HasValue)
            writer.Write(value.Value.ToByteArray());
    }

    private static Guid? ReadGuid(BinaryReader reader)
        => reader.ReadBoolean() ? new Guid(reader.ReadBytes(16)) : null;
}
