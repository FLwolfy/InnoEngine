using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Inno.Rendering.Core;

namespace Inno.Rendering.Assets;

/// <summary>
/// Identifies one deterministic selection of static shader keyword options.
/// </summary>
public readonly struct ShaderVariantKey : IEquatable<ShaderVariantKey>
{
    private static readonly IReadOnlyDictionary<string, string> S_EMPTY_OPTIONS =
        new ReadOnlyDictionary<string, string>(new Dictionary<string, string>());

    private readonly IReadOnlyDictionary<string, string>? m_options;
    private readonly string? m_value;

    /// <summary>
    /// Creates a deterministic variant key.
    /// </summary>
    /// <param name="options">Stable keyword ID to selected option mappings.</param>
    public ShaderVariantKey(IReadOnlyDictionary<string, string> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        m_options = new ReadOnlyDictionary<string, string>(options
            .OrderBy(static value => value.Key, StringComparer.Ordinal)
            .ToDictionary(static value => value.Key, static value => value.Value, StringComparer.Ordinal));
        m_value = string.Join(
            ";",
            m_options.Select(static pair => $"{pair.Key}={pair.Value}"));
    }

    /// <summary>Gets the empty/default variant.</summary>
    public static ShaderVariantKey empty { get; } = new(new Dictionary<string, string>());

    /// <summary>Gets stable keyword selections.</summary>
    public IReadOnlyDictionary<string, string> options => m_options ?? S_EMPTY_OPTIONS;

    /// <summary>Gets the canonical cache-key representation.</summary>
    public string value => m_value ?? string.Empty;

    /// <inheritdoc />
    public bool Equals(ShaderVariantKey other)
        => string.Equals(value, other.value, StringComparison.Ordinal);

    /// <inheritdoc />
    public override bool Equals(object? obj)
        => obj is ShaderVariantKey other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(value);

    /// <summary>Determines whether two variants select identical stable options.</summary>
    public static bool operator ==(ShaderVariantKey left, ShaderVariantKey right) => left.Equals(right);

    /// <summary>Determines whether two variants select different stable options.</summary>
    public static bool operator !=(ShaderVariantKey left, ShaderVariantKey right) => !left.Equals(right);

    /// <inheritdoc />
    public override string ToString() => value;
}

/// <summary>
/// Selects one renderer profile and compilation policy.
/// </summary>
public sealed class ShaderCompileTarget
{
    /// <summary>
    /// Creates a shader compilation target.
    /// </summary>
    /// <param name="profile">Target shaderc profile.</param>
    /// <param name="capabilities">Target renderer capabilities.</param>
    /// <param name="optimize">Whether release optimization is enabled.</param>
    /// <param name="debugInformation">Whether shader debug information is emitted.</param>
    public ShaderCompileTarget(
        ShaderCompilerProfile profile,
        GraphicsCapabilities capabilities,
        bool optimize = true,
        bool debugInformation = false)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(capabilities);
        if (profile.backend != capabilities.backend)
        {
            throw new ArgumentException(
                "Shader profile backend must match the capability snapshot.",
                nameof(capabilities));
        }

        this.profile = profile;
        this.capabilities = capabilities;
        this.optimize = optimize;
        this.debugInformation = debugInformation;
    }

    /// <summary>Gets the target compiler profile.</summary>
    public ShaderCompilerProfile profile { get; }

    /// <summary>Gets target renderer capabilities.</summary>
    public GraphicsCapabilities capabilities { get; }

    /// <summary>Gets whether release optimization is enabled.</summary>
    public bool optimize { get; }

    /// <summary>Gets whether shader debug information is emitted.</summary>
    public bool debugInformation { get; }

    /// <summary>Gets a stable target cache-key fragment.</summary>
    public string key => $"{profile.key}:opt={optimize}:debug={debugInformation}";
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
    /// <param name="stage">Single compiled shader stage.</param>
    /// <param name="bytes">Target binary bytes.</param>
    /// <param name="sourceLocation">Original source mapping.</param>
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

    /// <summary>Gets the compiled shader stage.</summary>
    public ShaderStage stage { get; }

    /// <summary>Gets immutable target binary bytes.</summary>
    public ReadOnlyMemory<byte> bytes => m_bytes;

    /// <summary>Gets the original source mapping.</summary>
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
    /// <param name="definition">Stable pass definition.</param>
    /// <param name="stages">Compiled target stages.</param>
    /// <param name="shaderInterface">Pass-local manifest binding contract.</param>
    public CompiledShaderPass(
        ShaderPassDefinition definition,
        IReadOnlyList<ShaderStageArtifact> stages,
        ShaderInterface shaderInterface)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(stages);
        ArgumentNullException.ThrowIfNull(shaderInterface);
        this.definition = definition;
        this.stages = stages;
        this.shaderInterface = shaderInterface;
    }

    /// <summary>Gets the stable pass definition.</summary>
    public ShaderPassDefinition definition { get; }

    /// <summary>Gets compiled target stages.</summary>
    public IReadOnlyList<ShaderStageArtifact> stages { get; }

    /// <summary>Gets the pass-local manifest binding contract.</summary>
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
    /// <param name="shaderName">Artist-facing shader name.</param>
    /// <param name="targetKey">Stable target cache key.</param>
    /// <param name="variant">Static keyword variant.</param>
    /// <param name="shaderInterface">Manifest-derived binding contract.</param>
    /// <param name="passes">Compiled pass binaries.</param>
    public CompiledShaderArtifact(
        string shaderName,
        string targetKey,
        ShaderVariantKey variant,
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

    /// <summary>Gets the artist-facing shader name.</summary>
    public string shaderName { get; }

    /// <summary>Gets the stable target cache key.</summary>
    public string targetKey { get; }

    /// <summary>Gets the static keyword variant.</summary>
    public ShaderVariantKey variant { get; }

    /// <summary>Gets the manifest-derived binding contract.</summary>
    public ShaderInterface shaderInterface { get; }

    /// <summary>Gets compiled pass binaries.</summary>
    public IReadOnlyList<CompiledShaderPass> passes { get; }
}

