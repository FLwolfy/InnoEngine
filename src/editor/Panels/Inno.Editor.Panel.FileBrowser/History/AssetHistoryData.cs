using System;
using System.IO;
using System.Text;

namespace Inno.Editor.Panel.FileBrowser;

internal sealed record AssetHistoryData(
    AssetHistoryOperationKind operationKind,
    string sourcePath,
    string targetPath,
    bool isDirectory,
    byte[] archive)
{
    internal byte[] Encode()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write((byte)operationKind);
        writer.Write(sourcePath);
        writer.Write(targetPath);
        writer.Write(isDirectory);
        writer.Write(archive.Length);
        writer.Write(archive);
        writer.Flush();
        return stream.ToArray();
    }

    internal static AssetHistoryData Decode(ReadOnlySpan<byte> bytes)
    {
        using var stream = new MemoryStream(bytes.ToArray(), writable: false);
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: false);
        var kind = (AssetHistoryOperationKind)reader.ReadByte();
        if (!Enum.IsDefined(kind))
            throw new InvalidDataException($"Unknown asset history operation kind '{kind}'.");
        string sourcePath = reader.ReadString();
        string targetPath = reader.ReadString();
        bool isDirectory = reader.ReadBoolean();
        int archiveLength = reader.ReadInt32();
        if (archiveLength < 0 || archiveLength > stream.Length - stream.Position)
            throw new InvalidDataException("Asset history archive length is invalid.");
        byte[] archive = reader.ReadBytes(archiveLength);
        if (archive.Length != archiveLength)
            throw new EndOfStreamException("Asset history archive is truncated.");
        if (stream.Position != stream.Length)
            throw new InvalidDataException("Asset history payload contains trailing data.");
        return new AssetHistoryData(kind, sourcePath, targetPath, isDirectory, archive);
    }
}
