using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Inno.Rendering.Shaders;

// These bytes are a transient hash preimage, not an asset or IR persistence format.
internal static class ShaderIrSemanticHash
{
    internal static string Compute(ShaderIrStage stage)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write((int)stage.stage);
        writer.Write(stage.threadsX);
        writer.Write(stage.threadsY);
        writer.Write(stage.threadsZ);
        writer.Write(stage.inputs.Count);
        foreach (ShaderIrStageInput input in stage.inputs.OrderBy(static value => value.id, StringComparer.Ordinal))
        {
            writer.Write(input.id);
            WriteType(input.type);
            writer.Write((int)input.kind);
            writer.Write(input.semantic);
            writer.Write(input.location);
        }
        writer.Write(stage.outputs.Count);
        foreach (ShaderIrStageOutput output in stage.outputs.OrderBy(static value => value.id, StringComparer.Ordinal))
        {
            writer.Write(output.id);
            writer.Write((int)output.kind);
            writer.Write(output.semantic);
            writer.Write(output.location);
        }
        WriteBlock(stage.body);
        writer.Flush();
        return Convert.ToHexString(SHA256.HashData(stream.GetBuffer().AsSpan(0, checked((int)stream.Length))));

        void WriteBlock(ShaderIrBlock block)
        {
            writer.Write(block.instructions.Count);
            foreach (ShaderIrInstruction instruction in block.instructions)
            {
                writer.Write((int)instruction.operation);
                writer.Write(instruction.inputs.Count);
                foreach (ShaderIrValue input in instruction.inputs) writer.Write(input.index);
                writer.Write(instruction.outputs.Count);
                foreach (ShaderIrValue output in instruction.outputs) { writer.Write(output.index); WriteType(output.type); }
                writer.Write(instruction.inputName ?? string.Empty);
                writer.Write(instruction.constantBits);
                writer.Write(instruction.memberIndex);
                writer.Write(instruction.source?.contentHash ?? string.Empty);
                writer.Write(instruction.regions.Count);
                foreach (ShaderIrBlock region in instruction.regions) WriteBlock(region);
            }
            writer.Write(block.outputs.Count);
            foreach (var output in block.outputs.OrderBy(static value => value.Key, StringComparer.Ordinal))
            { writer.Write(output.Key); writer.Write(output.Value.index); }
        }

        void WriteType(ShaderSourceType type)
        {
            writer.Write(type.id);
            writer.Write(type.elementCount);
            writer.Write(type.elementType is not null);
            if (type.elementType is not null) WriteType(type.elementType);
            writer.Write(type.fields.Count);
            foreach (ShaderSourceField field in type.fields) { writer.Write(field.name); WriteType(field.type); }
            writer.Write(type.storage is not null);
            if (type.storage is ShaderStorageType storage)
            {
                writer.Write((int)storage.access);
                writer.Write(storage.format.HasValue ? (int)storage.format.Value : -1);
                writer.Write((int)storage.dimension);
                writer.Write(storage.array);
                WriteType(storage.valueType);
            }
        }
    }
}
