using System;
using System.Collections.Generic;
using System.Linq;

namespace Inno.Rendering;

/// <summary>
/// Stores one immutable target shader stage without retaining authoring source or compiler state.
/// </summary>
public sealed class RenderShaderStageArtifact
{
    private readonly byte[] m_bytes;

    /// <summary>
    /// Creates a deployed shader stage.
    /// </summary>
    /// <param name="stage">
    /// The single programmable stage represented by the target binary.
    /// </param>
    /// <param name="bytes">
    /// The non-empty target backend program bytes.
    /// </param>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="stage"/> is not a single stage or <paramref name="bytes"/> is empty.
    /// </exception>
    public RenderShaderStageArtifact(ShaderStage stage, ReadOnlySpan<byte> bytes)
    {
        if (stage is not ShaderStage.Vertex and not ShaderStage.Fragment and not ShaderStage.Compute)
            throw new ArgumentException("A deployed shader stage must identify one programmable stage.", nameof(stage));
        if (bytes.IsEmpty)
            throw new ArgumentException("A deployed shader stage cannot be empty.", nameof(bytes));
        this.stage = stage;
        m_bytes = bytes.ToArray();
    }

    /// <summary>
    /// Gets the programmable stage represented by this artifact.
    /// </summary>
    public ShaderStage stage { get; }

    /// <summary>
    /// Gets the immutable target backend program bytes.
    /// </summary>
    public ReadOnlyMemory<byte> bytes => m_bytes;
}

/// <summary>
/// Carries one immutable compiled shader pass and its reflected runtime bindings.
/// </summary>
public sealed class RenderShaderPassArtifact
{
    private readonly IReadOnlyList<RenderShaderStageArtifact> m_stages;

    /// <summary>
    /// Creates a deployed shader pass.
    /// </summary>
    /// <param name="name">
    /// The stable pass name within the owning shader.
    /// </param>
    /// <param name="programKind">
    /// The programmable stage combination used by the pass.
    /// </param>
    /// <param name="rasterState">
    /// The backend-neutral fixed-function state used by raster passes.
    /// </param>
    /// <param name="shaderInterface">
    /// The validated pass-local resource binding contract.
    /// </param>
    /// <param name="stages">
    /// The complete target stage binaries required by the pass.
    /// </param>
    /// <exception cref="ArgumentException">
    /// Thrown when the name, stage set, or program kind is invalid.
    /// </exception>
    public RenderShaderPassArtifact(
        string name,
        ShaderProgramKind programKind,
        RenderRasterState rasterState,
        ShaderInterface shaderInterface,
        IReadOnlyList<RenderShaderStageArtifact> stages)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (!Enum.IsDefined(programKind))
            throw new ArgumentOutOfRangeException(nameof(programKind));
        ArgumentNullException.ThrowIfNull(rasterState);
        ArgumentNullException.ThrowIfNull(shaderInterface);
        ArgumentNullException.ThrowIfNull(stages);
        RenderShaderStageArtifact[] stageSnapshot = stages.ToArray();
        ValidateStages(programKind, stageSnapshot);
        this.name = name;
        this.programKind = programKind;
        this.rasterState = CloneRasterState(rasterState);
        this.shaderInterface = CloneInterface(shaderInterface);
        m_stages = Array.AsReadOnly(stageSnapshot);
    }

    /// <summary>
    /// Gets the stable pass name within the owning shader.
    /// </summary>
    public string name { get; }

    /// <summary>
    /// Gets the programmable stage combination used by this pass.
    /// </summary>
    public ShaderProgramKind programKind { get; }

    /// <summary>
    /// Gets an immutable copy of the backend-neutral raster state.
    /// </summary>
    public RenderRasterState rasterState { get; }

    /// <summary>
    /// Gets the validated pass-local resource binding contract.
    /// </summary>
    public ShaderInterface shaderInterface { get; }

    /// <summary>
    /// Gets the complete target stage binaries required by this pass.
    /// </summary>
    public IReadOnlyList<RenderShaderStageArtifact> stages => m_stages;

    internal static ShaderInterface CloneInterface(ShaderInterface source)
        => new(source.bindings.Select(static binding => new ShaderInterfaceBinding(
            binding.id,
            binding.type,
            binding.stages,
            binding.arrayCount,
            binding.bindingKind,
            binding.storageAccess)).ToArray());

    internal static RenderRasterState CloneRasterState(RenderRasterState source)
        => new()
        {
            topology = source.topology,
            cull = source.cull,
            frontFace = source.frontFace,
            depthCompare = source.depthCompare,
            depthWrite = source.depthWrite,
            blend = source.blend,
            colorWriteMask = source.colorWriteMask,
            multisampling = source.multisampling
        };

    private static void ValidateStages(
        ShaderProgramKind programKind,
        IReadOnlyCollection<RenderShaderStageArtifact> stages)
    {
        ShaderStage[] actual = stages.Select(static value => value.stage).Order().ToArray();
        ShaderStage[] expected = programKind == ShaderProgramKind.Raster
            ? [ShaderStage.Vertex, ShaderStage.Fragment]
            : [ShaderStage.Compute];
        if (!actual.SequenceEqual(expected))
        {
            throw new ArgumentException(
                $"Program kind '{programKind}' requires stages '{string.Join(", ", expected)}'.",
                nameof(stages));
        }
    }
}

