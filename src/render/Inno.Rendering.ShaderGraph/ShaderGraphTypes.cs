using System;
using System.Collections.Generic;
using Inno.Core.Graphs;
using Inno.Core.Scripting;

namespace Inno.Rendering.ShaderGraph;

/// <summary>
/// Identifies a statically checked shader graph value.
/// </summary>
public enum ShaderValueType
{
    /// <summary>Scalar floating-point value.</summary>
    Float,
    /// <summary>Two-component floating-point vector.</summary>
    Float2,
    /// <summary>Three-component floating-point vector.</summary>
    Float3,
    /// <summary>Four-component floating-point vector.</summary>
    Float4,
    /// <summary>Linear four-component color.</summary>
    Color,
    /// <summary>Four-by-four floating-point matrix.</summary>
    Matrix4x4,
    /// <summary>Two-dimensional sampled texture.</summary>
    Texture2D,
    /// <summary>Layered two-dimensional sampled texture.</summary>
    Texture2DArray,
    /// <summary>Three-dimensional sampled volume texture.</summary>
    Texture3D,
    /// <summary>Cube sampled texture.</summary>
    TextureCube,
    /// <summary>Sampler state.</summary>
    Sampler,
    /// <summary>Structured shader buffer.</summary>
    Buffer
}

/// <summary>
/// Provides stable type identifiers shared by graph ports and shader emitters.
/// </summary>
public static class ShaderGraphValueTypes
{
    /// <summary>Gets the stable type ID for one shader value type.</summary>
    /// <param name="type">Shader value type.</param>
    /// <returns>A stable graph type identifier.</returns>
    public static string GetId(ShaderValueType type) => $"inno.shader.{type}";

    /// <summary>Parses a stable shader graph type identifier.</summary>
    /// <param name="typeId">Stable graph type identifier.</param>
    /// <returns>The represented shader value type.</returns>
    /// <exception cref="ArgumentException">Thrown when the identifier is not a shader value type.</exception>
    public static ShaderValueType Parse(string typeId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(typeId);
        const string prefix = "inno.shader.";
        if (!typeId.StartsWith(prefix, StringComparison.Ordinal)
            || !Enum.TryParse(typeId[prefix.Length..], out ShaderValueType type))
        {
            throw new ArgumentException($"'{typeId}' is not a shader graph value type.", nameof(typeId));
        }

        return type;
    }
}

/// <summary>
/// Applies explicit, directed shader numeric conversions during graph validation.
/// </summary>
public sealed class ShaderGraphTypeConversion : IGraphTypeConversion
{
    /// <inheritdoc />
    public bool CanConvert(string sourceTypeId, string destinationTypeId)
    {
        ShaderValueType source;
        ShaderValueType destination;
        try
        {
            source = ShaderGraphValueTypes.Parse(sourceTypeId);
            destination = ShaderGraphValueTypes.Parse(destinationTypeId);
        }
        catch (ArgumentException)
        {
            return false;
        }

        if (source == destination)
        {
            return true;
        }

        return source == ShaderValueType.Float
            && destination is ShaderValueType.Float2
                or ShaderValueType.Float3
                or ShaderValueType.Float4
                or ShaderValueType.Color
            || source == ShaderValueType.Float3
                && destination is ShaderValueType.Float4 or ShaderValueType.Color
            || source == ShaderValueType.Float4 && destination == ShaderValueType.Color
            || source == ShaderValueType.Color && destination == ShaderValueType.Float4;
    }
}

/// <summary>
/// Marks one reloadable shader node implementation with a stable extension identity.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class ShaderNodeExtensionAttribute : Attribute
{
    /// <summary>Creates a shader node extension declaration.</summary>
    /// <param name="id">Globally stable node definition identifier.</param>
    public ShaderNodeExtensionAttribute(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        this.id = id;
    }

    /// <summary>Gets the globally stable node definition identifier.</summary>
    public string id { get; }
}

/// <summary>
/// Stores one typed canonical shader expression and its source node.
/// </summary>
public readonly record struct ShaderValue
{
    /// <summary>Creates a typed shader expression.</summary>
    /// <param name="type">Static expression type.</param>
    /// <param name="expression">Shaderc-compatible expression text.</param>
    /// <param name="sourceNodeId">Stable source node identity.</param>
    public ShaderValue(ShaderValueType type, string expression, GraphNodeId sourceNodeId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expression);
        this.type = type;
        this.expression = expression;
        this.sourceNodeId = sourceNodeId;
    }

    /// <summary>Gets the static expression type.</summary>
    public ShaderValueType type { get; }

    /// <summary>Gets shaderc-compatible expression text.</summary>
    public string expression { get; }

    /// <summary>Gets the stable source node identity.</summary>
    public GraphNodeId sourceNodeId { get; }
}

