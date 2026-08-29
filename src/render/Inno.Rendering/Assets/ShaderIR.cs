using System;
using System.Collections.Generic;
using System.Linq;
using Inno.Rendering.Core;

namespace Inno.Rendering;

/// <summary>
/// Identifies whether stage code originated in a handwritten source or a graph emitter.
/// </summary>
public enum ShaderIRSourceKind
{
    /// <summary>Code was authored in a shader source file.</summary>
    Handwritten,
    /// <summary>Code was emitted from a validated node graph.</summary>
    Generated
}

/// <summary>
/// Identifies the severity of a shader compilation diagnostic.
/// </summary>
public enum ShaderDiagnosticSeverity
{
    /// <summary>Informational compiler context.</summary>
    Info,
    /// <summary>A recoverable issue that does not invalidate the artifact.</summary>
    Warning,
    /// <summary>An issue that invalidates the candidate artifact.</summary>
    Error
}

/// <summary>
/// Maps a shader IR range back to an asset, pass, stage, graph node and source position.
/// </summary>
public readonly record struct ShaderSourceLocation
{
    /// <summary>
    /// Creates a shader source location.
    /// </summary>
    /// <param name="assetPath">Project-relative source asset path.</param>
    /// <param name="passName">Stable pass name.</param>
    /// <param name="stage">Shader stage.</param>
    /// <param name="line">One-based source line, or zero when unavailable.</param>
    /// <param name="column">One-based source column, or zero when unavailable.</param>
    /// <param name="nodeId">Stable graph node ID, or an empty string for handwritten code.</param>
    public ShaderSourceLocation(
        string assetPath,
        string passName,
        ShaderStage stage,
        int line = 0,
        int column = 0,
        string nodeId = "")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assetPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(passName);
        ArgumentOutOfRangeException.ThrowIfNegative(line);
        ArgumentOutOfRangeException.ThrowIfNegative(column);
        this.assetPath = assetPath;
        this.passName = passName;
        this.stage = stage;
        this.line = line;
        this.column = column;
        this.nodeId = nodeId ?? string.Empty;
    }

    /// <summary>Gets the project-relative source asset path.</summary>
    public string assetPath { get; }

    /// <summary>Gets the stable pass name.</summary>
    public string passName { get; }

    /// <summary>Gets the shader stage.</summary>
    public ShaderStage stage { get; }

    /// <summary>Gets the one-based line, or zero when unavailable.</summary>
    public int line { get; }

    /// <summary>Gets the one-based column, or zero when unavailable.</summary>
    public int column { get; }

    /// <summary>Gets the stable graph node ID, or an empty string for handwritten code.</summary>
    public string nodeId { get; }
}

/// <summary>
/// Reports one structured shader validation or compilation issue.
/// </summary>
public sealed class ShaderDiagnostic
{
    /// <summary>
    /// Creates a shader diagnostic.
    /// </summary>
    /// <param name="code">Stable diagnostic code.</param>
    /// <param name="severity">Diagnostic severity.</param>
    /// <param name="message">Artist-facing message.</param>
    /// <param name="location">Optional source mapping.</param>
    public ShaderDiagnostic(
        string code,
        ShaderDiagnosticSeverity severity,
        string message,
        ShaderSourceLocation? location = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        this.code = code;
        this.severity = severity;
        this.message = message;
        this.location = location;
    }

    /// <summary>Gets the stable diagnostic code.</summary>
    public string code { get; }

    /// <summary>Gets the diagnostic severity.</summary>
    public ShaderDiagnosticSeverity severity { get; }

    /// <summary>Gets the artist-facing message.</summary>
    public string message { get; }

    /// <summary>Gets the optional source mapping.</summary>
    public ShaderSourceLocation? location { get; }
}

