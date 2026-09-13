using System;
using System.Collections.Generic;
using Inno.Core.Serialization;

namespace Inno.Rendering.Shaders;

/// <summary>Stores an exact typed value for an unconnected node input, independently of source-language syntax.</summary>
public sealed class ShaderGraphLiteral : ISerializable
{
    /// <summary>Gets or sets the complete value type, including named aggregates and fixed arrays.</summary>
    [SerializableProperty] public ShaderGraphType type { get; set; } = new() { id = "float" };
    /// <summary>Gets or sets scalar bit patterns in declaration order; matrices use column-major order.</summary>
    [SerializableProperty] public uint[] scalarBits { get; set; } = [0];

    /// <summary>Creates an explicit zero value for a supported scalar, vector, matrix, structure or array.</summary>
    /// <param name="type">Complete numeric or boolean value type; resources and effect tokens are not values.</param>
    /// <returns>A detached zero literal with an exact type and component count.</returns>
    /// <exception cref="NotSupportedException">The type is opaque or exceeds the bounded literal size.</exception>
    public static ShaderGraphLiteral Zero(ShaderSourceType type)
    {
        ArgumentNullException.ThrowIfNull(type);
        var scalars = new List<string>();
        Flatten(type, scalars, 0);
        return new() { type = ShaderGraphType.Capture(type), scalarBits = new uint[scalars.Count] };
    }

    /// <summary>Gets the exact scalar type of each stored component for shared Inspector value controls.</summary>
    /// <returns>Detached scalar identities in the same order as the bit patterns.</returns>
    public IReadOnlyList<string> GetScalarTypes()
    {
        if (type is null || scalarBits is null) throw new InvalidOperationException("The input default must retain a type and scalar bits.");
        var scalars = new List<string>();
        Flatten(type.CreateType(), scalars, 0);
        if (scalarBits.Length != scalars.Count) throw new InvalidOperationException("The input default's component count does not match its type.");
        for (int i = 0; i < scalars.Count; i++)
            if (scalars[i] == "bool" && scalarBits[i] > 1) throw new InvalidOperationException("A boolean input default must contain zero or one.");
        return scalars;
    }

    /// <summary>Emits ordinary typed constants and aggregate construction into the common IR.</summary>
    /// <param name="builder">Current region's instruction owner.</param>
    /// <param name="expectedType">Current port type; stale defaults never undergo implicit conversion.</param>
    /// <returns>A value owned by the supplied builder.</returns>
    /// <exception cref="InvalidOperationException">The stored type, component count or boolean bits are invalid.</exception>
    public ShaderIrValue Emit(ShaderIrBuilder builder, ShaderSourceType expectedType)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(expectedType);
        _ = GetScalarTypes();
        ShaderSourceType declared = type.CreateType();
        if (!declared.IsEquivalentTo(expectedType)) throw new InvalidOperationException("The input default type changed; reset or repair it explicitly.");
        int offset = 0;
        return Build(declared);

        ShaderIrValue Build(ShaderSourceType current)
        {
            if (IsScalar(current.id) && current.fields.Count == 0 && current.elementType is null && current.storage is null)
            {
                uint bits = scalarBits[offset++];
                return current.id switch
                {
                    "float" => builder.Constant(BitConverter.UInt32BitsToSingle(bits)),
                    "int" => builder.Constant(unchecked((int)bits)),
                    "uint" => builder.Constant(bits),
                    "bool" when bits <= 1 => builder.Constant(bits != 0),
                    _ => throw new InvalidOperationException("A boolean input default must contain zero or one.")
                };
            }
            ShaderSourceType[] members = Members(current);
            var values = new ShaderIrValue[members.Length];
            for (int i = 0; i < values.Length; i++) values[i] = Build(members[i]);
            return builder.Construct(current, values);
        }
    }

    private static bool IsScalar(string id) => id is "float" or "int" or "uint" or "bool";

    private static void Flatten(ShaderSourceType type, List<string> scalars, int depth)
    {
        if (depth > 32 || scalars.Count >= 4096) throw new NotSupportedException("An input literal exceeds the bounded aggregate size.");
        if (IsScalar(type.id) && type.fields.Count == 0 && type.elementType is null && type.storage is null) scalars.Add(type.id);
        else foreach (ShaderSourceType member in Members(type)) Flatten(member, scalars, depth + 1);
    }

    private static ShaderSourceType[] Members(ShaderSourceType type)
    {
        if (type.storage is not null) throw new NotSupportedException("Resource inputs require explicit bindings, not literal defaults.");
        if (type.elementType is not null)
        {
            if (type.elementCount > 4096) throw new NotSupportedException("An input literal array is too large.");
            var elements = new ShaderSourceType[type.elementCount];
            Array.Fill(elements, type.elementType); return elements;
        }
        if (type.fields.Count != 0)
        {
            var fields = new ShaderSourceType[type.fields.Count];
            for (int i = 0; i < fields.Length; i++) fields[i] = type.fields[i].type;
            return fields;
        }
        foreach (string scalar in new[] { "float", "int", "uint", "bool" })
        {
            string shape = type.id.StartsWith(scalar, StringComparison.Ordinal) ? type.id[scalar.Length..] : "";
            int count = shape.Length == 1 && shape[0] is >= '2' and <= '4' ? shape[0] - '0'
                : scalar == "float" && shape.Length == 3 && shape[0] is >= '2' and <= '4' && shape[1] == 'x' && shape[2] is >= '2' and <= '4'
                    ? (shape[0] - '0') * (shape[2] - '0') : 0;
            if (count == 0) continue;
            var result = new ShaderSourceType[count]; Array.Fill(result, ShaderSourceType.Atomic(scalar)); return result;
        }
        throw new NotSupportedException($"Input type '{type.id}' has no literal representation.");
    }
}
