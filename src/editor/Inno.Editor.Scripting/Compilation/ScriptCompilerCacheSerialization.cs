using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Inno.Editor.Scripting;

internal static class ScriptCompilerCacheSerialization
{
    private const int C_DIAGNOSTIC_MAGIC = 0x494E4447;
    private const int C_TYPE_MANIFEST_MAGIC = 0x494E544D;
    private const int C_MAX_ENTRY_COUNT = 1_000_000;

    internal static byte[] EncodeDiagnostics(IEnumerable<ScriptDiagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);

        ScriptDiagnostic[] entries = diagnostics as ScriptDiagnostic[] ?? [.. diagnostics];
        using MemoryStream stream = new();
        using BinaryWriter writer = new(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(C_DIAGNOSTIC_MAGIC);
        writer.Write(entries.Length);
        foreach (ScriptDiagnostic diagnostic in entries)
        {
            writer.Write(diagnostic.id);
            writer.Write((int)diagnostic.severity);
            writer.Write(diagnostic.message);
            writer.Write(diagnostic.filePath is not null);
            if (diagnostic.filePath is not null)
            {
                writer.Write(diagnostic.filePath);
            }

            writer.Write(diagnostic.line);
            writer.Write(diagnostic.column);
        }

        writer.Flush();
        return stream.ToArray();
    }

    internal static ScriptDiagnostic[] DecodeDiagnostics(ReadOnlySpan<byte> bytes)
    {
        using MemoryStream stream = new(bytes.ToArray(), writable: false);
        using BinaryReader reader = new(stream, Encoding.UTF8, leaveOpen: false);
        ValidateMagic(reader, C_DIAGNOSTIC_MAGIC, "script diagnostic cache");
        int count = ReadCount(reader, "diagnostic");
        ScriptDiagnostic[] diagnostics = new ScriptDiagnostic[count];
        for (int index = 0; index < count; index++)
        {
            string id = reader.ReadString();
            ScriptDiagnosticSeverity severity = (ScriptDiagnosticSeverity)reader.ReadInt32();
            string message = reader.ReadString();
            string? filePath = reader.ReadBoolean() ? reader.ReadString() : null;
            int line = reader.ReadInt32();
            int column = reader.ReadInt32();
            diagnostics[index] = new ScriptDiagnostic(id, severity, message, filePath, line, column);
        }

        ValidateEndOfArtifact(stream, "script diagnostic cache");
        return diagnostics;
    }

    internal static byte[] EncodeTypeManifest(ScriptTypeManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        using MemoryStream stream = new();
        using BinaryWriter writer = new(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(C_TYPE_MANIFEST_MAGIC);
        writer.Write(manifest.assemblyName);
        writer.Write(manifest.types.Count);
        foreach (ScriptTypeManifestEntry type in manifest.types)
        {
            writer.Write(type.typeName);
            writer.Write(type.kind);
            writer.Write(type.stableTypeId.ToByteArray());
            writer.Write(type.sourcePersistentId.ToByteArray());
            writer.Write(type.sourcePath);
            writer.Write(type.line);
            writer.Write(type.column);
            writer.Write(type.explicitIdentity);
            writer.Write(type.canonicalSource);
        }

        writer.Flush();
        return stream.ToArray();
    }

    internal static ScriptTypeManifest DecodeTypeManifest(ReadOnlySpan<byte> bytes)
    {
        using MemoryStream stream = new(bytes.ToArray(), writable: false);
        using BinaryReader reader = new(stream, Encoding.UTF8, leaveOpen: false);
        ValidateMagic(reader, C_TYPE_MANIFEST_MAGIC, "script type manifest cache");
        string assemblyName = reader.ReadString();
        int count = ReadCount(reader, "type manifest");
        ScriptTypeManifestEntry[] types = new ScriptTypeManifestEntry[count];
        for (int index = 0; index < count; index++)
        {
            types[index] = new ScriptTypeManifestEntry(
                reader.ReadString(),
                reader.ReadString(),
                ReadGuid(reader, "stable type ID"),
                ReadGuid(reader, "source persistent ID"),
                reader.ReadString(),
                reader.ReadInt32(),
                reader.ReadInt32(),
                reader.ReadBoolean(),
                reader.ReadBoolean());
        }

        ValidateEndOfArtifact(stream, "script type manifest cache");
        return new ScriptTypeManifest(assemblyName, types);
    }

    private static int ReadCount(BinaryReader reader, string entryName)
    {
        int count = reader.ReadInt32();
        if (count < 0 || count > C_MAX_ENTRY_COUNT)
        {
            throw new InvalidDataException($"The {entryName} cache contains an invalid entry count '{count}'.");
        }

        return count;
    }

    private static Guid ReadGuid(BinaryReader reader, string fieldName)
    {
        byte[] bytes = reader.ReadBytes(16);
        if (bytes.Length != 16)
            throw new InvalidDataException($"The script compiler cache truncated its {fieldName}.");
        return new Guid(bytes);
    }

    private static void ValidateMagic(BinaryReader reader, int expectedMagic, string artifactName)
    {
        if (reader.ReadInt32() != expectedMagic)
        {
            throw new InvalidDataException($"The {artifactName} header is invalid.");
        }
    }

    private static void ValidateEndOfArtifact(MemoryStream stream, string artifactName)
    {
        if (stream.Position != stream.Length)
        {
            throw new InvalidDataException($"The {artifactName} contains trailing data.");
        }
    }
}
