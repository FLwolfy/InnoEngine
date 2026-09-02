using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Inno.Rendering;

namespace Inno.Rendering.Assets;

/// <summary>
/// Selects one renderer profile and compilation policy.
/// </summary>
public sealed class ShaderCompileTarget
{
    /// <summary>
    /// Creates a shader compilation target.
    /// </summary>
    /// <param name="profileKey">
    /// Backend compiler-owned stable profile key.
    /// </param>
    /// <param name="capabilities">
    /// Target renderer capabilities.
    /// </param>
    /// <param name="optimize">
    /// Whether release optimization is enabled.
    /// </param>
    /// <param name="debugInformation">
    /// Whether shader debug information is emitted.
    /// </param>
    public ShaderCompileTarget(
        string profileKey,
        GraphicsCapabilities capabilities,
        bool optimize = true,
        bool debugInformation = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileKey);
        ArgumentNullException.ThrowIfNull(capabilities);
        this.profileKey = profileKey;
        this.capabilities = capabilities;
        this.optimize = optimize;
        this.debugInformation = debugInformation;
    }

    /// <summary>
    /// Gets the backend compiler-owned stable profile key.
    /// </summary>
    public string profileKey { get; }

    /// <summary>
    /// Gets target renderer capabilities.
    /// </summary>
    public GraphicsCapabilities capabilities { get; }

    /// <summary>
    /// Gets whether release optimization is enabled.
    /// </summary>
    public bool optimize { get; }

    /// <summary>
    /// Gets whether shader debug information is emitted.
    /// </summary>
    public bool debugInformation { get; }

    /// <summary>
    /// Gets a stable target cache-key fragment.
    /// </summary>
    public string key => $"{profileKey}:opt={optimize}:debug={debugInformation}";
}

/// <summary>
/// Stores one immutable target stage binary.
/// </summary>
public sealed class ShaderStageArtifact
{
    private readonly byte[] m_bytes;

    /// <summary>
    /// Creates a stage artifact.
    /// </summary>
    /// <param name="stage">
    /// Single compiled shader stage.
    /// </param>
    /// <param name="bytes">
    /// Target binary bytes.
    /// </param>
    /// <param name="sourceLocation">
    /// Original source mapping.
    /// </param>
    public ShaderStageArtifact(
        ShaderStage stage,
        ReadOnlySpan<byte> bytes,
        ShaderSourceLocation sourceLocation)
    {
        if (bytes.IsEmpty)
        {
            throw new ArgumentException("A compiled shader stage cannot be empty.", nameof(bytes));
        }

        this.stage = stage;
        m_bytes = bytes.ToArray();
        this.sourceLocation = sourceLocation;
    }

    /// <summary>
    /// Gets the compiled shader stage.
    /// </summary>
    public ShaderStage stage { get; }

    /// <summary>
    /// Gets immutable target binary bytes.
    /// </summary>
    public ReadOnlyMemory<byte> bytes => m_bytes;

    /// <summary>
    /// Gets the original source mapping.
    /// </summary>
    public ShaderSourceLocation sourceLocation { get; }
}

/// <summary>
/// Stores all compiled stages and state for one shader pass.
/// </summary>
public sealed class CompiledShaderPass
{
    /// <summary>
    /// Creates a compiled pass artifact.
    /// </summary>
    /// <param name="definition">
    /// Stable pass definition.
    /// </param>
    /// <param name="stages">
    /// Compiled target stages.
    /// </param>
    /// <param name="shaderInterface">
    /// Pass-local manifest binding contract.
    /// </param>
    public CompiledShaderPass(
        ShaderPassDefinition definition,
        IReadOnlyList<ShaderStageArtifact> stages,
        ShaderInterface shaderInterface)
    {
        ArgumentNullException.ThrowIfNull(stages);
        ArgumentNullException.ThrowIfNull(shaderInterface);
        this.definition = definition;
        this.stages = stages;
        this.shaderInterface = shaderInterface;
    }