/// <summary>
/// Contains one immutable, source-free shader artifact ready for runtime GPU resource creation.
/// </summary>
public sealed class RenderShaderArtifact
{
    private readonly IReadOnlyList<RenderShaderPassArtifact> m_passes;

    /// <summary>
    /// Creates a complete deployed shader artifact.
    /// </summary>
    /// <param name="shaderName">
    /// The stable shader name expected by the runtime asset definition.
    /// </param>
    /// <param name="targetKey">
    /// The target compiler profile and policy identity.
    /// </param>
    /// <param name="variant">
    /// The canonical static keyword selection.
    /// </param>
    /// <param name="shaderInterface">
    /// The complete validated shader resource binding contract.
    /// </param>
    /// <param name="passes">
    /// Every source-free pass available to the runtime shader definition.
    /// </param>
    /// <exception cref="ArgumentException">
    /// Thrown when a required identity is empty, no pass exists, or pass names are duplicated.
    /// </exception>
    public RenderShaderArtifact(
        string shaderName,
        string targetKey,
        RenderShaderVariant variant,
        ShaderInterface shaderInterface,
        IReadOnlyList<RenderShaderPassArtifact> passes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(shaderName);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetKey);
        ArgumentNullException.ThrowIfNull(shaderInterface);
        ArgumentNullException.ThrowIfNull(passes);
        RenderShaderPassArtifact[] passSnapshot = passes.ToArray();
        if (passSnapshot.Length == 0)
            throw new ArgumentException("A deployed shader artifact must contain at least one pass.", nameof(passes));
        if (passSnapshot.Select(static pass => pass.name).Distinct(StringComparer.Ordinal).Count() != passSnapshot.Length)
            throw new ArgumentException("A deployed shader artifact cannot repeat a pass name.", nameof(passes));
        this.shaderName = shaderName;
        this.targetKey = targetKey;
        this.variant = variant;
        this.shaderInterface = RenderShaderPassArtifact.CloneInterface(shaderInterface);
        m_passes = Array.AsReadOnly(passSnapshot);
    }

    /// <summary>
    /// Gets the stable shader name expected by the runtime asset definition.
    /// </summary>
    public string shaderName { get; }

    /// <summary>
    /// Gets the target compiler profile and policy identity.
    /// </summary>
    public string targetKey { get; }

    /// <summary>
    /// Gets the canonical static keyword selection.
    /// </summary>
    public RenderShaderVariant variant { get; }

    /// <summary>
    /// Gets the complete validated shader resource binding contract.
    /// </summary>
    public ShaderInterface shaderInterface { get; }

    /// <summary>
    /// Gets every source-free pass available to the runtime shader definition.
    /// </summary>
    public IReadOnlyList<RenderShaderPassArtifact> passes => m_passes;
}