/// <summary>
/// Defines shader semantics over a neutral graph node record.
/// </summary>
public abstract class ShaderNodeDefinition : GraphNodeDefinition
{
    /// <summary>Creates a shader node definition.</summary>
    /// <param name="id">Globally stable definition identity.</param>
    /// <param name="displayName">Artist-facing display name.</param>
    /// <param name="category">Search-menu category path.</param>
    /// <param name="supportedStages">Shader stages in which the node may emit code.</param>
    protected ShaderNodeDefinition(
        string id,
        string displayName,
        string category,
        ShaderStage supportedStages)
        : base(id, displayName, category)
    {
        if (supportedStages == ShaderStage.None)
        {
            throw new ArgumentOutOfRangeException(nameof(supportedStages));
        }

        this.supportedStages = supportedStages;
    }

    /// <summary>Gets shader stages in which the node may emit code.</summary>
    public ShaderStage supportedStages { get; }

    /// <summary>Emits typed expressions, statements, properties or output semantics.</summary>
    /// <param name="context">Generation-scoped node emission context.</param>
    public abstract void Emit(ShaderNodeEmitContext context);
}

/// <summary>
/// Exposes validated node inputs and emission sinks to Plugin shader nodes.
/// </summary>
public sealed class ShaderNodeEmitContext
{
    private readonly IReadOnlyDictionary<GraphPortId, ShaderValue> m_inputs;
    private readonly Action<GraphPortId, ShaderValue> m_setOutput;
    private readonly Action<string> m_addStatement;
    private readonly Action<ShaderPropertyDefinition> m_declareProperty;
    private readonly Action<string, ShaderValue> m_setSemantic;

    internal ShaderNodeEmitContext(
        GraphNodeRecord node,
        ShaderStage stage,
        IReadOnlyDictionary<GraphPortId, ShaderValue> inputs,
        Action<GraphPortId, ShaderValue> setOutput,
        Action<string> addStatement,
        Action<ShaderPropertyDefinition> declareProperty,
        Action<string, ShaderValue> setSemantic)
    {
        this.node = node;
        this.stage = stage;
        m_inputs = inputs;
        m_setOutput = setOutput;
        m_addStatement = addStatement;
        m_declareProperty = declareProperty;
        m_setSemantic = setSemantic;
    }

    /// <summary>Gets the neutral source node.</summary>
    public GraphNodeRecord node { get; }

    /// <summary>Gets the stage currently being emitted.</summary>
    public ShaderStage stage { get; }

    /// <summary>Gets a required connected input.</summary>
    /// <param name="portId">Stable input port identity.</param>
    /// <returns>The converted upstream shader value.</returns>
    /// <exception cref="InvalidOperationException">Thrown when no input value is connected.</exception>
    public ShaderValue GetInput(GraphPortId portId)
        => m_inputs.TryGetValue(portId, out ShaderValue value)
            ? value
            : throw new InvalidOperationException($"Input '{portId}' is not connected.");

    /// <summary>Tries to get a connected input.</summary>
    /// <param name="portId">Stable input port identity.</param>
    /// <param name="value">Receives the converted upstream shader value.</param>
    /// <returns><see langword="true"/> when the input is connected.</returns>
    public bool TryGetInput(GraphPortId portId, out ShaderValue value)
        => m_inputs.TryGetValue(portId, out value);

    /// <summary>Publishes one typed node output expression.</summary>
    /// <param name="portId">Stable output port identity.</param>
    /// <param name="value">Typed expression.</param>
    public void SetOutput(GraphPortId portId, ShaderValue value) => m_setOutput(portId, value);

    /// <summary>Adds one complete statement to the current stage body.</summary>
    /// <param name="statement">Shaderc-compatible statement.</param>
    public void AddStatement(string statement)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(statement);
        m_addStatement(statement);
    }

    /// <summary>Declares one stable material property in the shared Shader IR manifest.</summary>
    /// <param name="property">Property declaration.</param>
    public void DeclareProperty(ShaderPropertyDefinition property)
    {
        m_declareProperty(property);
    }

    /// <summary>Publishes one output semantic consumed by the graph target generator.</summary>
    /// <param name="semantic">Stable target semantic.</param>
    /// <param name="value">Typed output expression.</param>
    public void SetSemantic(string semantic, ShaderValue value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(semantic);
        m_setSemantic(semantic, value);
    }
}

/// <summary>Contains one stage-local graph emission requested by a Plugin program output.</summary>
public sealed class ShaderGraphEmission
{
    internal ShaderGraphEmission(
        ShaderStage stage,
        IReadOnlyList<ShaderPropertyDefinition> properties,
        IReadOnlyDictionary<string, ShaderValue> semantics,
        IReadOnlyList<string> statements,
        GraphNodeId outputNodeId)
    {
        this.stage = stage;
        this.properties = properties;
        this.semantics = semantics;
        this.statements = statements;
        this.outputNodeId = outputNodeId;
    }

