using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Inno.Core.Diagnostics;
using Inno.Rendering;
using Inno.Rendering.Assets;
using Inno.Rendering.Shaders;

namespace Inno.Build.Toolchains.Bgfx.Tools;

/// <summary>
/// Compiles typed shader stages with the bgfx shader toolchain.
/// </summary>
public sealed partial class BgfxShadercToolchain
{
    private const byte C_SHADER_BINARY_VERSION = 11;
    private const int C_SHADER_BINARY_HEADER_SIZE = 12;
    private const int C_SHADER_UNIFORM_METADATA_SIZE = 10;
    private static readonly IReadOnlyList<string> s_languages = Array.AsReadOnly(new[] { "inno.shader-language.bgfx-sc" });

    /// <summary>
    /// Gets shader source languages accepted by this toolchain.
    /// </summary>
    public IReadOnlyList<string> supportedSourceLanguages => s_languages;

    /// <summary>
    /// Gets the implementation id text used by the current instance.
    /// </summary>
    public string implementationId => "bgfx";

    /// <summary>
    /// Compiles the supplied source into a validated runtime artifact.
    /// </summary>
    /// <param name="request">
    /// The validated immutable request that defines this operation.
    /// </param>
    /// <param name="cancellationToken">
    /// The token that cancels the operation before it commits.
    /// </param>
    /// <returns>
    /// An asynchronous operation that completes after all requested work has finished.
    /// </returns>
    public async ValueTask<ShaderStageToolResult> CompileAsync(ShaderStageToolRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.stage);
        ArgumentNullException.ThrowIfNull(request.target);
        cancellationToken.ThrowIfCancellationRequested();
        BgfxGeneratedStage generated;
        try
        {
            ValidateCapabilities(request.stage, request.target.capabilities);
            generated = new BgfxShaderIrGenerator(request.stage).Generate();
        }
        catch (BgfxSourceSyntaxException failure)
        {
            return new([], [], [new("BGFX_IR_GENERATION", DiagnosticSeverity.Error, failure.Message, failure.position)]);
        }
        BgfxShadercResult native = await RunCompilerAsync(generated.source, generated.varying, request.stage.stage,
            request.target, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        List<ShaderSourceDiagnostic> diagnostics = ParseDiagnostics(native, generated.sourcePositions, new("inno-generated-stage", 0, 0));
        if (native.exitCode != 0 || native.bytes is null || native.bytes.Length == 0 || diagnostics.Any(static value => value.severity == DiagnosticSeverity.Error))
        {
            if (!diagnostics.Any(static diagnostic => diagnostic.severity == DiagnosticSeverity.Error))
                diagnostics.Add(new("BGFX_SHADER_COMPILE_FAILED", DiagnosticSeverity.Error, $"BGFX shaderc exited with code {native.exitCode} without a usable binary.", new("inno-generated-stage", 0, 0)));
            return new([], generated.bindings, diagnostics);
        }
        IReadOnlyList<ShaderStageBinding> reflectedBindings;
        try
        {
            HashSet<string> reflectedNames = ReadReflectedUniformNames(native.bytes);
            reflectedBindings = generated.bindings.Where(binding =>
            {
                ShaderIrStageInput input = request.stage.inputs.Single(value => value.id == binding.id);
                return input.kind == ShaderIrInputKind.Storage || reflectedNames.Contains(binding.nativeName);
            }).ToArray();
        }
        catch (InvalidDataException failure)
        {
            diagnostics.Add(new("BGFX_SHADER_REFLECTION", DiagnosticSeverity.Error, failure.Message,
                new("inno-generated-stage", 0, 0)));
            return new([], [], diagnostics);
        }
        return new(native.bytes, reflectedBindings, diagnostics);
    }

    private static HashSet<string> ReadReflectedUniformNames(ReadOnlySpan<byte> binary)
    {
        if (binary.Length < C_SHADER_BINARY_HEADER_SIZE + sizeof(ushort))
            throw new InvalidDataException("BGFX shaderc returned a truncated shader binary header.");
        if (binary[1] != (byte)'S' || binary[2] != (byte)'H'
            || binary[0] is not ((byte)'V' or (byte)'F' or (byte)'C'))
            throw new InvalidDataException("BGFX shaderc returned an unrecognized shader binary.");
        if (binary[3] != C_SHADER_BINARY_VERSION)
            throw new InvalidDataException(
                $"BGFX shader binary version {binary[3]} does not match the bundled reader version {C_SHADER_BINARY_VERSION}.");

        int offset = C_SHADER_BINARY_HEADER_SIZE;
        ushort count = BinaryPrimitives.ReadUInt16LittleEndian(binary.Slice(offset, sizeof(ushort)));
        offset += sizeof(ushort);
        var result = new HashSet<string>(StringComparer.Ordinal);
        for (int index = 0; index < count; index++)
        {
            RequireBinaryRange(binary, offset, sizeof(byte));
            int nameLength = binary[offset++];
            RequireBinaryRange(binary, offset, nameLength + C_SHADER_UNIFORM_METADATA_SIZE);
            string name = Encoding.UTF8.GetString(binary.Slice(offset, nameLength));
            if (string.IsNullOrWhiteSpace(name) || !result.Add(name))
                throw new InvalidDataException("BGFX shader binary contains an invalid reflected uniform table.");
            offset += nameLength + C_SHADER_UNIFORM_METADATA_SIZE;
        }
        return result;
    }

    private static void RequireBinaryRange(ReadOnlySpan<byte> binary, int offset, int length)
    {
        if (length < 0 || offset < 0 || offset > binary.Length - length)
            throw new InvalidDataException("BGFX shaderc returned a truncated reflected uniform table.");
    }