    /// <summary>
    /// Gets the stable pass definition.
    /// </summary>
    public ShaderPassDefinition definition { get; }

    /// <summary>
    /// Gets compiled target stages.
    /// </summary>
    public IReadOnlyList<ShaderStageArtifact> stages { get; }

    /// <summary>
    /// Gets the pass-local manifest binding contract.
    /// </summary>
    public ShaderInterface shaderInterface { get; }
}

/// <summary>
/// Contains an immutable target shader artifact and expected reflected interface.
/// </summary>
public sealed class CompiledShaderArtifact
{
    /// <summary>
    /// Creates a compiled shader artifact.
    /// </summary>
    /// <param name="shaderName">
    /// Artist-facing shader name.
    /// </param>
    /// <param name="targetKey">
    /// Stable target cache key.
    /// </param>
    /// <param name="variant">
    /// Static keyword variant.
    /// </param>
    /// <param name="shaderInterface">
    /// Manifest-derived binding contract.
    /// </param>
    /// <param name="passes">
    /// Compiled pass binaries.
    /// </param>
    public CompiledShaderArtifact(
        string shaderName,
        string targetKey,
        RenderShaderVariant variant,
        ShaderInterface shaderInterface,
        IReadOnlyList<CompiledShaderPass> passes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(shaderName);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetKey);
        ArgumentNullException.ThrowIfNull(shaderInterface);
        ArgumentNullException.ThrowIfNull(passes);
        this.shaderName = shaderName;
        this.targetKey = targetKey;
        this.variant = variant;
        this.shaderInterface = shaderInterface;
        this.passes = passes;
    }

    /// <summary>
    /// Gets the artist-facing shader name.
    /// </summary>
    public string shaderName { get; }

    /// <summary>
    /// Gets the stable target cache key.
    /// </summary>
    public string targetKey { get; }

    /// <summary>
    /// Gets the static keyword variant.
    /// </summary>
    public RenderShaderVariant variant { get; }

    /// <summary>
    /// Gets the manifest-derived binding contract.
    /// </summary>
    public ShaderInterface shaderInterface { get; }

    /// <summary>
    /// Gets compiled pass binaries.
    /// </summary>
    public IReadOnlyList<CompiledShaderPass> passes { get; }

    /// <summary>
    /// Creates the source-free deployment artifact consumed by Editor preview sessions and Players.
    /// </summary>
    /// <returns>
    /// A deep immutable runtime artifact containing only target binaries, fixed-function state, and binding contracts.
    /// </returns>
    public RenderShaderArtifact CreateRuntimeArtifact()
        => new(
            shaderName,
            targetKey,
            variant,
            shaderInterface,
            passes.Select(static pass => new RenderShaderPassArtifact(
                pass.definition.name,
                pass.definition.programKind,
                ConvertRasterState(pass.definition.renderState),
                pass.shaderInterface,
                pass.stages.Select(static stage => new RenderShaderStageArtifact(
                    stage.stage,
                    stage.bytes.Span)).ToArray())).ToArray());

    private static RenderRasterState ConvertRasterState(ShaderRenderState source)
        => new()
        {
            topology = source.topology,
            cull = source.cull switch
            {
                ShaderCullMode.None => RenderCullMode.None,
                ShaderCullMode.Front => RenderCullMode.Front,
                ShaderCullMode.Back => RenderCullMode.Back,
                _ => throw new ArgumentOutOfRangeException(nameof(source))
            },
            frontFace = source.frontFace,
            depthCompare = source.depthCompare switch
            {
                ShaderCompareFunction.Never => RenderDepthCompare.Never,
                ShaderCompareFunction.Less => RenderDepthCompare.Less,
                ShaderCompareFunction.Equal => RenderDepthCompare.Equal,
                ShaderCompareFunction.LessEqual => RenderDepthCompare.LessEqual,
                ShaderCompareFunction.Greater => RenderDepthCompare.Greater,
                ShaderCompareFunction.NotEqual => RenderDepthCompare.NotEqual,
                ShaderCompareFunction.GreaterEqual => RenderDepthCompare.GreaterEqual,
                ShaderCompareFunction.Always => RenderDepthCompare.Always,
                _ => throw new ArgumentOutOfRangeException(nameof(source))
            },
            depthWrite = source.depthWrite,
            blend = source.blend,
            colorWriteMask = source.colorWriteMask,
            multisampling = source.multisampling
        };
}