    /// <summary>Gets the concrete stage used for this emission.</summary>
    public ShaderStage stage { get; }

    /// <summary>Gets stable properties declared by participating nodes.</summary>
    public IReadOnlyList<ShaderPropertyDefinition> properties { get; }

    /// <summary>Gets open output semantics published by the program node.</summary>
    public IReadOnlyDictionary<string, ShaderValue> semantics { get; }

    /// <summary>Gets ordered shaderc-compatible statements emitted by participating nodes.</summary>
    public IReadOnlyList<string> statements { get; }

    /// <summary>Gets the stable program output node identity.</summary>
    public GraphNodeId outputNodeId { get; }

    /// <summary>Gets a required open output semantic.</summary>
    /// <param name="id">Plugin-defined semantic ID.</param>
    /// <returns>The typed emitted value.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the output did not publish the semantic.</exception>
    public ShaderValue GetSemantic(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return semantics.TryGetValue(id, out ShaderValue value)
            ? value
            : throw new InvalidOperationException($"Shader graph output did not publish semantic '{id}'.");
    }
}

/// <summary>Exposes validated graph emission to one Plugin-defined program output node.</summary>
public sealed class ShaderGraphProgramContext
{
    private readonly Func<ShaderStage, ShaderGraphEmission> m_emit;

    internal ShaderGraphProgramContext(
        string assetPath,
        string shaderName,
        GraphDocument document,
        GraphNodeRecord outputNode,
        Func<ShaderStage, ShaderGraphEmission> emit)
    {
        this.assetPath = assetPath;
        this.shaderName = shaderName;
        this.document = document;
        this.outputNode = outputNode;
        m_emit = emit;
    }

    /// <summary>Gets the canonical graph asset path used by diagnostics.</summary>
    public string assetPath { get; }

    /// <summary>Gets the artist-facing generated shader name.</summary>
    public string shaderName { get; }

    /// <summary>Gets the immutable graph view for Plugin-specific metadata inspection.</summary>
    public GraphDocument document { get; }

    /// <summary>Gets the selected Plugin program output node.</summary>
    public GraphNodeRecord outputNode { get; }

    /// <summary>Emits all ancestors of the program output for one concrete shader stage.</summary>
    /// <param name="stage">Vertex, Fragment, or Compute.</param>
    /// <returns>Typed properties, semantics, and statements for that stage.</returns>
    public ShaderGraphEmission Emit(ShaderStage stage) => m_emit(stage);

    /// <summary>Creates a generated stage module with stable output-node source mapping.</summary>
    /// <param name="passName">Owning pass name.</param>
    /// <param name="stage">Concrete shader stage.</param>
    /// <param name="source">Complete shaderc-compatible stage source.</param>
    /// <param name="emission">Emission used to generate the source.</param>
    /// <param name="entryPoint">Compiler entry point.</param>
    /// <returns>A shared Shader IR stage module.</returns>
    public ShaderIRStageModule CreateStage(
        string passName,
        ShaderStage stage,
        string source,
        ShaderGraphEmission emission,
        string entryPoint = "main")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(passName);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentNullException.ThrowIfNull(emission);
        return new ShaderIRStageModule(
            stage,
            entryPoint,
            source,
            ShaderIRSourceKind.Generated,
            new ShaderSourceLocation(
                assetPath,
                passName,
                stage,
                nodeId: emission.outputNodeId.value),
            new Dictionary<int, string> { [1] = emission.outputNodeId.value });
    }
}

/// <summary>Defines the sole graph output that turns open node semantics into arbitrary shared Shader IR.</summary>
public abstract class ShaderGraphProgramNodeDefinition : ShaderNodeDefinition
{
    /// <summary>Creates a Plugin-defined program output.</summary>
    /// <param name="id">Globally stable node identity.</param>
    /// <param name="displayName">Artist-facing display name.</param>
    /// <param name="category">Search-menu category path.</param>
    /// <param name="supportedStages">Stages that the output may request from its ancestors.</param>
    protected ShaderGraphProgramNodeDefinition(
        string id,
        string displayName,
        string category,
        ShaderStage supportedStages)
        : base(id, displayName, category, supportedStages)
    {
    }

    /// <summary>Builds arbitrary passes, techniques, contracts, and stage sources from graph emissions.</summary>
    /// <param name="context">Generation-scoped program build context.</param>
    /// <returns>The complete shared Shader IR module.</returns>
    public abstract ShaderIRModule BuildProgram(ShaderGraphProgramContext context);
}