/// <summary>
/// Returns a candidate artifact and structured diagnostics without mutating active state.
/// </summary>
public sealed class ShaderCompilationResult
{
    /// <summary>
    /// Creates a shader compilation result.
    /// </summary>
    /// <param name="artifact">Candidate artifact, or <see langword="null"/> after failure.</param>
    /// <param name="diagnostics">Validation and compiler diagnostics.</param>
    public ShaderCompilationResult(
        CompiledShaderArtifact? artifact,
        IReadOnlyList<ShaderDiagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        this.artifact = artifact;
        this.diagnostics = diagnostics;
    }

    /// <summary>Gets the candidate artifact, or <see langword="null"/> after failure.</summary>
    public CompiledShaderArtifact? artifact { get; }

    /// <summary>Gets validation and compiler diagnostics.</summary>
    public IReadOnlyList<ShaderDiagnostic> diagnostics { get; }

    /// <summary>Gets whether a complete candidate artifact was produced.</summary>
    public bool succeeded => artifact is not null
        && diagnostics.All(static value => value.severity != ShaderDiagnosticSeverity.Error);
}

internal sealed record ShaderToolRequest(
    ShaderIRStageModule stage,
    ShaderIRPass stagePass,
    ShaderPassDefinition pass,
    ShaderCompileTarget target,
    ShaderVariantKey variant,
    string sourceRoot);

internal sealed record ShaderToolResult(
    byte[]? bytes,
    int exitCode,
    string standardOutput,
    string standardError);

internal interface IShaderCompilerToolchain
{
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

    /// <summary>Creates a compiler backed by the bundled BGFX shaderc executable.</summary>
    public ShaderCompiler()
        : this(new BgfxShadercToolchain())
    {
    }

    internal ShaderCompiler(IShaderCompilerToolchain toolchain)
    {
        m_toolchain = toolchain ?? throw new ArgumentNullException(nameof(toolchain));
    }

    /// <summary>
    /// Compiles a complete shader candidate without replacing any active artifact.
    /// </summary>
    /// <param name="module">Shared handwritten/graph Shader IR.</param>
    /// <param name="target">Target renderer profile and capabilities.</param>
    /// <param name="variant">Static keyword selection.</param>
    /// <param name="sourceRoot">Absolute project Assets directory used for includes and varying definitions.</param>
    /// <param name="cancellationToken">Compilation cancellation.</param>
    /// <returns>A candidate artifact or structured failure diagnostics.</returns>
    public async ValueTask<ShaderCompilationResult> CompileAsync(
        ShaderIRModule module,
        ShaderCompileTarget target,
        ShaderVariantKey variant,
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
                            "SHADERC_FAILED",
                            ShaderDiagnosticSeverity.Error,
                            $"shaderc failed for pass '{pass.definition.name}' stage '{stage.stage}' " +
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
        ShaderVariantKey variant,
        List<ShaderDiagnostic> diagnostics)
    {
        Dictionary<string, ShaderKeywordDefinition> definitions = module.definition.keywords
            .ToDictionary(static value => value.id, StringComparer.Ordinal);
        foreach ((string id, string option) in variant.options)
        {
            if (!definitions.TryGetValue(id, out ShaderKeywordDefinition? definition))
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