/// <summary>
/// Returns a candidate artifact and structured diagnostics without mutating active state.
/// </summary>
public sealed class ShaderCompilationResult
{
    /// <summary>
    /// Creates a shader compilation result.
    /// </summary>
    /// <param name="artifact">
    /// Candidate artifact, or <see langword="null"/> after failure.
    /// </param>
    /// <param name="diagnostics">
    /// Validation and compiler diagnostics.
    /// </param>
    public ShaderCompilationResult(
        CompiledShaderArtifact? artifact,
        IReadOnlyList<ShaderDiagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        this.artifact = artifact;
        this.diagnostics = diagnostics;
    }

    /// <summary>
    /// Gets the candidate artifact, or <see langword="null"/> after failure.
    /// </summary>
    public CompiledShaderArtifact? artifact { get; }

    /// <summary>
    /// Gets validation and compiler diagnostics.
    /// </summary>
    public IReadOnlyList<ShaderDiagnostic> diagnostics { get; }

    /// <summary>
    /// Gets whether a complete candidate artifact was produced.
    /// </summary>
    public bool succeeded => artifact is not null
        && diagnostics.All(static value => value.severity != ShaderDiagnosticSeverity.Error);
}

/// <summary>
/// Supplies one validated IR stage to a backend-owned shader compiler.
/// </summary>
/// <param name="stage">
/// Validated stage module.
/// </param>
/// <param name="stagePass">
/// Owning IR pass.
/// </param>
/// <param name="pass">
/// Stable pass definition.
/// </param>
/// <param name="target">
/// Backend compiler target.
/// </param>
/// <param name="variant">
/// Static keyword variant.
/// </param>
/// <param name="sourceRoot">
/// Controlled source root used to resolve includes.
/// </param>
public sealed record ShaderToolRequest(
    ShaderIRStageModule stage,
    ShaderIRPass stagePass,
    ShaderPassDefinition pass,
    ShaderCompileTarget target,
    RenderShaderVariant variant,
    string sourceRoot);

/// <summary>
/// Returns one backend compiler process result without activating it.
/// </summary>
/// <param name="bytes">
/// Compiled stage bytes, or <see langword="null"/> after failure.
/// </param>
/// <param name="exitCode">
/// Compiler process exit code.
/// </param>
/// <param name="standardOutput">
/// Captured standard output.
/// </param>
/// <param name="standardError">
/// Captured standard error.
/// </param>
public sealed record ShaderToolResult(
    byte[]? bytes,
    int exitCode,
    string standardOutput,
    string standardError);

/// <summary>
/// Defines a backend-owned target compiler used by the common Shader IR pipeline.
/// </summary>
public interface IShaderCompilerToolchain
{
    /// <summary>
    /// Creates a target supported by this toolchain and capability snapshot.
    /// </summary>
    /// <param name="capabilities">
    /// Target renderer capabilities.
    /// </param>
    /// <param name="optimize">
    /// Whether release optimization is enabled.
    /// </param>
    /// <param name="debugInformation">
    /// Whether compiler debug information is emitted.
    /// </param>
    /// <returns>
    /// A stable target description accepted by this toolchain.
    /// </returns>
    ShaderCompileTarget CreateTarget(
        GraphicsCapabilities capabilities,
        bool optimize = true,
        bool debugInformation = false);