/// <summary>
/// Declares one canonical stage source in the shared handwritten/graph shader IR.
/// </summary>
public sealed class ShaderIRStageModule
{
    /// <summary>
    /// Creates a canonical stage module.
    /// </summary>
    /// <param name="stage">Single shader stage represented by the source.</param>
    /// <param name="entryPoint">Entry point passed to the target compiler.</param>
    /// <param name="source">Shaderc-compatible canonical source.</param>
    /// <param name="sourceKind">Original source kind.</param>
    /// <param name="location">Root source mapping.</param>
    /// <param name="lineNodeIds">Optional generated-source line to stable node ID mapping.</param>
    public ShaderIRStageModule(
        ShaderStage stage,
        string entryPoint,
        string source,
        ShaderIRSourceKind sourceKind,
        ShaderSourceLocation location,
        IReadOnlyDictionary<int, string>? lineNodeIds = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entryPoint);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        this.stage = stage;
        this.entryPoint = entryPoint;
        this.source = source;
        this.sourceKind = sourceKind;
        this.location = location;
        this.lineNodeIds = lineNodeIds ?? new Dictionary<int, string>();
    }

    /// <summary>Gets the single shader stage.</summary>
    public ShaderStage stage { get; }

    /// <summary>Gets the compiler entry point.</summary>
    public string entryPoint { get; }

    /// <summary>Gets shaderc-compatible canonical source.</summary>
    public string source { get; }

    /// <summary>Gets whether source was handwritten or graph-generated.</summary>
    public ShaderIRSourceKind sourceKind { get; }

    /// <summary>Gets the root source mapping.</summary>
    public ShaderSourceLocation location { get; }

    /// <summary>Gets generated-source line to stable graph node mappings.</summary>
    public IReadOnlyDictionary<int, string> lineNodeIds { get; }
}

/// <summary>
/// Combines one pass manifest with its canonical stage modules.
/// </summary>
public sealed class ShaderIRPass
{
    private readonly IReadOnlyList<ShaderPropertyId> m_bindingIds;

    /// <summary>
    /// Creates a shader IR pass.
    /// </summary>
    /// <param name="definition">Pass interface and fixed-function state.</param>
    /// <param name="stages">Canonical stage modules.</param>
    /// <param name="generatedVaryingSource">Optional graph-generated varying definition content.</param>
    /// <param name="bindingIds">
    /// Optional pass-local property IDs. A null value uses every manifest property visible to this pass.
    /// </param>
    public ShaderIRPass(
        ShaderPassDefinition definition,
        IReadOnlyList<ShaderIRStageModule> stages,
        string? generatedVaryingSource = null,
        IReadOnlyList<ShaderPropertyId>? bindingIds = null)
    {
        ArgumentNullException.ThrowIfNull(stages);
        this.definition = definition;
        this.stages = stages;
        this.generatedVaryingSource = generatedVaryingSource;
        usesAllBindings = bindingIds is null;
        m_bindingIds = bindingIds?.ToArray() ?? Array.Empty<ShaderPropertyId>();
    }

    /// <summary>Gets the pass interface and fixed-function state.</summary>
    public ShaderPassDefinition definition { get; }

    /// <summary>Gets canonical stage modules.</summary>
    public IReadOnlyList<ShaderIRStageModule> stages { get; }

    /// <summary>Gets optional graph-generated varying definition content.</summary>
    public string? generatedVaryingSource { get; }

    /// <summary>
    /// Gets whether this pass consumes every manifest property visible to one of its stages.
    /// </summary>
    public bool usesAllBindings { get; }

    /// <summary>
    /// Gets explicit pass-local property IDs when <see cref="usesAllBindings"/> is false.
    /// </summary>
    public IReadOnlyList<ShaderPropertyId> bindingIds => m_bindingIds;
}

