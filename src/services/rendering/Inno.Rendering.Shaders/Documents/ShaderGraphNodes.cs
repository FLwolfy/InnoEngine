using System;
using System.Collections.Generic;
using System.Linq;
using Inno.Core.Graphs;
using Inno.Core.Serialization;

namespace Inno.Rendering.Shaders;

/// <summary>Chooses whether a graph-authored node is inlined or consumed by a domain Target.</summary>
public enum ShaderGraphNodeKind
{
    /// <summary>Inlines the node graph into the caller before Target expansion and typed lowering.</summary>
    Function,
    /// <summary>Leaves the node as a typed domain boundary for the selected Shader Target.</summary>
    DomainOutput
}

/// <summary>Declares whether a graph-authored function is pure or intentionally emits ordered effects.</summary>
public enum ShaderGraphNodeEffect
{
    /// <summary>The function only computes returned values and therefore requires at least one output.</summary>
    Pure,
    /// <summary>The function may contain ordered GPU effects and can intentionally expose no returned values.</summary>
    SideEffect
}

/// <summary>Stores graph-level identity and catalog metadata for a reusable Shader node.</summary>
public sealed class ShaderGraphNodeSettings : ISerializable
{
    /// <summary>Gets or sets the node title displayed to authors.</summary>
    [SerializableProperty] public string displayName { get; set; } = "Graph Node";
    /// <summary>Gets or sets the slash-separated creation catalog beneath Graph Nodes.</summary>
    [SerializableProperty] public string createPath { get; set; } = "General";
    /// <summary>Gets or sets the deterministic order within the creation catalog.</summary>
    [SerializableProperty] public int createOrder { get; set; }
    /// <summary>Gets or sets how a reference to this graph participates in compilation.</summary>
    [SerializableProperty] public ShaderGraphNodeKind kind { get; set; }
    /// <summary>Gets or sets the observable computation behavior of an inline function.</summary>
    [SerializableProperty] public ShaderGraphNodeEffect effect { get; set; }
    /// <summary>Gets or sets the domain role consumed by a Target; empty for ordinary inline functions.</summary>
    [SerializableProperty] public string role { get; set; } = "";
}

/// <summary>Declares one stable, typed port on a graph-authored node interface.</summary>
public sealed class ShaderGraphNodePortDefinition : ISerializable
{
    /// <summary>Gets or sets the stable node-local port identity.</summary>
    [SerializableProperty] public string id { get; set; } = "value";
    /// <summary>Gets or sets the complete backend-neutral value type.</summary>
    [SerializableProperty] public ShaderGraphType type { get; set; } = new() { id = "float" };
    /// <summary>Gets or sets whether callers must connect the input instead of using an explicit/default zero.</summary>
    [SerializableProperty] public bool required { get; set; } = true;

    internal ShaderNodePort ToPort(GraphPortDirection direction)
        => new(id, type.CreateType(), direction, direction == GraphPortDirection.Input && required);
}

/// <summary>Stores values supplied by callers to a graph-authored node.</summary>
public sealed class ShaderGraphNodeInputSettings : ISerializable
{
    /// <summary>Gets or sets values supplied by callers and exposed as outputs inside the node graph.</summary>
    [SerializableProperty] public ShaderGraphNodePortDefinition[] ports { get; set; } = [];
}

/// <summary>Stores values returned by a graph-authored function node.</summary>
public sealed class ShaderGraphNodeOutputSettings : ISerializable
{
    /// <summary>Gets or sets values collected inside the node graph and exposed as outputs to callers.</summary>
    [SerializableProperty] public ShaderGraphNodePortDefinition[] ports { get; set; } = [];
}

/// <summary>Freezes the complete public interface resolved from one graph-authored node asset.</summary>
public sealed class ShaderGraphNodeInterface : ISerializable
{
    /// <summary>Gets or sets the node title displayed to authors.</summary>
    [SerializableProperty] public string displayName { get; set; } = "Graph Node";
    /// <summary>Gets or sets the slash-separated creation catalog beneath Graph Nodes.</summary>
    [SerializableProperty] public string createPath { get; set; } = "General";
    /// <summary>Gets or sets the deterministic order within the creation catalog.</summary>
    [SerializableProperty] public int createOrder { get; set; }
    /// <summary>Gets or sets whether the graph is inline computation or a Target-owned output boundary.</summary>
    [SerializableProperty] public ShaderGraphNodeKind kind { get; set; }
    /// <summary>Gets or sets whether an inline function is pure or intentionally emits ordered effects.</summary>
    [SerializableProperty] public ShaderGraphNodeEffect effect { get; set; }
    /// <summary>Gets or sets the Target-owned role for a domain output.</summary>
    [SerializableProperty] public string role { get; set; } = "";
    /// <summary>Gets or sets externally supplied inputs.</summary>
    [SerializableProperty] public ShaderGraphNodePortDefinition[] inputs { get; set; } = [];
    /// <summary>Gets or sets externally visible results.</summary>
    [SerializableProperty] public ShaderGraphNodePortDefinition[] outputs { get; set; } = [];

