using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using Inno.Core.Graphs;

namespace Inno.Rendering.Shaders;

/// <summary>Projects one resolved source interface into immutable graph ports without duplicating authored port declarations.</summary>
public sealed class ShaderSourceNodeDefinition : GraphNodeDefinition
{
    private readonly IReadOnlyList<GraphPortDefinition> m_ports;

    /// <summary>Creates a source node definition for the currently resolved source generation.</summary>
    /// <param name="function">The single function derived by the source frontend.</param>
    public ShaderSourceNodeDefinition(ShaderSourceFunction function)
        : base("inno.shader.source", function?.name ?? throw new ArgumentNullException(nameof(function)), "Code")
    {
        this.function = function;
        var ports = new List<GraphPortDefinition>();
        foreach (ShaderSourceParameter parameter in function.parameters)
        {
            if (parameter.direction != ShaderSourceParameterDirection.Output)
                Add("input." + parameter.name, parameter.name, parameter.type, GraphPortDirection.Input);
            if (parameter.direction != ShaderSourceParameterDirection.Input)
                Add("output." + parameter.name, parameter.name, parameter.type, GraphPortDirection.Output);
        }
        if (function.returnType.id != "void")
            Add("return", "Result", function.returnType, GraphPortDirection.Output);
        m_ports = ports.AsReadOnly();

        void Add(string id, string label, ShaderSourceType type, GraphPortDirection direction)
        {
            ports.Add(new(new GraphPortId(id), label, TypeIdentity(type), direction,
                direction == GraphPortDirection.Input ? GraphPortCapacity.Single : GraphPortCapacity.Multiple));
            // Aggregate and member ports coexist so collapsing presentation never changes edge identity.
            foreach (ShaderSourceField field in type.fields)
                Add(id + "." + field.name, label + "." + field.name, field.type, direction);
        }
    }

    /// <summary>Gets the immutable parsed source interface belonging to this definition.</summary>
    public ShaderSourceFunction function { get; }

    /// <inheritdoc />
    public override IReadOnlyList<GraphPortDefinition> GetPorts(GraphNodeRecord node)
    {
        ArgumentNullException.ThrowIfNull(node);
        return m_ports;
    }

    private static string TypeIdentity(ShaderSourceType type)
    {
        if (type.elementType is null && type.fields.Count == 0 && type.storage is null) return type.id;
        var canonical = new StringBuilder();
        Write(type);
        return "inno.shader.aggregate." + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())));

        void Write(ShaderSourceType value)
        {
            canonical.Append(value.id.Length).Append(':').Append(value.id).Append('[').Append(value.elementCount).Append(']');
            if (value.elementType is not null) Write(value.elementType);
            if (value.storage is ShaderStorageType storage)
            {
                canonical.Append('(').Append((int)storage.access).Append(',').Append(storage.format.HasValue ? (int)storage.format.Value : -1)
                    .Append(',').Append((int)storage.dimension).Append(',').Append(storage.array ? 1 : 0).Append(')');
                Write(storage.valueType);
            }
            canonical.Append('{');
            foreach (ShaderSourceField field in value.fields)
            {
                canonical.Append(field.name.Length).Append(':').Append(field.name);
                Write(field.type);
            }
            canonical.Append('}');
        }
    }
}
