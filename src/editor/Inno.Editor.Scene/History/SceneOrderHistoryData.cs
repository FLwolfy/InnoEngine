using System;
using System.IO;

namespace Inno.Editor.Scene;

internal readonly record struct SceneOrderHistoryData(Guid sceneId, int beforeIndex, int afterIndex)
{
    internal byte[] Encode()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(sceneId.ToByteArray());
        writer.Write(beforeIndex);
        writer.Write(afterIndex);
        writer.Flush();
        return stream.ToArray();
    }

    internal static SceneOrderHistoryData Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != 24)
            throw new InvalidDataException("Scene order history payload must contain exactly 24 bytes.");
        return new SceneOrderHistoryData(
            new Guid(bytes[..16]),
            BitConverter.ToInt32(bytes[16..20]),
            BitConverter.ToInt32(bytes[20..24]));
    }
}