    /// <summary>Gets detached typed ports in deterministic input-then-output order.</summary>
    /// <returns>A provider-free port snapshot suitable for Editor presentation and validation.</returns>
    public IReadOnlyList<ShaderNodePort> GetPorts()
        => inputs.Select(static value => value.ToPort(GraphPortDirection.Input))
            .Concat(outputs.Select(static value => value.ToPort(GraphPortDirection.Output)))
            .ToArray();
}

/// <summary>Reads and expands reusable node graphs without retaining assets or provider instances.</summary>
public static class ShaderGraphNodes
{
    /// <summary>Identifies the single multi-port node-input interface record.</summary>
    public const string inputDefinitionId = "inno.shader.node-inputs";
    /// <summary>Identifies the optional multi-port node-output interface record.</summary>
    public const string outputDefinitionId = "inno.shader.node-outputs";
    /// <summary>Identifies a reference to another Shader graph used as a node.</summary>
    public const string callDefinitionId = "inno.shader.graph-node";
    /// <summary>Identifies the serialized interface snapshot retained by a graph-node reference.</summary>
    public const string interfaceKey = "interface";
    /// <summary>Identifies graph-level reusable-node metadata, independent of either interface direction.</summary>
    public const string settingsKey = "inno.shader.node-settings";

    /// <summary>Determines whether a Shader graph declares a reusable node interface.</summary>
    /// <param name="graph">Graph to inspect.</param>
    /// <returns><see langword="true"/> when graph-level node settings are present.</returns>
    public static bool IsNodeGraph(GraphDocument graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        return graph.metadata.ContainsKey(settingsKey);
    }

