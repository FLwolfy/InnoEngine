using System;
using System.IO;
using System.Text;

namespace Inno.Editor.Scene;

internal sealed record SceneElementHistoryData(
    SceneElementKind elementKind,
    Guid sceneId,
    Guid ownerId,
    Guid elementId,
    Guid stableTypeId,
    int beforeIndex,
    int afterIndex,
    bool existsBefore,
    bool existsAfter,
    byte[] beforeState,
    byte[] afterState,
    SceneIncomingReferenceState[] incomingReferences)
{
    internal byte[] Encode()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write((byte)elementKind);
        writer.Write(sceneId.ToByteArray());
        writer.Write(ownerId.ToByteArray());
        writer.Write(elementId.ToByteArray());
        writer.Write(stableTypeId.ToByteArray());
        writer.Write(beforeIndex);
        writer.Write(afterIndex);
        writer.Write(existsBefore);
        writer.Write(existsAfter);
        WriteBytes(writer, beforeState);
        WriteBytes(writer, afterState);
        writer.Write(incomingReferences.Length);
        for (int i = 0; i < incomingReferences.Length; i++)
        {
            SceneIncomingReferenceState reference = incomingReferences[i];
            writer.Write(reference.ownerId.ToByteArray());
            writer.Write(reference.propertyName);
            WriteBytes(writer, reference.data);
        }
        writer.Flush();
        return stream.ToArray();
    }

    internal static SceneElementHistoryData Decode(ReadOnlySpan<byte> bytes)
    {
        using var stream = new MemoryStream(bytes.ToArray(), writable: false);
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: false);
        var elementKind = (SceneElementKind)reader.ReadByte();
        if (!Enum.IsDefined(elementKind))
            throw new InvalidDataException($"Unknown scene element history kind '{elementKind}'.");
        Guid sceneId = new(reader.ReadBytes(16));
        Guid ownerId = new(reader.ReadBytes(16));
        Guid elementId = new(reader.ReadBytes(16));
        Guid stableTypeId = new(reader.ReadBytes(16));
        int beforeIndex = reader.ReadInt32();
        int afterIndex = reader.ReadInt32();
        bool existsBefore = reader.ReadBoolean();
        bool existsAfter = reader.ReadBoolean();
        byte[] beforeState = ReadBytes(reader, "before state");
        byte[] afterState = ReadBytes(reader, "after state");
        int referenceCount = reader.ReadInt32();
        if (referenceCount < 0)
            throw new InvalidDataException("Scene element incoming reference count cannot be negative.");
        var references = new SceneIncomingReferenceState[referenceCount];
        for (int i = 0; i < references.Length; i++)
        {
            references[i] = new SceneIncomingReferenceState(
                new Guid(reader.ReadBytes(16)),
                reader.ReadString(),
                ReadBytes(reader, "incoming reference"));
        }
        if (stream.Position != stream.Length)
            throw new InvalidDataException("Scene element history payload contains trailing data.");
        return new SceneElementHistoryData(
            elementKind,
            sceneId,
            ownerId,
            elementId,
            stableTypeId,
            beforeIndex,
            afterIndex,
            existsBefore,
            existsAfter,
            beforeState,
            afterState,
            references);
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
            throw new InvalidDataException($"Scene element history {name} length is invalid.");
        byte[] bytes = reader.ReadBytes(length);
        if (bytes.Length != length)
            throw new EndOfStreamException($"Scene element history {name} is truncated.");
        return bytes;
    }
}