/// <summary>
/// Provides the single source of truth consumed by every shader compiler backend.
/// </summary>
public sealed class ShaderIRModule
{
    /// <summary>
    /// Creates a shader IR module.
    /// </summary>
    /// <param name="definition">Stable properties, keywords and pass manifest.</param>
    /// <param name="passes">Canonical stage implementations.</param>
    public ShaderIRModule(ShaderDefinition definition, IReadOnlyList<ShaderIRPass> passes)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(passes);
        this.definition = definition;
        this.passes = passes;
    }

    /// <summary>Gets stable properties, keywords and pass declarations.</summary>
    public ShaderDefinition definition { get; }

    /// <summary>Gets canonical pass stage implementations.</summary>
    public IReadOnlyList<ShaderIRPass> passes { get; }
}

/// <summary>
/// Describes one reflected material binding expected by compiled programs.
/// </summary>
public sealed class ShaderInterfaceBinding
{
    /// <summary>
    /// Creates a reflected interface binding.
    /// </summary>
    /// <param name="id">Stable property ID.</param>
    /// <param name="type">Expected value or resource type.</param>
    /// <param name="stages">Stages that consume the binding.</param>
    /// <param name="arrayCount">Required array element count.</param>
    /// <param name="bindingKind">Backend-neutral interface binding domain.</param>
    /// <param name="storageAccess">Required access for storage resources.</param>
    public ShaderInterfaceBinding(
        ShaderPropertyId id,
        ShaderPropertyType type,
        ShaderStage stages,
        int arrayCount = 1,
        ShaderPropertyBindingKind bindingKind = ShaderPropertyBindingKind.Uniform,
        RenderStorageAccess storageAccess = RenderStorageAccess.Read)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(arrayCount);
        if (!Enum.IsDefined(bindingKind))
            throw new ArgumentOutOfRangeException(nameof(bindingKind));
        if (!Enum.IsDefined(storageAccess))
            throw new ArgumentOutOfRangeException(nameof(storageAccess));
        this.id = id;
        this.type = type;
        this.stages = stages;
        this.arrayCount = arrayCount;
        this.bindingKind = bindingKind;
        this.storageAccess = storageAccess;
    }

    /// <summary>Gets the stable property ID.</summary>
    public ShaderPropertyId id { get; }

    /// <summary>Gets the expected value or resource type.</summary>
    public ShaderPropertyType type { get; }

    /// <summary>Gets stages that consume the binding.</summary>
    public ShaderStage stages { get; }

    /// <summary>Gets the required array element count.</summary>
    public int arrayCount { get; }

    /// <summary>Gets the backend-neutral interface binding domain.</summary>
    public ShaderPropertyBindingKind bindingKind { get; }

    /// <summary>Gets required access for storage resources.</summary>
    public RenderStorageAccess storageAccess { get; }
}

