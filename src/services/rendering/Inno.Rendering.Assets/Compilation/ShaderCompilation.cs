using Inno.Core.Diagnostics;
using System;
using System.Collections.Generic;
using System.Linq;
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
    private readonly ShaderPassDefinition m_definition;
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
        definition.metadata = definition.metadata?.ToArray() ?? [];
        m_definition = definition;
        this.stages = Array.AsReadOnly(stages.ToArray());
        this.shaderInterface = shaderInterface;
    }

    /// <summary>
    /// Gets the stable pass definition.
    /// </summary>
    public ShaderPassDefinition definition
    {
        get
        {
            ShaderPassDefinition value = m_definition;
            value.metadata = value.metadata.ToArray();
            return value;
        }
    }

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
    private readonly byte[] m_definitionData;
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
    /// <param name="definitionData">Native-serialized runtime definition captured before asynchronous compilation.</param>
    public CompiledShaderArtifact(
        string shaderName,
        string targetKey,
        RenderShaderVariant variant,
        ShaderInterface shaderInterface,
        IReadOnlyList<CompiledShaderPass> passes,
        ReadOnlySpan<byte> definitionData)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(shaderName);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetKey);
        ArgumentNullException.ThrowIfNull(shaderInterface);
        ArgumentNullException.ThrowIfNull(passes);
        this.shaderName = shaderName;
        this.targetKey = targetKey;
        this.variant = variant;
        this.shaderInterface = shaderInterface;
        this.passes = Array.AsReadOnly(passes.ToArray());
        if (definitionData.IsEmpty) throw new ArgumentException("Compiled programs require a captured runtime contract.", nameof(definitionData));
        m_definitionData = definitionData.ToArray();
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
                    stage.bytes.Span)).ToArray())).ToArray(),
            m_definitionData);

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
        this.diagnostics = Array.AsReadOnly(diagnostics.ToArray());
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
        && diagnostics.All(static value => value.severity != DiagnosticSeverity.Error);
}

/// <summary>
/// Defines a backend-owned target compiler used by the common Shader IR pipeline.
/// </summary>
public interface IShaderCompilerToolchain
{
    /// <summary>Gets the stable source implementation identity paired with this rendering adapter.</summary>
    string implementationId { get; }

    /// <summary>Gets the explicit source-language identities accepted by this adapter's typed IR generator.</summary>
    IReadOnlyList<string> supportedSourceLanguages { get; }

    /// <summary>Generates and compiles one typed stage, including its frozen function modules and resource layout.</summary>
    /// <param name="request">Typed stage and exact target profile.</param>
    /// <param name="cancellationToken">Cancellation before publishing the candidate.</param>
    /// <returns>Immutable compiled bytes, generated binding names and structured diagnostics.</returns>
    ValueTask<ShaderStageToolResult> CompileAsync(ShaderStageToolRequest request, CancellationToken cancellationToken);

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

}

/// <summary>
/// Compiles validated Shader IR through one target toolchain.
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

}