    /// <summary>
    /// Compiles one validated stage without mutating active GPU state.
    /// </summary>
    /// <param name="request">
    /// Complete stage compilation request.
    /// </param>
    /// <param name="cancellationToken">
    /// Compilation cancellation.
    /// </param>
    /// <returns>
    /// The target stage bytes and captured diagnostics.
    /// </returns>
    ValueTask<ShaderToolResult> CompileAsync(
        ShaderToolRequest request,
        CancellationToken cancellationToken);
}

/// <summary>
/// Compiles validated handwritten or graph-generated Shader IR through one target toolchain.
/// </summary>
public sealed partial class ShaderCompiler
{
    private readonly IShaderCompilerToolchain m_toolchain;

    /// <summary>
    /// Creates a common IR compiler with one backend-owned target toolchain.
    /// </summary>
    /// <param name="toolchain">
    /// Backend compiler implementation.
    /// </param>
    public ShaderCompiler(IShaderCompilerToolchain toolchain)
    {
        m_toolchain = toolchain ?? throw new ArgumentNullException(nameof(toolchain));
    }

    /// <summary>
    /// Creates a target supported by the configured backend compiler.
    /// </summary>
    /// <param name="capabilities">
    /// Target renderer capabilities.
    /// </param>
    /// <param name="optimize">
    /// Whether release optimization is enabled.
    /// </param>
    /// <param name="debugInformation">
    /// Whether compiler debug information is emitted.
    /// </param>
    /// <returns>
    /// A stable target description accepted by this compiler.
    /// </returns>
    public ShaderCompileTarget CreateTarget(
        GraphicsCapabilities capabilities,
        bool optimize = true,
        bool debugInformation = false)
        => m_toolchain.CreateTarget(capabilities, optimize, debugInformation);

    /// <summary>
    /// Compiles a complete shader candidate without replacing any active artifact.
    /// </summary>
    /// <param name="module">
    /// Shared handwritten/graph Shader IR.
    /// </param>
    /// <param name="target">
    /// Target renderer profile and capabilities.
    /// </param>
    /// <param name="variant">
    /// Static keyword selection.
    /// </param>
    /// <param name="sourceRoot">
    /// Absolute project Assets directory used for includes and varying definitions.
    /// </param>
    /// <param name="cancellationToken">
    /// Compilation cancellation.
    /// </param>
    /// <returns>
    /// A candidate artifact or structured failure diagnostics.
    /// </returns>
    public async ValueTask<ShaderCompilationResult> CompileAsync(
        ShaderIRModule module,
        ShaderCompileTarget target,
        RenderShaderVariant variant,
        string sourceRoot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(module);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceRoot);
        var diagnostics = new List<ShaderDiagnostic>();
        ShaderIRValidationResult validation = ShaderIRValidator.Validate(module, target.capabilities);
        diagnostics.AddRange(validation.diagnostics);
        ValidateVariant(module, variant, diagnostics);
        if (diagnostics.Any(static value => value.severity == ShaderDiagnosticSeverity.Error))
        {
            return new ShaderCompilationResult(null, diagnostics);
        }

        var passes = new List<CompiledShaderPass>();
        foreach (ShaderIRPass pass in module.passes)
        {
            GraphicsFeature unavailable = pass.definition.requiredFeatures & ~target.capabilities.features;
            if (unavailable != GraphicsFeature.None)
            {
                continue;
            }

            var stages = new List<ShaderStageArtifact>();
            foreach (ShaderIRStageModule stage in pass.stages)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ShaderToolResult result = await m_toolchain.CompileAsync(
                    new ShaderToolRequest(stage, pass, pass.definition, target, variant, sourceRoot),
                    cancellationToken).ConfigureAwait(false);
                if (result.exitCode != 0 || result.bytes is null || result.bytes.Length == 0)
                {
                    diagnostics.AddRange(ParseCompilerDiagnostics(stage, result));
                    if (!diagnostics.Any(static value => value.severity == ShaderDiagnosticSeverity.Error))
                    {
                        diagnostics.Add(new ShaderDiagnostic(
                            "SHADER_TARGET_COMPILE_FAILED",
                            ShaderDiagnosticSeverity.Error,
                            $"Target compiler failed for pass '{pass.definition.name}' stage '{stage.stage}' " +
                            $"with exit code {result.exitCode}.",
                            stage.location));
                    }

                    return new ShaderCompilationResult(null, diagnostics);
                }

                diagnostics.AddRange(ParseCompilerDiagnostics(stage, result));
                stages.Add(new ShaderStageArtifact(stage.stage, result.bytes, stage.location));
            }