/// <summary>
/// Stores the manifest-derived binding contract verified after backend program creation.
/// </summary>
public sealed class ShaderInterface
{
    /// <summary>
    /// Creates a shader interface contract.
    /// </summary>
    /// <param name="bindings">Stable expected bindings.</param>
    public ShaderInterface(IReadOnlyList<ShaderInterfaceBinding> bindings)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        this.bindings = bindings;
    }

    /// <summary>Gets stable expected bindings.</summary>
    public IReadOnlyList<ShaderInterfaceBinding> bindings { get; }

    /// <summary>Builds an interface from a validated shader manifest.</summary>
    /// <param name="module">Validated shader IR.</param>
    /// <returns>The expected runtime binding interface.</returns>
    public static ShaderInterface FromModule(ShaderIRModule module)
    {
        ArgumentNullException.ThrowIfNull(module);
        return new ShaderInterface(module.definition.properties
            .Select(static property => new ShaderInterfaceBinding(
                property.id,
                property.type,
                property.stages,
                bindingKind: property.bindingKind,
                storageAccess: property.storageAccess))
            .ToArray());
    }

    /// <summary>Builds the interface consumed by one validated shader pass.</summary>
    /// <param name="module">Validated shader IR containing the pass and property manifest.</param>
    /// <param name="pass">Pass whose stage-local interface is required.</param>
    /// <returns>The ordered binding interface used to create and reflect this pass.</returns>
    /// <exception cref="ArgumentException">
    /// Thrown when the pass declares a property ID absent from the module manifest.
    /// </exception>
    public static ShaderInterface FromPass(ShaderIRModule module, ShaderIRPass pass)
    {
        ArgumentNullException.ThrowIfNull(module);
        ArgumentNullException.ThrowIfNull(pass);
        ShaderStage stageMask = pass.stages.Aggregate(
            ShaderStage.None,
            static (current, stage) => current | stage.stage);
        HashSet<string>? selected = pass.usesAllBindings
            ? null
            : pass.bindingIds.Select(static value => value.value).ToHashSet(StringComparer.Ordinal);
        if (selected is not null)
        {
            string? missing = selected.FirstOrDefault(id => !module.definition.properties.Any(property =>
                string.Equals(property.id.value, id, StringComparison.Ordinal)));
            if (missing is not null)
            {
                throw new ArgumentException(
                    $"Pass '{pass.definition.name}' declares unknown shader property '{missing}'.",
                    nameof(pass));
            }
        }

        return new ShaderInterface(module.definition.properties
            .Where(property => (property.stages & stageMask) != 0
                && (selected is null || selected.Contains(property.id.value)))
            .Select(property => new ShaderInterfaceBinding(
                property.id,
                property.type,
                property.stages & stageMask,
                bindingKind: property.bindingKind,
                storageAccess: property.storageAccess))
            .ToArray());
    }
}

/// <summary>
/// Returns immutable shader IR validation diagnostics.
/// </summary>
public sealed class ShaderIRValidationResult
{
    /// <summary>
    /// Creates a shader IR validation result.
    /// </summary>
    /// <param name="diagnostics">Validation diagnostics.</param>
    public ShaderIRValidationResult(IReadOnlyList<ShaderDiagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        this.diagnostics = diagnostics;
    }

    /// <summary>Gets validation diagnostics.</summary>
    public IReadOnlyList<ShaderDiagnostic> diagnostics { get; }

    /// <summary>Gets whether no error diagnostic was produced.</summary>
    public bool succeeded => diagnostics.All(static value => value.severity != ShaderDiagnosticSeverity.Error);
}

