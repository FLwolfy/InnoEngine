using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Inno.Rendering;

/// <summary>
/// Encodes and validates the strict source-free binary format shared by game builds and Players.
/// </summary>
public static class RenderShaderArtifactCodec
{
    private const int C_MAX_BINDINGS = 4096;
    private const int C_MAX_PASSES = 1024;
    private const int C_MAX_STAGE_BYTES = 128 * 1024 * 1024;
    private const int C_MAX_STRING_BYTES = 1024 * 1024;
    private static readonly byte[] S_MAGIC = "INNOSHDR"u8.ToArray();

    /// <summary>
    /// Encodes a validated shader artifact into its deterministic runtime deployment representation.
    /// </summary>
    /// <param name="artifact">
    /// The complete source-free artifact to encode.
    /// </param>
    /// <returns>
    /// The deterministic binary deployment payload.
    /// </returns>
    public static byte[] Encode(RenderShaderArtifact artifact)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(S_MAGIC);
        WriteString(writer, artifact.shaderName);
        WriteString(writer, artifact.targetKey);
        WriteString(writer, artifact.variant.value);
        WriteInterface(writer, artifact.shaderInterface);
        writer.Write(artifact.passes.Count);
        foreach (RenderShaderPassArtifact pass in artifact.passes)
        {
            WriteString(writer, pass.name);
            writer.Write((int)pass.programKind);
            WriteRasterState(writer, pass.rasterState);
            WriteInterface(writer, pass.shaderInterface);
            writer.Write(pass.stages.Count);
            foreach (RenderShaderStageArtifact stage in pass.stages)
            {
                writer.Write((int)stage.stage);
                writer.Write(stage.bytes.Length);
                writer.Write(stage.bytes.Span);
            }
        }
        writer.Flush();
        return stream.ToArray();
    }

    /// <summary>
    /// Decodes a deployed shader artifact and validates it against the requesting runtime shader and variant.
    /// </summary>
    /// <param name="bytes">
    /// The complete binary deployment payload.
    /// </param>
    /// <param name="expectedShaderName">
    /// The shader name declared by the requesting runtime asset.
    /// </param>
    /// <param name="expectedVariant">
    /// The exact static keyword selection requested by the material.
    /// </param>
    /// <returns>
    /// A fully validated, source-free artifact ready for GPU resource creation.
    /// </returns>
    /// <exception cref="InvalidDataException">
    /// Thrown when the payload is corrupt, exceeds safety limits, or does not match the request.
    /// </exception>
    public static RenderShaderArtifact Decode(
        ReadOnlySpan<byte> bytes,
        string expectedShaderName,
        RenderShaderVariant expectedVariant)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedShaderName);
        try
        {
            using var stream = new MemoryStream(bytes.ToArray(), writable: false);
            using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
            if (!reader.ReadBytes(S_MAGIC.Length).AsSpan().SequenceEqual(S_MAGIC))
                throw new InvalidDataException("Shader artifact magic is invalid.");
            string shaderName = ReadString(reader);
            if (!string.Equals(shaderName, expectedShaderName, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"Shader artifact '{shaderName}' does not match requested shader '{expectedShaderName}'.");
            }
            string targetKey = ReadString(reader);
            RenderShaderVariant variant = RenderShaderVariant.Parse(ReadString(reader));
            if (variant != expectedVariant)
            {
                throw new InvalidDataException(
                    $"Shader artifact variant '{variant}' does not match requested variant '{expectedVariant}'.");
            }
            ShaderInterface shaderInterface = ReadInterface(reader);
            int passCount = ReadCount(reader, C_MAX_PASSES, "shader pass");
            if (passCount == 0)
                throw new InvalidDataException("Shader artifact contains no pass.");
            var passes = new RenderShaderPassArtifact[passCount];
            for (int index = 0; index < passes.Length; index++)
            {
                string passName = ReadString(reader);
                ShaderProgramKind programKind = ReadEnum<ShaderProgramKind>(reader);
                RenderRasterState rasterState = ReadRasterState(reader);
                ShaderInterface passInterface = ReadInterface(reader);
                int stageCount = ReadCount(reader, 3, "shader stage");
                var stages = new RenderShaderStageArtifact[stageCount];
                for (int stageIndex = 0; stageIndex < stages.Length; stageIndex++)
                {
                    ShaderStage stage = ReadEnum<ShaderStage>(reader);
                    int byteCount = ReadCount(reader, C_MAX_STAGE_BYTES, "shader stage byte");
                    if (byteCount == 0)
                        throw new InvalidDataException($"Shader pass '{passName}' contains an empty stage.");
                    stages[stageIndex] = new RenderShaderStageArtifact(stage, ReadBytes(reader, byteCount));
                }
                passes[index] = new RenderShaderPassArtifact(
                    passName,
                    programKind,
                    rasterState,
                    passInterface,
                    stages);
            }
            if (stream.Position != stream.Length)
                throw new InvalidDataException("Shader artifact contains trailing data.");
            return new RenderShaderArtifact(shaderName, targetKey, variant, shaderInterface, passes);
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception exception) when (exception is EndOfStreamException
            or IOException
            or ArgumentException
            or FormatException
            or OverflowException)
        {
            throw new InvalidDataException("Shader artifact payload is corrupt.", exception);
        }
    }

    private static void WriteInterface(BinaryWriter writer, ShaderInterface shaderInterface)
    {
        writer.Write(shaderInterface.bindings.Count);
        foreach (ShaderInterfaceBinding binding in shaderInterface.bindings)
        {
            WriteString(writer, binding.id.value);
            writer.Write((int)binding.type);
            writer.Write((int)binding.stages);
            writer.Write(binding.arrayCount);
            writer.Write((int)binding.bindingKind);
            writer.Write((int)binding.storageAccess);
        }
    }

    private static ShaderInterface ReadInterface(BinaryReader reader)
    {
        int bindingCount = ReadCount(reader, C_MAX_BINDINGS, "shader binding");
        var bindings = new ShaderInterfaceBinding[bindingCount];
        var identities = new HashSet<string>(StringComparer.Ordinal);
        for (int index = 0; index < bindings.Length; index++)
        {
            string id = ReadString(reader);
            if (!identities.Add(id))
                throw new InvalidDataException($"Shader interface repeats binding '{id}'.");
            ShaderPropertyType type = ReadEnum<ShaderPropertyType>(reader);
            ShaderStage stages = (ShaderStage)reader.ReadInt32();
            const ShaderStage validStages = ShaderStage.Vertex | ShaderStage.Fragment | ShaderStage.Compute;
            if (stages == ShaderStage.None || (stages & ~validStages) != 0)
                throw new InvalidDataException($"Shader binding '{id}' has an invalid stage mask.");
            int arrayCount = reader.ReadInt32();
            if (arrayCount <= 0)
                throw new InvalidDataException($"Shader binding '{id}' has an invalid array count.");
            bindings[index] = new ShaderInterfaceBinding(
                new ShaderPropertyId(id),
                type,
                stages,
                arrayCount,
                ReadEnum<ShaderPropertyBindingKind>(reader),
                ReadEnum<RenderStorageAccess>(reader));
        }
        return new ShaderInterface(bindings);
    }

    private static void WriteRasterState(BinaryWriter writer, RenderRasterState state)
    {
        writer.Write((int)state.topology);
        writer.Write((int)state.cull);
        writer.Write((int)state.frontFace);
        writer.Write((int)state.depthCompare);
        writer.Write(state.depthWrite);
        writer.Write(state.colorWriteMask);
        writer.Write(state.multisampling);
        writer.Write(state.blend.enabled);
        writer.Write((int)state.blend.colorSource);
        writer.Write((int)state.blend.colorDestination);
        writer.Write((int)state.blend.colorEquation);
        writer.Write((int)state.blend.alphaSource);
        writer.Write((int)state.blend.alphaDestination);
        writer.Write((int)state.blend.alphaEquation);
        writer.Write(state.blend.constantRgba);
        writer.Write(state.blend.alphaToCoverage);
    }

    private static RenderRasterState ReadRasterState(BinaryReader reader)
        => new()
        {
            topology = ReadEnum<RenderPrimitiveTopology>(reader),
            cull = ReadEnum<RenderCullMode>(reader),
            frontFace = ReadEnum<RenderFrontFace>(reader),
            depthCompare = ReadEnum<RenderDepthCompare>(reader),
            depthWrite = reader.ReadBoolean(),
            colorWriteMask = reader.ReadByte(),
            multisampling = reader.ReadBoolean(),
            blend = new RenderBlendState
            {
                enabled = reader.ReadBoolean(),
                colorSource = ReadEnum<RenderBlendFactor>(reader),
                colorDestination = ReadEnum<RenderBlendFactor>(reader),
                colorEquation = ReadEnum<RenderBlendEquation>(reader),
                alphaSource = ReadEnum<RenderBlendFactor>(reader),
                alphaDestination = ReadEnum<RenderBlendFactor>(reader),
                alphaEquation = ReadEnum<RenderBlendEquation>(reader),
                constantRgba = reader.ReadUInt32(),
                alphaToCoverage = reader.ReadBoolean()
            }
        };

    private static void WriteString(BinaryWriter writer, string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        if (bytes.Length > C_MAX_STRING_BYTES)
            throw new ArgumentException("Shader artifact string exceeds the deployment safety limit.", nameof(value));
        writer.Write(bytes.Length);
        writer.Write(bytes);
    }

    private static string ReadString(BinaryReader reader)
    {
        int length = ReadCount(reader, C_MAX_STRING_BYTES, "string byte");
        return Encoding.UTF8.GetString(ReadBytes(reader, length));
    }

    private static int ReadCount(BinaryReader reader, int maximum, string subject)
    {
        int value = reader.ReadInt32();
        if (value < 0 || value > maximum)
            throw new InvalidDataException($"Shader artifact {subject} count '{value}' is outside the valid range.");
        return value;
    }

    private static byte[] ReadBytes(BinaryReader reader, int count)
    {
        byte[] bytes = reader.ReadBytes(count);
        if (bytes.Length != count)
            throw new EndOfStreamException("Shader artifact ended before the declared payload length.");
        return bytes;
    }

    private static TEnum ReadEnum<TEnum>(BinaryReader reader)
        where TEnum : struct, Enum
    {
        int value = reader.ReadInt32();
        if (!Enum.IsDefined(typeof(TEnum), value))
            throw new InvalidDataException($"Shader artifact contains invalid {typeof(TEnum).Name} value '{value}'.");
        return (TEnum)Enum.ToObject(typeof(TEnum), value);
    }
}
