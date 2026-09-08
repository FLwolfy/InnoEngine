using System;
using System.IO;
using System.Text;

namespace Inno.Editor.Scene;

internal sealed record SceneSubtreeHistoryData(
    Guid sceneId,
    Guid rootId,
    Guid? parentId,
    int siblingIndex,
    bool existsBefore,
    bool existsAfter,
    byte[] subtree,
    SceneIncomingReferenceState[] incomingReferences,
    Guid? selectedBefore,
    Guid? selectedAfter)
{
    internal byte[] Encode()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(sceneId.ToByteArray());
        writer.Write(rootId.ToByteArray());
        WriteGuid(writer, parentId);
        writer.Write(siblingIndex);
        writer.Write(existsBefore);
        writer.Write(existsAfter);
        WriteBytes(writer, subtree);
        writer.Write(incomingReferences.Length);
        for (int i = 0; i < incomingReferences.Length; i++)
        {
            SceneIncomingReferenceState reference = incomingReferences[i];
            writer.Write(reference.ownerId.ToByteArray());
            writer.Write(reference.propertyName);
            WriteBytes(writer, reference.data);
        }
        WriteGuid(writer, selectedBefore);
        WriteGuid(writer, selectedAfter);
        writer.Flush();
        return stream.ToArray();
    }

    internal static SceneSubtreeHistoryData Decode(ReadOnlySpan<byte> bytes)
    {
        using var stream = new MemoryStream(bytes.ToArray(), writable: false);
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: false);
        Guid sceneId = new(reader.ReadBytes(16));
        Guid rootId = new(reader.ReadBytes(16));
        Guid? parentId = ReadGuid(reader);
        int siblingIndex = reader.ReadInt32();
        bool existsBefore = reader.ReadBoolean();
        bool existsAfter = reader.ReadBoolean();
        byte[] subtree = ReadBytes(reader, "subtree");
        int referenceCount = reader.ReadInt32();
        if (referenceCount < 0)
            throw new InvalidDataException("Scene subtree incoming reference count cannot be negative.");
        var references = new SceneIncomingReferenceState[referenceCount];
        for (int i = 0; i < references.Length; i++)
        {
            references[i] = new SceneIncomingReferenceState(
                new Guid(reader.ReadBytes(16)),
                reader.ReadString(),
                ReadBytes(reader, "incoming reference"));
        }
        Guid? selectedBefore = ReadGuid(reader);
        Guid? selectedAfter = ReadGuid(reader);
        if (stream.Position != stream.Length)
            throw new InvalidDataException("Scene subtree history payload contains trailing data.");
        return new SceneSubtreeHistoryData(
            sceneId,
            rootId,
            parentId,
            siblingIndex,
            existsBefore,
            existsAfter,
            subtree,
            references,
            selectedBefore,
            selectedAfter);
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
            throw new InvalidDataException($"Scene subtree history {name} length is invalid.");
        byte[] bytes = reader.ReadBytes(length);
        if (bytes.Length != length)
            throw new EndOfStreamException($"Scene subtree history {name} is truncated.");
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