/// <summary>
/// Validates the shared handwritten/graph shader IR before target compilation.
/// </summary>
public static class ShaderIRValidator
{
    /// <summary>
    /// Validates stable identities, stage composition and target capability requirements.
    /// </summary>
    /// <param name="module">Candidate shader IR module.</param>
    /// <param name="capabilities">Optional target capability snapshot.</param>
    /// <returns>Structured diagnostics suitable for source and graph navigation.</returns>
    public static ShaderIRValidationResult Validate(
        ShaderIRModule module,
        GraphicsCapabilities? capabilities = null)
    {
        ArgumentNullException.ThrowIfNull(module);
        var diagnostics = new List<ShaderDiagnostic>();

        AddDuplicateDiagnostics(
            module.definition.properties.Select(static value => value.id.value),
            "SHADER_IR_DUPLICATE_PROPERTY",
            "property",
            diagnostics);
        foreach (ShaderPropertyDefinition property in module.definition.properties)
        {
            if (!ShaderPropertyDefinition.IsBindingKindCompatible(property.type, property.bindingKind))
            {
                diagnostics.Add(Error(
                    "SHADER_IR_PROPERTY_BINDING_INCOMPATIBLE",
                    $"Shader property '{property.id}' type '{property.type}' is incompatible with "
                    + $"binding kind '{property.bindingKind}'."));
            }
            if (!Enum.IsDefined(property.storageAccess))
            {
                diagnostics.Add(Error(
                    "SHADER_IR_STORAGE_ACCESS_INVALID",
                    $"Shader property '{property.id}' declares invalid storage access."));
            }
        }
        AddDuplicateDiagnostics(
            module.definition.keywords.Select(static value => value.id),
            "SHADER_IR_DUPLICATE_KEYWORD",
            "keyword",
            diagnostics);
        AddDuplicateDiagnostics(
            module.definition.passes.Select(static value => value.name),
            "SHADER_IR_DUPLICATE_PASS",
            "pass",
            diagnostics);
        AddDuplicateDiagnostics(
            module.definition.techniques.Select(static value => value.id.value),
            "SHADER_IR_DUPLICATE_TECHNIQUE",
            "technique",
            diagnostics);

        HashSet<string> declaredPassNames = module.definition.passes
            .Select(static value => value.name)
            .ToHashSet(StringComparer.Ordinal);
        foreach (ShaderTechniqueDefinition technique in module.definition.techniques)
        {
            AddDuplicateDiagnostics(
                technique.passes.Select(static value => value.role.value),
                "SHADER_IR_DUPLICATE_TECHNIQUE_ROLE",
                $"role in technique '{technique.id}'",
                diagnostics);
            foreach (string missingPass in technique.passes
                         .Select(static value => value.passName)
                         .Where(value => !declaredPassNames.Contains(value))
                         .Distinct(StringComparer.Ordinal))
            {
                diagnostics.Add(Error(
                    "SHADER_IR_UNKNOWN_TECHNIQUE_PASS",
                    $"Technique '{technique.id}' maps to unknown pass '{missingPass}'."));
            }
        }

        Dictionary<string, ShaderIRPass> implementations = module.passes
            .GroupBy(static value => value.definition.name, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.First(), StringComparer.Ordinal);
        if (implementations.Count != module.passes.Count)
        {
            diagnostics.Add(Error(
                "SHADER_IR_DUPLICATE_IMPLEMENTATION",
                "The IR contains more than one implementation for the same pass."));
        }

        HashSet<string> propertyIds = module.definition.properties
            .Select(static value => value.id.value)
            .ToHashSet(StringComparer.Ordinal);
        foreach (ShaderIRPass pass in module.passes.Where(static value => !value.usesAllBindings))
        {
            AddDuplicateDiagnostics(
                pass.bindingIds.Select(static value => value.value),
                "SHADER_IR_DUPLICATE_PASS_BINDING",
                $"binding in pass '{pass.definition.name}'",
                diagnostics);
            foreach (string missing in pass.bindingIds
                         .Select(static value => value.value)
                         .Where(value => !propertyIds.Contains(value))
                         .Distinct(StringComparer.Ordinal))
            {
                diagnostics.Add(Error(
                    "SHADER_IR_UNKNOWN_PASS_BINDING",
                    $"Pass '{pass.definition.name}' declares unknown shader property '{missing}'."));
            }
        }

        foreach (ShaderPassDefinition definition in module.definition.passes)
        {
            if (!implementations.TryGetValue(definition.name, out ShaderIRPass? pass))
            {
                diagnostics.Add(Error(
                    "SHADER_IR_MISSING_PASS",
                    $"Pass '{definition.name}' has no IR implementation."));
                continue;
            }

            ValidatePass(definition, pass, capabilities, diagnostics);
        }

        foreach (ShaderIRPass pass in module.passes)
        {
            if (!module.definition.passes.Any(value =>
                    string.Equals(value.name, pass.definition.name, StringComparison.Ordinal)))
            {
                diagnostics.Add(Error(
                    "SHADER_IR_UNDECLARED_PASS",
                    $"IR pass '{pass.definition.name}' is not present in the shader definition."));
            }
        }

        return new ShaderIRValidationResult(diagnostics);
    }