            passes.Add(new CompiledShaderPass(
                pass.definition,
                stages,
                ShaderInterface.FromPass(module, pass)));
        }

        if (passes.Count == 0)
        {
            diagnostics.Add(new ShaderDiagnostic(
                "SHADER_NO_SUPPORTED_PASS",
                ShaderDiagnosticSeverity.Error,
                $"Shader '{module.definition.name}' has no pass supported by target '{target.key}'."));
            return new ShaderCompilationResult(null, diagnostics);
        }

        var artifact = new CompiledShaderArtifact(
            module.definition.name,
            target.key,
            variant,
            ShaderInterface.FromModule(module),
            passes);
        return new ShaderCompilationResult(artifact, diagnostics);
    }

    private static void ValidateVariant(
        ShaderIRModule module,
        RenderShaderVariant variant,
        List<ShaderDiagnostic> diagnostics)
    {
        Dictionary<string, ShaderKeywordDefinition> definitions = module.definition.keywords
            .ToDictionary(static value => value.id, StringComparer.Ordinal);
        foreach ((string id, string option) in variant.options)
        {
            if (!definitions.TryGetValue(id, out ShaderKeywordDefinition definition))
            {
                diagnostics.Add(new ShaderDiagnostic(
                    "SHADER_VARIANT_UNKNOWN_KEYWORD",
                    ShaderDiagnosticSeverity.Error,
                    $"Variant selects undeclared keyword '{id}'."));
            }
            else if (!definition.options.Contains(option, StringComparer.Ordinal))
            {
                diagnostics.Add(new ShaderDiagnostic(
                    "SHADER_VARIANT_UNKNOWN_OPTION",
                    ShaderDiagnosticSeverity.Error,
                    $"Variant selects undeclared option '{option}' for keyword '{id}'."));
            }
        }
    }

    private static IReadOnlyList<ShaderDiagnostic> ParseCompilerDiagnostics(
        ShaderIRStageModule stage,
        ShaderToolResult result)
    {
        string text = string.Join(
            Environment.NewLine,
            new[] { result.standardOutput, result.standardError }
                .Where(static value => !string.IsNullOrWhiteSpace(value)));
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var diagnostics = new List<ShaderDiagnostic>();
        foreach (string line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            Match match = CompilerLinePattern().Match(line);
            int sourceLine = match.Success && int.TryParse(match.Groups[1].Value, out int parsedLine)
                ? parsedLine
                : 0;
            string nodeId = sourceLine > 0 && stage.lineNodeIds.TryGetValue(sourceLine, out string? mappedNode)
                ? mappedNode
                : stage.location.nodeId;
            ShaderDiagnosticSeverity severity = line.Contains("warning", StringComparison.OrdinalIgnoreCase)
                ? ShaderDiagnosticSeverity.Warning
                : result.exitCode == 0
                    ? ShaderDiagnosticSeverity.Info
                    : ShaderDiagnosticSeverity.Error;
            diagnostics.Add(new ShaderDiagnostic(
                severity == ShaderDiagnosticSeverity.Warning ? "SHADERC_WARNING" : "SHADERC_DIAGNOSTIC",
                severity,
                line,
                new ShaderSourceLocation(
                    stage.location.assetPath,
                    stage.location.passName,
                    stage.stage,
                    sourceLine,
                    0,
                    nodeId)));
        }

        return diagnostics;
    }

    [GeneratedRegex(@"(?:\(|:)(\d+)(?:[,\):])")]
    private static partial Regex CompilerLinePattern();
}
