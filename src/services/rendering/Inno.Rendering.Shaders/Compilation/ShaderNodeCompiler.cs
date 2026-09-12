using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Inno.Core.Graphs;
using Inno.Core.Serialization;

namespace Inno.Rendering.Shaders;

/// <summary>Declares a typed shader port independently of editor presentation and backend source syntax.</summary>
/// <param name="id">Stable node-local port identity.</param>
/// <param name="type">Complete value type, including all aggregate fields.</param>
/// <param name="direction">Input or output value flow.</param>
/// <param name="required">Whether an input must be connected; alternative aggregate/member inputs validate in their compiler.</param>
public sealed record ShaderNodePort(string id, ShaderSourceType type, GraphPortDirection direction, bool required = true);

/// <summary>Marks a node compiler for discovery by the shared type-generation registry.</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class ShaderNodeCompilerExtensionAttribute : Attribute;

/// <summary>Defines one shader node's typed ports and lowering; drawing belongs to a separate editor extension.</summary>
public interface IShaderNodeCompiler
{
    /// <summary>Gets the exact graph node definition identity implemented by this compiler.</summary>
    string definitionId { get; }
    /// <summary>Describes ports for the current neutral properties and resolved source/target inputs.</summary>
    /// <param name="context">Invocation-scoped node properties and frozen source/target data.</param>
    /// <returns>Complete typed ports; duplicate identities are invalid.</returns>
    IReadOnlyList<ShaderNodePort> GetPorts(ShaderNodeDescriptionContext context);
    /// <summary>Lowers the node into the supplied typed builder without generating source strings.</summary>
    /// <param name="context">Connected typed inputs and the shared region builder.</param>
    /// <returns>Exactly the declared output ports by stable identity.</returns>
    IReadOnlyDictionary<string, ShaderIrValue> Lower(ShaderNodeLoweringContext context);
}

/// <summary>Provides invocation-scoped property decoding through the complete owner serialization context.</summary>
public sealed class ShaderNodeDescriptionContext
{
    private readonly GraphNodeRecord m_node;
    private readonly SerializationRegistry m_serialization;
    private readonly SerializationContext m_context;

    internal ShaderNodeDescriptionContext(GraphNodeRecord node, SerializationRegistry serialization, SerializationContext context,
        ShaderSourceModuleAnalysis? sourceModule, string implementationId, ShaderIrStageInput? stageInput)
    {
        m_node = node;
        m_serialization = serialization;
        m_context = context;
        this.sourceModule = sourceModule;
        this.implementationId = implementationId;
        this.stageInput = stageInput;
    }

    /// <summary>Gets the node's stable identity, never its editor runtime handle.</summary>
    public GraphNodeId nodeId => m_node.id;
    /// <summary>Gets the node definition's stable identity.</summary>
    public string definitionId => m_node.definitionId;
    /// <summary>Gets a frozen module resolved by the asset owner; null means unassigned or unavailable.</summary>
    public ShaderSourceModuleAnalysis? sourceModule { get; }
    /// <summary>Gets the exact implementation key chosen by the target.</summary>
    public string implementationId { get; }
    /// <summary>Gets the target-assigned stage input for this node, when applicable.</summary>
    public ShaderIrStageInput? stageInput { get; }

    /// <summary>Reads a property using the owner's converter generation and complete reference resolver context.</summary>
    /// <typeparam name="T">Declared property type supported by the native serializer.</typeparam>
    /// <param name="id">Stable property identity.</param>
    /// <param name="defaultValue">Value used only when the property is absent, never after corrupt-data failure.</param>
    /// <returns>The deserialized value or the supplied absent-property default.</returns>
    public T Read<T>(string id, T defaultValue)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return m_node.TryGetValue(id, out GraphSerializedValue? value)
            ? m_serialization.Decode(value!.data.Span, reader => reader.Read<T>("value"), m_context)
            : defaultValue;
    }
}

/// <summary>Supplies one node's connected values and the region builder during lowering only.</summary>
public sealed class ShaderNodeLoweringContext
{
    internal ShaderNodeLoweringContext(ShaderNodeDescriptionContext description, ShaderIrBuilder builder,
        IReadOnlyDictionary<string, ShaderIrValue> inputs)
    {
        this.description = description;
        this.builder = builder;
        this.inputs = new ReadOnlyDictionary<string, ShaderIrValue>(new Dictionary<string, ShaderIrValue>(inputs, StringComparer.Ordinal));
    }

    /// <summary>Gets the invocation-scoped node description.</summary>
    public ShaderNodeDescriptionContext description { get; }
    /// <summary>Gets the builder shared by nodes in this typed region.</summary>
    public ShaderIrBuilder builder { get; }
    /// <summary>Gets connected values keyed by semantic input port identity, never connection order.</summary>
    public IReadOnlyDictionary<string, ShaderIrValue> inputs { get; }
    /// <summary>Requires one connected input without implicit defaults or positional rebinding.</summary>
    /// <param name="id">Exact semantic input port identity.</param>
    /// <returns>The connected typed value.</returns>
    public ShaderIrValue Input(string id)
        => inputs.TryGetValue(id, out ShaderIrValue? value) ? value : throw new InvalidOperationException($"Input '{id}' is not connected.");
}