    private static void ValidatePass(
        ShaderPassDefinition definition,
        ShaderIRPass pass,
        GraphicsCapabilities? capabilities,
        List<ShaderDiagnostic> diagnostics)
    {
        if (definition.programKind != pass.definition.programKind
            || definition.requiredFeatures != pass.definition.requiredFeatures)
        {
            diagnostics.Add(Error(
                "SHADER_IR_PASS_CONTRACT_MISMATCH",
                $"IR implementation for pass '{definition.name}' does not match its manifest."));
        }

        if (capabilities is not null
            && (definition.requiredFeatures & ~capabilities.features) != GraphicsFeature.None)
        {
            diagnostics.Add(Error(
                "SHADER_IR_CAPABILITY_UNAVAILABLE",
                $"Pass '{definition.name}' requires unavailable features " +
                $"'{definition.requiredFeatures & ~capabilities.features}'.",
                severity: ShaderDiagnosticSeverity.Warning));
        }

        var stages = new HashSet<ShaderStage>();
        foreach (ShaderIRStageModule stage in pass.stages)
        {
            if (stage.stage is not (ShaderStage.Vertex or ShaderStage.Fragment or ShaderStage.Compute))
            {
                diagnostics.Add(Error(
                    "SHADER_IR_INVALID_STAGE",
                    $"Pass '{definition.name}' contains an invalid stage mask '{stage.stage}'.",
                    stage.location));
            }
            else if (!stages.Add(stage.stage))
            {
                diagnostics.Add(Error(
                    "SHADER_IR_DUPLICATE_STAGE",
                    $"Pass '{definition.name}' contains stage '{stage.stage}' more than once.",
                    stage.location));
            }

            if (stage.location.stage != stage.stage)
            {
                diagnostics.Add(Error(
                    "SHADER_IR_SOURCE_STAGE_MISMATCH",
                    $"Source mapping for pass '{definition.name}' does not match stage '{stage.stage}'.",
                    stage.location));
            }

            foreach ((int line, string nodeId) in stage.lineNodeIds)
            {
                if (line <= 0 || string.IsNullOrWhiteSpace(nodeId))
                {
                    diagnostics.Add(Error(
                        "SHADER_IR_INVALID_NODE_MAP",
                        $"Pass '{definition.name}' contains an invalid generated-source node mapping.",
                        stage.location));
                    break;
                }
            }
        }

        bool compute = stages.Contains(ShaderStage.Compute);
        if (compute && stages.Count != 1)
        {
            diagnostics.Add(Error(
                "SHADER_IR_MIXED_COMPUTE_RASTER",
                $"Pass '{definition.name}' mixes compute and raster stages."));
        }
        else if (compute && definition.programKind != ShaderProgramKind.Compute)
        {
            diagnostics.Add(Error(
                "SHADER_IR_COMPUTE_KIND_REQUIRED",
                $"Compute pass '{definition.name}' must declare the compute program kind."));
        }
        else if (!compute
            && (definition.programKind != ShaderProgramKind.Raster
                || !stages.Contains(ShaderStage.Vertex)
                || !stages.Contains(ShaderStage.Fragment)))
        {
            diagnostics.Add(Error(
                "SHADER_IR_RASTER_STAGE_PAIR_REQUIRED",
                $"Raster pass '{definition.name}' requires one vertex and one fragment stage."));
        }
    }

    private static void AddDuplicateDiagnostics(
        IEnumerable<string> values,
        string code,
        string kind,
        List<ShaderDiagnostic> diagnostics)
    {
        foreach (string value in values
                     .GroupBy(static value => value, StringComparer.Ordinal)
                     .Where(static group => group.Count() > 1)
                     .Select(static group => group.Key))
        {
            diagnostics.Add(Error(code, $"Shader {kind} ID '{value}' is declared more than once."));
        }
    }

    private static ShaderDiagnostic Error(
        string code,
        string message,
        ShaderSourceLocation? location = null,
        ShaderDiagnosticSeverity severity = ShaderDiagnosticSeverity.Error)
        => new(code, severity, message, location);
}