    /// <summary>Writes graph-level reusable-node metadata without coupling it to an input or output record.</summary>
    /// <param name="graph">Graph whose node identity is being assigned.</param>
    /// <param name="settings">Detached node metadata.</param>
    /// <param name="serialization">Owner converter registry.</param>
    /// <param name="context">Complete owner reference context.</param>
    public static void WriteSettings(GraphDocument graph, ShaderGraphNodeSettings settings,
        SerializationRegistry serialization, SerializationContext context)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(settings);
        graph.SetMetadata(settingsKey, ShaderGraphDocument.Encode(settings, serialization, context));
    }

    /// <summary>Reads required graph-level reusable-node metadata.</summary>
    /// <param name="graph">Graph declaring the reusable node.</param>
    /// <param name="serialization">Owner converter registry.</param>
    /// <param name="context">Complete owner reference context.</param>
    /// <returns>Detached node metadata.</returns>
    public static ShaderGraphNodeSettings ReadSettings(GraphDocument graph, SerializationRegistry serialization,
        SerializationContext context)
    {
        ArgumentNullException.ThrowIfNull(graph);
        if (!graph.metadata.TryGetValue(settingsKey, out GraphSerializedValue? value))
            throw new InvalidOperationException("The Shader graph does not declare reusable-node settings.");
        return ShaderGraphDocument.Decode<ShaderGraphNodeSettings>(value, serialization, context);
    }

    /// <summary>Reads and validates the public node interface declared by a Shader graph.</summary>
    /// <param name="graph">Detached graph asset source.</param>
    /// <param name="serialization">Owner converter registry.</param>
    /// <param name="context">Complete owner reference context.</param>
    /// <returns>The detached graph-node interface.</returns>
    public static ShaderGraphNodeInterface ReadInterface(GraphDocument graph, SerializationRegistry serialization,
        SerializationContext context)
    {
        ArgumentNullException.ThrowIfNull(graph);
        if (ShaderGraphDocument.ReadTarget(graph, serialization, context).Length != 0)
            throw new InvalidOperationException("A graph-authored node cannot select a Shader Target.");
        if (graph.nodes.Any(static node => node.definitionId == ShaderGraphDocument.outputDefinitionId))
            throw new InvalidOperationException("A graph-authored node cannot contain explicit GPU stage outputs.");
        GraphNodeRecord[] inputs = graph.nodes.Where(static node => node.definitionId == inputDefinitionId).ToArray();
        GraphNodeRecord[] outputs = graph.nodes.Where(static node => node.definitionId == outputDefinitionId).ToArray();
        if (inputs.Length > 1) throw new InvalidOperationException("A graph-authored node can contain at most one Function Inputs record.");
        if (outputs.Length > 1) throw new InvalidOperationException("A graph-authored node can contain at most one Function Outputs record.");
        if (inputs.Length == 0 && outputs.Length == 0)
            throw new InvalidOperationException("A graph-authored node requires Function Inputs, Function Outputs, or both.");
        ShaderGraphNodeSettings settings = ReadSettings(graph, serialization, context);
        ShaderGraphNodeInputSettings input = inputs.Length == 0
            ? new()
            : ShaderGraphDocument.Read(inputs[0], ShaderGraphDocument.settingsKey,
                new ShaderGraphNodeInputSettings(), serialization, context);
        ShaderGraphNodeOutputSettings output = outputs.Length == 0
            ? new()
            : ShaderGraphDocument.Read(outputs[0], ShaderGraphDocument.settingsKey,
                new ShaderGraphNodeOutputSettings(), serialization, context);
        var result = new ShaderGraphNodeInterface
        {
            displayName = settings.displayName,
            createPath = settings.createPath,
            createOrder = settings.createOrder,
            kind = settings.kind,
            effect = settings.effect,
            role = settings.role,
            inputs = input.ports,
            outputs = output.ports
        };
        Validate(result, inputs.Length, outputs.Length);
        return result;
    }

    /// <summary>Reads the current interface snapshot stored on a graph-node reference.</summary>
    /// <param name="node">Graph-node reference.</param>
    /// <param name="serialization">Owner converter registry.</param>
    /// <param name="context">Complete owner reference context.</param>
    /// <returns>The detached stored interface.</returns>
    public static ShaderGraphNodeInterface ReadCallInterface(GraphNodeRecord node, SerializationRegistry serialization,
        SerializationContext context)
    {
        ShaderGraphNodeInterface result = ShaderGraphDocument.Read(
            node, interfaceKey, new ShaderGraphNodeInterface(), serialization, context);
        Validate(result, result.inputs.Length == 0 ? 0 : 1, result.outputs.Length == 0 ? 0 : 1);
        return result;
    }

    /// <summary>Expands every inline graph-node reference and refreshes domain-output interfaces.</summary>
    /// <param name="graph">Authored parent graph.</param>
    /// <param name="resolve">Resolves a referenced Shader graph by persistent identity and diagnostic path.</param>
    /// <param name="serialization">Owner converter registry.</param>
    /// <param name="context">Complete owner reference context.</param>
    /// <returns>A detached graph containing no inline graph-node references.</returns>
    public static GraphDocument Expand(GraphDocument graph, Func<Guid, string, GraphDocument> resolve,
        SerializationRegistry serialization, SerializationContext context)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(resolve);
        GraphDocument expanded = graph.Clone();
        Expand(expanded, resolve, serialization, context, []);
        return expanded;
    }

    private static void Expand(GraphDocument graph, Func<Guid, string, GraphDocument> resolve,
        SerializationRegistry serialization, SerializationContext context, HashSet<Guid> stack)
    {
        var retainedDomainOutputs = new HashSet<GraphNodeId>();
        while (graph.nodes.FirstOrDefault(node => node.definitionId == callDefinitionId
                   && !retainedDomainOutputs.Contains(node.id)) is { } call)
        {
            Guid sourceId = ShaderGraphDocument.Read(call, "sourceId", Guid.Empty, serialization, context);
            string sourcePath = ShaderGraphDocument.Read(call, "sourcePath", "", serialization, context);
            if (sourceId == Guid.Empty) throw new InvalidOperationException("A graph-node reference has no Shader asset identity.");
            if (!stack.Add(sourceId)) throw new InvalidOperationException($"Graph-node references contain a cycle at '{sourcePath}'.");
            GraphDocument child = resolve(sourceId, sourcePath).Clone();
            ShaderGraphNodeInterface nodeInterface = ReadInterface(child, serialization, context);
            if (nodeInterface.kind == ShaderGraphNodeKind.DomainOutput)
            {
                call.SetValue(interfaceKey, ShaderGraphDocument.Encode(nodeInterface, serialization, context));
                retainedDomainOutputs.Add(call.id);
                stack.Remove(sourceId);
                continue;
            }
            Expand(child, resolve, serialization, context, stack);
            stack.Remove(sourceId);
            Inline(graph, call, child, nodeInterface, serialization, context);
        }
    }

    private static void Inline(GraphDocument parent, GraphNodeRecord call, GraphDocument child,
        ShaderGraphNodeInterface nodeInterface, SerializationRegistry serialization, SerializationContext context)
    {
        GraphNodeRecord? inputNode = child.nodes.SingleOrDefault(static node => node.definitionId == inputDefinitionId);
        GraphNodeRecord? outputNode = child.nodes.SingleOrDefault(static node => node.definitionId == outputDefinitionId);
        string stage = ShaderGraphDocument.Read(call, ShaderGraphDocument.stageKey, "", serialization, context);
        MergeDefinition(parent, child, stage, serialization, context);
        var map = new Dictionary<GraphNodeId, GraphNodeId>();
        foreach (GraphNodeRecord node in child.nodes.Where(node => node.id != inputNode?.id && node.id != outputNode?.id))
        {
            var mapped = new GraphNodeId(call.id.value + "/" + node.id.value);
            if (parent.FindNode(mapped) is not null) throw new InvalidOperationException($"Inlining '{nodeInterface.displayName}' produced duplicate node identity '{mapped.value}'.");
            var copy = new GraphNodeRecord(mapped, node.definitionId) { position = new(call.position.x + node.position.x, call.position.y + node.position.y) };
            foreach ((string key, GraphSerializedValue value) in node.values) copy.SetValue(key, value);
            if (stage.Length != 0) copy.SetValue(ShaderGraphDocument.stageKey, ShaderGraphDocument.Encode(stage, serialization, context));
            parent.AddNode(copy);
            map.Add(node.id, mapped);
        }

        var incoming = parent.edges.Where(edge => edge.input.nodeId == call.id)
            .GroupBy(static edge => edge.input.portId.value, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.ToArray(), StringComparer.Ordinal);
        var outgoing = parent.edges.Where(edge => edge.output.nodeId == call.id)
            .GroupBy(static edge => edge.output.portId.value, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.ToArray(), StringComparer.Ordinal);
        HashSet<string> inputIds = nodeInterface.inputs.Select(static port => port.id).ToHashSet(StringComparer.Ordinal);
        HashSet<string> outputIds = nodeInterface.outputs.Select(static port => port.id).ToHashSet(StringComparer.Ordinal);
        string? missingInput = incoming.Keys.FirstOrDefault(id => !inputIds.Contains(id));
        if (missingInput is not null)
            throw new InvalidOperationException(
                $"Graph-node call '{nodeInterface.displayName}' retains unavailable input port '{missingInput}'.");
        string? missingOutput = outgoing.Keys.FirstOrDefault(id => !outputIds.Contains(id));
        if (missingOutput is not null)
            throw new InvalidOperationException(
                $"Graph-node call '{nodeInterface.displayName}' retains unavailable output port '{missingOutput}'.");
        foreach (GraphEdgeRecord[] values in incoming.Values)
            if (values.Length != 1) throw new InvalidOperationException("A graph-node input has multiple incoming connections.");

        var resolvedInputs = new Dictionary<string, GraphEndpoint>(StringComparer.Ordinal);
        foreach (ShaderGraphNodePortDefinition port in nodeInterface.inputs)
        {
            if (incoming.TryGetValue(port.id, out GraphEdgeRecord[]? edges))
                resolvedInputs.Add(port.id, edges[0].output);
            else if (!port.required)
                resolvedInputs.Add(port.id, AddOptionalZero(parent, call, port, stage, serialization, context));
            else
                throw new InvalidOperationException(
                    $"Required graph-node input '{port.id}' is not connected on '{nodeInterface.displayName}'.");
        }

        var resolvedOutputs = new Dictionary<string, GraphEndpoint>(StringComparer.Ordinal);
        foreach (GraphEdgeRecord edge in child.edges)
        {
            bool fromInput = inputNode is not null && edge.output.nodeId == inputNode.id;
            bool toOutput = outputNode is not null && edge.input.nodeId == outputNode.id;
            GraphEndpoint mappedSource;
            if (fromInput)
            {
                if (!resolvedInputs.TryGetValue(edge.output.portId.value, out GraphEndpoint source)) continue;
                mappedSource = source;
            }
            else
            {
                if (!map.TryGetValue(edge.output.nodeId, out GraphNodeId sourceNode)) continue;
                mappedSource = new(sourceNode, edge.output.portId);
            }
            if (toOutput)
            {
                if (!resolvedOutputs.TryAdd(edge.input.portId.value, mappedSource))
                    throw new InvalidOperationException($"Graph-node output '{edge.input.portId.value}' is connected more than once.");
            }
            else if (map.TryGetValue(edge.input.nodeId, out GraphNodeId targetNode))
                parent.AddEdge(new(new(call.id.value + "/" + edge.id.value), mappedSource, new(targetNode, edge.input.portId)));
        }
        foreach (ShaderGraphNodePortDefinition port in nodeInterface.outputs)
        {
            if (!resolvedOutputs.TryGetValue(port.id, out GraphEndpoint source))
                throw new InvalidOperationException($"Graph-node output '{port.id}' has no value inside '{nodeInterface.displayName}'.");
            if (!outgoing.TryGetValue(port.id, out GraphEdgeRecord[]? edges)) continue;
            foreach (GraphEdgeRecord edge in edges)
                parent.AddEdge(new(new(call.id.value + "/return/" + edge.id.value), source, edge.input));
        }
        parent.RemoveNode(call.id);
    }

    private static void MergeDefinition(GraphDocument parent, GraphDocument child, string stageId,
        SerializationRegistry serialization, SerializationContext context)
    {
        ShaderDefinition parentDefinition = ShaderGraphDocument.ReadDefinition(parent, serialization, context);
        ShaderDefinition childDefinition = ShaderGraphDocument.ReadDefinition(child, serialization, context);
        ShaderStage callerStage = ShaderStage.None;
        if (stageId.Length != 0 && parent.FindNode(new(stageId)) is { definitionId: ShaderGraphDocument.outputDefinitionId } stageNode)
            callerStage = ShaderGraphDocument.Read(stageNode, ShaderGraphDocument.settingsKey,
                new ShaderGraphStageSettings(), serialization, context).stage;
        var properties = parentDefinition.properties.ToList();
        foreach (ShaderPropertyDefinition childPropertyValue in childDefinition.properties)
        {
            ShaderPropertyDefinition childProperty = childPropertyValue;
            if (callerStage != ShaderStage.None) childProperty.stages |= callerStage;
            int index = properties.FindIndex(value => value.id.value == childProperty.id.value);
            if (index < 0)
            {
                properties.Add(childProperty);
                continue;
            }
            ShaderPropertyDefinition existing = properties[index];
            if (existing.type != childProperty.type || existing.bindingKind != childProperty.bindingKind
                || existing.storageAccess != childProperty.storageAccess || existing.bindingOwner != childProperty.bindingOwner)
                throw new InvalidOperationException($"Graph node property '{childProperty.id.value}' conflicts with its caller.");
            existing.stages |= childProperty.stages;
            properties[index] = existing;
        }
        parentDefinition.properties = properties.ToArray();
        parent.SetMetadata(ShaderGraphDocument.definitionKey,
            ShaderGraphDocument.Encode(serialization.Serialize(parentDefinition, context), serialization, context));
    }

    private static GraphEndpoint AddOptionalZero(GraphDocument graph, GraphNodeRecord call,
        ShaderGraphNodePortDefinition port, string stage,
        SerializationRegistry serialization, SerializationContext context)
    {
        _ = ShaderGraphLiteral.Zero(port.type.CreateType());
        var id = new GraphNodeId(call.id.value + "/default/" + port.id);
        var node = new GraphNodeRecord(id, "inno.shader.reroute") { position = call.position };
        node.SetValue("valueType", ShaderGraphDocument.Encode(port.type, serialization, context));
        if (stage.Length != 0) node.SetValue(ShaderGraphDocument.stageKey, ShaderGraphDocument.Encode(stage, serialization, context));
        graph.AddNode(node);
        return new(id, new("value"));
    }

    private static void Validate(ShaderGraphNodeInterface value, int inputRecords, int outputRecords)
    {
        if (string.IsNullOrWhiteSpace(value.displayName)) throw new InvalidOperationException("A graph-authored node requires a display name.");
        if (!Enum.IsDefined(value.kind)) throw new InvalidOperationException("The graph-authored node kind is invalid.");
        if (!Enum.IsDefined(value.effect)) throw new InvalidOperationException("The graph-authored node effect is invalid.");
        if (inputRecords is < 0 or > 1 || outputRecords is < 0 or > 1 || inputRecords + outputRecords == 0)
            throw new InvalidOperationException("A graph-authored node requires at most one interface record per direction and at least one direction.");
        if (value.inputs.Length + value.outputs.Length == 0)
            throw new InvalidOperationException("A graph-authored node requires at least one public input or output port.");
        if (value.kind == ShaderGraphNodeKind.Function && value.effect == ShaderGraphNodeEffect.Pure && outputRecords == 0)
            throw new InvalidOperationException("A pure graph-authored function requires at least one Function Output.");
        if (value.kind == ShaderGraphNodeKind.DomainOutput && outputRecords != 0)
            throw new InvalidOperationException("A domain output graph node cannot declare returned values.");
        if (value.kind == ShaderGraphNodeKind.DomainOutput && string.IsNullOrWhiteSpace(value.role))
            throw new InvalidOperationException("A domain output graph node requires a stable Target role.");
        ValidatePorts(value.inputs, "input");
        ValidatePorts(value.outputs, "output");
    }

    private static void ValidatePorts(IEnumerable<ShaderGraphNodePortDefinition> ports, string direction)
    {
        if (ports is null) throw new InvalidOperationException($"The graph-node {direction} port list is missing.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (ShaderGraphNodePortDefinition port in ports)
        {
            if (port is null || string.IsNullOrWhiteSpace(port.id) || port.type is null || port.type.CreateType().id == "void")
                throw new InvalidOperationException($"A graph-node {direction} port is invalid.");
            if (!ids.Add(port.id)) throw new InvalidOperationException($"A graph-node {direction} port repeats '{port.id}'.");
        }
    }
}