    private static List<ShaderSourceDiagnostic> ParseDiagnostics(BgfxShadercResult native,
        IReadOnlyList<ShaderSourcePosition> sourcePositions, ShaderSourcePosition fallback)
    {
        var diagnostics = new List<ShaderSourceDiagnostic>();
        string[] lines = (native.standardOutput + "\n" + native.standardError).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var sourceMap = new Dictionary<int, ShaderSourcePosition>();
        foreach (string line in lines)
        {
            Match excerpt = SourceExcerptPattern().Match(line);
            if (!excerpt.Success || !int.TryParse(excerpt.Groups["line"].Value, out int physicalLine)
                || !int.TryParse(excerpt.Groups["marker"].Value, out int marker)) continue;
            if (marker >= 0 && marker < sourcePositions.Count) sourceMap[physicalLine] = sourcePositions[marker];
        }
        foreach (string line in lines)
        {
            if (line == "Code:" || line == "---") continue;
            DiagnosticSeverity severity = ErrorSeverityPattern().IsMatch(line) ? DiagnosticSeverity.Error
                : WarningSeverityPattern().IsMatch(line) ? DiagnosticSeverity.Warning : DiagnosticSeverity.Info;
            Match match = SourceDiagnosticPattern().Match(line);
            ShaderSourcePosition position = fallback;
            if (match.Success && int.TryParse(match.Groups["line"].Value, out int sourceLine))
            {
                _ = int.TryParse(match.Groups["column"].Value, out int column);
                position = sourceMap.TryGetValue(sourceLine, out ShaderSourcePosition original) ? original
                    : new(fallback.assetPath, sourceLine, column);
            }
            diagnostics.Add(new(severity == DiagnosticSeverity.Warning ? "BGFX_SHADER_WARNING" : "BGFX_SHADER_DIAGNOSTIC", severity, line, position));
        }
        return diagnostics;
    }

    private static void ValidateCapabilities(ShaderIrStage stage, GraphicsCapabilities capabilities)
    {
        if (stage.stage == ShaderStage.Compute) Require(GraphicsCapability.Compute);
        foreach (ShaderIrStageInput input in stage.inputs)
        {
            if (input.kind == ShaderIrInputKind.VertexAttribute && input.semantic == "instance-data") Require(GraphicsCapability.Instancing);
            if (input.kind == ShaderIrInputKind.Builtin && input.semantic == "vertex-id") Require(GraphicsCapability.ProceduralDraw);
            if (input.type.id == "sampled-texture2d-array") Require(GraphicsCapability.Texture2DArray);
            if (input.type.id == "sampled-texture3d") Require(GraphicsCapability.Texture3D);
            if (input.type.storage is ShaderStorageType storage)
            {
                if (stage.stage != ShaderStage.Compute)
                    throw new BgfxSourceSyntaxException("The BGFX runtime contract exposes storage bindings only for compute passes.", new("inno-generated-stage", 1, 1));
                Require(storage.isImage ? GraphicsCapability.StorageTexture : GraphicsCapability.StorageBuffer);
                if (input.location >= capabilities.limits.maxComputeBindings)
                    throw new BgfxSourceSyntaxException("A storage binding exceeds the target compute binding limit.", new("inno-generated-stage", 1, 1));
                if (storage.isImage)
                {
                    if (storage.array) Require(GraphicsCapability.Texture2DArray);
                    if (storage.dimension == RenderTextureDimension.Texture3D) Require(GraphicsCapability.Texture3D);
                    if (!capabilities.SupportsStorage(storage.format!.Value, storage.access))
                        throw new BgfxSourceSyntaxException($"Storage format '{storage.format}' does not support '{storage.access}' on this target.", new("inno-generated-stage", 1, 1));
                }
            }
        }
        foreach (ShaderIrStageOutput output in stage.outputs)
        {
            if (output.kind == ShaderIrOutputKind.Depth) Require(GraphicsCapability.FragmentDepth);
            if (output.kind == ShaderIrOutputKind.Color && output.location >= capabilities.limits.maxColorAttachments)
                throw new BgfxSourceSyntaxException($"Color attachment {output.location} exceeds the target limit {capabilities.limits.maxColorAttachments}.", new("inno-generated-stage", 1, 1));
        }
        return;
        void Require(GraphicsCapability capability)
        {
            if (!capabilities.Supports(capability)) throw new BgfxSourceSyntaxException($"The stage requires unsupported capability '{capability}'.", new("inno-generated-stage", 1, 1));
        }
    }

    [GeneratedRegex(@"(?:(?<path>[^\s()]+)(?:\(|:)|\()(?<line>\d+)(?:[, :](?<column>\d+))?[):]")]
    private static partial Regex SourceDiagnosticPattern();

    [GeneratedRegex(@"^(?:>>>\s*)?(?<line>\d+):.*?/\*inno_source_(?<marker>\d+)\*/")]
    private static partial Regex SourceExcerptPattern();

    [GeneratedRegex(@"^(?:>>>\s*)?\d+:")]
    private static partial Regex CodeExcerptPattern();

    [GeneratedRegex(@"(?:^ERROR:|^error:|\):\s*error|:\s+error(?:\s+[A-Z]+\d+)?:)", RegexOptions.IgnoreCase)]
    private static partial Regex ErrorSeverityPattern();

    [GeneratedRegex(@"(?:^WARNING:|^warning:|\):\s*warning|:\s+warning(?:\s+[A-Z]+\d+)?:)", RegexOptions.IgnoreCase)]
    private static partial Regex WarningSeverityPattern();
}
