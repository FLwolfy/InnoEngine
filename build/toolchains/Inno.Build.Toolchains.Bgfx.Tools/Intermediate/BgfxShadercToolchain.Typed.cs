using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Inno.Core.Diagnostics;
using Inno.Rendering;
using Inno.Rendering.Assets;
using Inno.Rendering.Shaders;

namespace Inno.Build.Toolchains.Bgfx.Tools;

public sealed partial class BgfxShadercToolchain
{
    private static readonly IReadOnlyList<string> s_languages = Array.AsReadOnly(new[] { "inno.shader-language.bgfx-sc" });

    /// <inheritdoc />
    public IReadOnlyList<string> supportedSourceLanguages => s_languages;

    /// <inheritdoc />
    public string implementationId => "bgfx";

    /// <inheritdoc />
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
        return new(native.bytes, generated.bindings, diagnostics);
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