/// <summary>Describes graph-authored node references before they are expanded or consumed by a Target.</summary>
public sealed class ShaderGraphCallNodeCompiler : IShaderNodeCompiler
{
    /// <inheritdoc />
    public string definitionId => ShaderGraphNodes.callDefinitionId;
    /// <inheritdoc />
    public IReadOnlyList<ShaderNodePort> GetPorts(ShaderNodeDescriptionContext context)
        => context.Read(ShaderGraphNodes.interfaceKey, new ShaderGraphNodeInterface()).GetPorts();
    /// <inheritdoc />
    public IReadOnlyDictionary<string, ShaderIrValue> Lower(ShaderNodeLoweringContext context)
        => throw new InvalidOperationException("A graph-authored node must be expanded or consumed by its Shader Target before lowering.");
}

/// <summary>Describes the multi-port external inputs while editing a graph-authored node asset.</summary>
public sealed class ShaderGraphNodeInputsCompiler : IShaderNodeCompiler
{
    /// <inheritdoc />
    public string definitionId => ShaderGraphNodes.inputDefinitionId;
    /// <inheritdoc />
    public IReadOnlyList<ShaderNodePort> GetPorts(ShaderNodeDescriptionContext context)
        => context.Read(ShaderGraphDocument.settingsKey, new ShaderGraphNodeInputSettings()).ports
            .Select(static value => value.ToPort(GraphPortDirection.Output)).ToArray();
    /// <inheritdoc />
    public IReadOnlyDictionary<string, ShaderIrValue> Lower(ShaderNodeLoweringContext context)
        => throw new InvalidOperationException("Node Inputs exist only inside a graph-authored node definition.");
}

/// <summary>Describes the multi-port returned values while editing a graph-authored function node asset.</summary>
public sealed class ShaderGraphNodeOutputsCompiler : IShaderNodeCompiler
{
    /// <inheritdoc />
    public string definitionId => ShaderGraphNodes.outputDefinitionId;
    /// <inheritdoc />
    public IReadOnlyList<ShaderNodePort> GetPorts(ShaderNodeDescriptionContext context)
        => context.Read(ShaderGraphDocument.settingsKey, new ShaderGraphNodeOutputSettings()).ports
            .Select(static value => value.ToPort(GraphPortDirection.Input)).ToArray();
    /// <inheritdoc />
    public IReadOnlyDictionary<string, ShaderIrValue> Lower(ShaderNodeLoweringContext context)
        => throw new InvalidOperationException("Node Outputs exist only inside a graph-authored node definition.");
}
