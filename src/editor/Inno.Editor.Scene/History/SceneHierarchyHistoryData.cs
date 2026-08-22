using System;
using System.IO;

namespace Inno.Editor.Scene;

internal readonly record struct SceneObjectPlacement(
    Guid sceneId,
    Guid objectId,
    Guid? parentId,
    int siblingIndex);

internal sealed record SceneHierarchyHistoryData(
    SceneObjectPlacement[] before,
    SceneObjectPlacement[] after,
    Guid selectedId)
{
    internal byte[] Encode()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(selectedId.ToByteArray());
        WritePlacements(writer, before);
        WritePlacements(writer, after);
        writer.Flush();
        return stream.ToArray();
    }

    internal static SceneHierarchyHistoryData Decode(ReadOnlySpan<byte> bytes)
    {
        using var stream = new MemoryStream(bytes.ToArray(), writable: false);
        using var reader = new BinaryReader(stream);
        Guid selectedId = new(reader.ReadBytes(16));
        SceneObjectPlacement[] before = ReadPlacements(reader);
        SceneObjectPlacement[] after = ReadPlacements(reader);
        if (stream.Position != stream.Length)
            throw new InvalidDataException("Scene hierarchy history payload contains trailing data.");
        return new SceneHierarchyHistoryData(before, after, selectedId);
    }

    private static void WritePlacements(BinaryWriter writer, SceneObjectPlacement[] placements)
    {
        writer.Write(placements.Length);
        for (int i = 0; i < placements.Length; i++)
        {
            SceneObjectPlacement placement = placements[i];
            writer.Write(placement.sceneId.ToByteArray());
            writer.Write(placement.objectId.ToByteArray());
            writer.Write(placement.parentId.HasValue);
            if (placement.parentId.HasValue)
                writer.Write(placement.parentId.Value.ToByteArray());
            writer.Write(placement.siblingIndex);
        }
    }

    private static SceneObjectPlacement[] ReadPlacements(BinaryReader reader)
    {
        int count = reader.ReadInt32();
        if (count < 0)
            throw new InvalidDataException("Scene hierarchy placement count cannot be negative.");
        var placements = new SceneObjectPlacement[count];
        for (int i = 0; i < placements.Length; i++)
        {
            Guid sceneId = new(reader.ReadBytes(16));
            Guid objectId = new(reader.ReadBytes(16));
            Guid? parentId = reader.ReadBoolean() ? new Guid(reader.ReadBytes(16)) : null;
            placements[i] = new SceneObjectPlacement(sceneId, objectId, parentId, reader.ReadInt32());
        }
        return placements;
    }
}
