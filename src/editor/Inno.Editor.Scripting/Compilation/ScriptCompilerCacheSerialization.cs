using System;
using System.Collections.Generic;
using System.Linq;
using Inno.Core.Serialization;

namespace Inno.Editor.Scripting;

internal static class ScriptCompilerCacheSerialization
{
    internal static byte[] EncodeDiagnostics(IEnumerable<ScriptDiagnostic> diagnostics)
        => SerializationManager.Encode(writer => writer.WriteObjectArray(
            "diagnostics",
            diagnostics,
            static (item, diagnostic) =>
            {
                item.Write("id", diagnostic.id);
                item.Write("severity", diagnostic.severity);
                item.Write("message", diagnostic.message);
                item.Write("hasFile", diagnostic.filePath is not null);
                item.Write("file", diagnostic.filePath ?? string.Empty);
                item.Write("line", diagnostic.line);
                item.Write("column", diagnostic.column);
            }));

    internal static ScriptDiagnostic[] DecodeDiagnostics(ReadOnlySpan<byte> bytes)
        => SerializationManager.Decode(bytes, reader => reader.ReadObjectArray("diagnostics")
            .Select(item => new ScriptDiagnostic(
                item.Read<string>("id"),
                item.Read<ScriptDiagnosticSeverity>("severity"),
                item.Read<string>("message"),
                item.Read<bool>("hasFile") ? item.Read<string>("file") : null,
                item.Read<int>("line"),
                item.Read<int>("column")))
            .ToArray());

    internal static byte[] EncodeTypeManifest(ScriptTypeManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        return SerializationManager.Encode(writer =>
        {
            writer.Write("assemblyName", manifest.assemblyName);
            writer.WriteObjectArray("types", manifest.types, static (item, type) =>
            {
                item.Write("typeName", type.typeName);
                item.Write("kind", type.kind);
                item.Write("stableTypeId", type.stableTypeId);
                item.Write("sourcePersistentId", type.sourcePersistentId);
                item.Write("sourcePath", type.sourcePath);
                item.Write("line", type.line);
                item.Write("column", type.column);
                item.Write("explicitIdentity", type.explicitIdentity);
                item.Write("canonicalSource", type.canonicalSource);
            });
        });
    }

    internal static ScriptTypeManifest DecodeTypeManifest(ReadOnlySpan<byte> bytes)
        => SerializationManager.Decode(bytes, reader => new ScriptTypeManifest(
            reader.Read<string>("assemblyName"),
            reader.ReadObjectArray("types").Select(item => new ScriptTypeManifestEntry(
                item.Read<string>("typeName"),
                item.Read<string>("kind"),
                item.Read<Guid>("stableTypeId"),
                item.Read<Guid>("sourcePersistentId"),
                item.Read<string>("sourcePath"),
                item.Read<int>("line"),
                item.Read<int>("column"),
                item.Read<bool>("explicitIdentity"),
                item.Read<bool>("canonicalSource")))
                .ToArray()));
}
