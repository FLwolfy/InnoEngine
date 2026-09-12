using System;
using System.Linq;
using System.Text;
using Inno.Rendering;
using Inno.Rendering.Shaders;

namespace Inno.Build.Toolchains.Bgfx.Tools;

internal sealed partial class BgfxShaderIrGenerator
{
    private void EmitStorageDeclaration(StringBuilder text, ShaderIrStageInput input, string name)
    {
        ShaderStorageType storage = input.type.storage!;
        string access = storage.access switch
        {
            RenderStorageAccess.Read => "RO", RenderStorageAccess.Write => "WO", RenderStorageAccess.ReadWrite => "RW",
            _ => throw Error("Unsupported storage access.")
        };
        if (!storage.isImage)
        {
            // BGFX compute buffers use a scalar/vector element contract. Arbitrary host/GLSL/HLSL struct
            // packing cannot be inferred safely; structured layouts need explicit target packing first.
            string element = storage.valueType.id;
            if (storage.valueType.elementType is not null || storage.valueType.fields.Count != 0
                || element is not ("float" or "float2" or "float4" or "int" or "int2" or "int4" or "uint" or "uint2" or "uint4"))
                throw Error("BGFX storage buffer elements require 32-bit scalars, two-component or four-component vectors with an explicit runtime stride.");
            text.Append("BUFFER_").Append(access).Append('(').Append(name).Append(", ")
                .Append(TypeName(storage.valueType)).Append(", ").Append(input.location).AppendLine(");");
            return;
        }
        string shape = storage.dimension == RenderTextureDimension.Texture3D ? "3D" : storage.array ? "2D_ARRAY" : "2D";
        string format = storage.format switch
        {
            RenderTextureFormat.R8 => "r8", RenderTextureFormat.RG8 => "rg8", RenderTextureFormat.RGBA8 => "rgba8",
            RenderTextureFormat.RGBA16Float => "rgba16f", RenderTextureFormat.R32Float => "r32f",
            RenderTextureFormat.RGB10A2 => "rgb10_a2", RenderTextureFormat.RG11B10Float => "r11f_g11f_b10f",
            _ => throw Error("The storage image format has no BGFX SC representation.")
        };
        text.Append("IMAGE").Append(shape).Append('_').Append(access).Append('(').Append(name).Append(", ")
            .Append(format).Append(", ").Append(input.location).AppendLine(");");
    }

    private bool EmitResourceInstruction(ShaderIrInstruction instruction)
    {
        if (instruction.operation is not (ShaderIrOperation.TextureSample or ShaderIrOperation.TextureSampleLevel
            or ShaderIrOperation.StorageLoad or ShaderIrOperation.StorageStore or ShaderIrOperation.StorageAtomicAdd or ShaderIrOperation.Discard)) return false;
        string[] operands = instruction.inputs.Select(Value).ToArray();
        string? result = null;
        if (instruction.outputs.Count != 0)
        {
            ShaderIrValue output = instruction.outputs[0];
            result = NewLocal(output);
            m_body.Append("    ").Append(Declaration(output.type, result)).AppendLine(";");
        }
        switch (instruction.operation)
        {
            case ShaderIrOperation.TextureSample:
            case ShaderIrOperation.TextureSampleLevel:
                string function = instruction.inputs[0].type.id switch
                {
                    "sampled-texture2d" => "texture2D", "sampled-texture2d-array" => "texture2DArray",
                    "sampled-texture3d" => "texture3D", "sampled-texture-cube" => "textureCube",
                    _ => throw Error("Unsupported sampled texture shape.")
                };
                if (instruction.operation == ShaderIrOperation.TextureSampleLevel) function += "Lod";
                Assign(m_body, instruction.outputs[0].type, result!, function + "(" + string.Join(", ", operands) + ")");
                break;
            case ShaderIrOperation.StorageLoad:
                string load = instruction.inputs[0].type.storage!.isImage
                    ? $"imageLoad({operands[0]}, {operands[1]})" : $"{operands[0]}[{operands[1]}]";
                Assign(m_body, instruction.outputs[0].type, result!, load);
                break;
            case ShaderIrOperation.StorageStore:
                if (instruction.inputs[0].type.storage!.isImage)
                    m_body.Append("    imageStore(").AppendJoin(", ", operands).AppendLine(");");
                else Assign(m_body, instruction.inputs[2].type, $"{operands[0]}[{operands[1]}]", operands[2]);
                break;
            case ShaderIrOperation.StorageAtomicAdd:
                m_body.Append("    atomicFetchAndAdd(").Append(operands[0]).Append('[').Append(operands[1]).Append("], ")
                    .Append(operands[2]).Append(", ").Append(result).AppendLine(");");
                break;
            case ShaderIrOperation.Discard:
                m_body.Append("    if (").Append(operands[0]).AppendLine(") { discard; }");
                break;
        }
        return true;
    }
}