/// <summary>
/// Owns one atomic generation of shader node definitions by stable identity.
/// </summary>
public sealed class ShaderNodeRegistry : IGraphNodeDefinitionResolver, IDisposable
{
    private readonly object m_sync = new();
    private readonly ShaderNodeExtensionRegistry? m_extensions;
    private ShaderNodeExtensionRegistry.Snapshot m_active;
    private ulong m_generation = 1;
    private bool m_disposed;

    /// <summary>Creates a manual or TypeCache-backed shader node registry.</summary>
    /// <param name="discoverExtensions">
    /// Whether definitions marked with <see cref="ShaderNodeExtensionAttribute"/> are discovered and
    /// replaced transactionally with the active TypeCache generation.
    /// </param>
    public ShaderNodeRegistry(bool discoverExtensions = false)
    {
        m_active = ShaderNodeExtensionRegistry.Snapshot.Create([]);
        if (discoverExtensions)
        {
            m_extensions = new ShaderNodeExtensionRegistry(this);
        }
    }

    /// <summary>Gets active definitions in stable identity order.</summary>
    public IReadOnlyCollection<ShaderNodeDefinition> definitions
    {
        get
        {
            lock (m_sync)
            {
                ObjectDisposedException.ThrowIf(m_disposed, this);
                return new List<ShaderNodeDefinition>(m_active.definitions.Values);
            }
        }
    }

    /// <summary>Gets the monotonic active definition generation used to invalidate graph previews.</summary>
    public ulong generation
    {
        get
        {
            lock (m_sync)
            {
                ObjectDisposedException.ThrowIf(m_disposed, this);
                return m_generation;
            }
        }
    }

    /// <summary>Builds and atomically activates a complete candidate definition snapshot.</summary>
    /// <param name="definitions">Generation-scoped Plugin definitions.</param>
    /// <exception cref="ArgumentException">Thrown when the candidate contains duplicate IDs.</exception>
    public void Replace(IEnumerable<ShaderNodeDefinition> definitions)
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
        ArgumentNullException.ThrowIfNull(definitions);
        if (m_extensions is not null)
        {
            throw new InvalidOperationException(
                "A discovery-backed shader node registry can only change through a TypeCache generation.");
        }

        ShaderNodeExtensionRegistry.Snapshot candidate = ShaderNodeExtensionRegistry.Snapshot.Create(definitions);
        ShaderNodeExtensionRegistry.Snapshot previous = Activate(candidate);
        previous.Dispose();
    }

    /// <summary>Builds the initial extension generation from the active TypeCache snapshot.</summary>
    /// <remarks>
    /// Later TypeCache replacements update the registry as part of the shared transactional reload.
    /// A failed candidate leaves the current definition generation active.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// Thrown when a discovered extension is invalid or duplicates a stable node ID.
    /// </exception>
    [ScriptingApiIgnore]
    public void RefreshExtensions()
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
        m_extensions?.Refresh();
    }

    /// <inheritdoc />
    public bool TryResolve(string definitionId, out GraphNodeDefinition? definition)
    {
        bool found = TryResolveShader(definitionId, out ShaderNodeDefinition? resolved);
        definition = resolved;
        return found;
    }

    /// <summary>Tries to resolve one active shader node definition.</summary>
    /// <param name="definitionId">Stable definition identity.</param>
    /// <param name="definition">Receives the active shader definition.</param>
    /// <returns><see langword="true"/> when the definition is active.</returns>
    public bool TryResolveShader(string definitionId, out ShaderNodeDefinition? definition)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(definitionId);
        lock (m_sync)
        {
            ObjectDisposedException.ThrowIf(m_disposed, this);
            return m_active.definitions.TryGetValue(definitionId, out definition);
        }
    }

    /// <summary>Releases the active node generation and unregisters extension discovery.</summary>
    public void Dispose()
    {
        ShaderNodeExtensionRegistry.Snapshot active;
        bool extensionOwnsActive;
        lock (m_sync)
        {
            if (m_disposed)
            {
                return;
            }

            m_disposed = true;
            active = m_active;
            extensionOwnsActive = m_extensions?.isInitialized == true;
        }

        m_extensions?.Dispose();
        if (!extensionOwnsActive)
        {
            active.Dispose();
        }

        GC.SuppressFinalize(this);
    }

    internal ShaderNodeExtensionRegistry.Snapshot Activate(ShaderNodeExtensionRegistry.Snapshot candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        lock (m_sync)
        {
            ObjectDisposedException.ThrowIf(m_disposed, this);
            ShaderNodeExtensionRegistry.Snapshot previous = m_active;
            m_active = candidate;
            m_generation = checked(m_generation + 1);
            return previous;
        }
    }
}
