using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Inno.Rendering.Shaders;

/// <summary>
/// Builds typed instructions and structured regions. No API accepts an arbitrary backend expression or complete stage source.
/// This builder is transient and belongs to one lowering operation, never to a persisted document or runtime frame.
/// </summary>
public sealed partial class ShaderIrBuilder
{
    private readonly object m_owner = new();
    private readonly List<ShaderIrInstruction> m_instructions = [];
    private readonly Dictionary<string, ShaderIrValue> m_inputs = new(StringComparer.Ordinal);
    private readonly ShaderIrBuilder? m_parent;
    private readonly ShaderIrBuilder m_root;
    private bool m_buildingChild;
    private bool m_closed;
    private int m_nextValue;

    /// <summary>Creates an independent typed region builder and value identity scope.</summary>
    public ShaderIrBuilder() => m_root = this;

    private ShaderIrBuilder(ShaderIrBuilder parent)
    {
        m_parent = parent;
        m_root = parent.m_root;
    }

    /// <summary>Reads a named input from the enclosing typed function or stage interface.</summary>
    /// <param name="name">Stable interface identity; it is not emitted as native source verbatim.</param>
    /// <param name="type">Complete non-void input type.</param>
    /// <returns>The shared input value for this name and type.</returns>
    /// <exception cref="ArgumentException">The name is empty, type is void, or this input was already declared with another type.</exception>
    public ShaderIrValue Input(string name, ShaderSourceType type)
    {
        EnsureWritable();
        if (m_parent is not null) throw new InvalidOperationException("Stage inputs must be declared in the root region and captured by nested regions.");
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        RequireValueType(type);
        if (m_inputs.TryGetValue(name, out ShaderIrValue? previous))
        {
            if (!type.IsEquivalentTo(previous.type)) throw new ArgumentException($"Input '{name}' changed type.", nameof(type));
            return previous;
        }
        ShaderIrValue output = NewValue(type);
        m_inputs.Add(name, output);
        m_instructions.Add(new(ShaderIrOperation.Input, [], [output], inputName: name));
        return output;
    }

    /// <summary>Produces a finite, exact 32-bit floating-point constant.</summary>
    /// <param name="value">Finite scalar value, including signed zero.</param>
    /// <returns>A float-typed intermediate value.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The value is NaN or infinite.</exception>
    public ShaderIrValue Constant(float value)
    {
        if (!float.IsFinite(value)) throw new ArgumentOutOfRangeException(nameof(value), "Shader constants must be finite.");
        return ConstantBits("float", BitConverter.SingleToUInt32Bits(value));
    }

    /// <summary>Produces an exact signed 32-bit integer constant.</summary>
    /// <param name="value">Scalar integer value.</param>
    /// <returns>An int-typed intermediate value.</returns>
    public ShaderIrValue Constant(int value) => ConstantBits("int", unchecked((uint)value));

    /// <summary>Produces an exact unsigned 32-bit integer constant.</summary>
    /// <param name="value">Scalar unsigned integer value.</param>
    /// <returns>A uint-typed intermediate value.</returns>
    public ShaderIrValue Constant(uint value) => ConstantBits("uint", value);

    /// <summary>Produces a scalar Boolean constant.</summary>
    /// <param name="value">Scalar Boolean value.</param>
    /// <returns>A bool-typed intermediate value.</returns>
    public ShaderIrValue Constant(bool value) => ConstantBits("bool", value ? 1UL : 0UL);

    /// <summary>Applies an explicitly typed binary arithmetic or scalar comparison operation.</summary>
    /// <param name="operation">Add, Subtract, Multiply, Divide, Minimum, Maximum, Equal or LessThan.</param>
    /// <param name="left">Left operand from this builder.</param>
    /// <param name="right">Right operand of the same complete type.</param>
    /// <returns>The result; comparisons produce a scalar Boolean.</returns>
    /// <exception cref="ArgumentException">Operands belong to another builder, differ in type, or the operation is invalid for them.</exception>
    public ShaderIrValue Binary(ShaderIrOperation operation, ShaderIrValue left, ShaderIrValue right)
    {
        RequireOwned(left);
        RequireOwned(right);
        if (!left.type.IsEquivalentTo(right.type)) throw new ArgumentException("Binary operands require identical types; conversions must be explicit.", nameof(right));
        bool numeric = TryNumeric(left.type, out string scalar, out int columns, out int rows);
        bool scalarValue = columns == 1 && rows == 1;
        bool comparison = operation is ShaderIrOperation.Equal or ShaderIrOperation.LessThan;
        bool permitted = operation switch
        {
            ShaderIrOperation.Add or ShaderIrOperation.Subtract or ShaderIrOperation.Multiply or ShaderIrOperation.Divide
                or ShaderIrOperation.Minimum or ShaderIrOperation.Maximum => numeric && scalar != "bool",
            ShaderIrOperation.Equal => numeric && scalarValue,
            ShaderIrOperation.LessThan => numeric && scalarValue && scalar != "bool",
            _ => false
        };
        if (!permitted) throw new ArgumentException($"Operation '{operation}' is not defined for '{left.type.id}'.", nameof(operation));
        ShaderIrValue output = NewValue(comparison ? ShaderSourceType.Atomic("bool") : left.type);
        m_instructions.Add(new(operation, [left, right], [output]));
        return output;
    }

    /// <summary>Selects between equal typed values without conditionally executing their producers.</summary>
    /// <param name="condition">Scalar Boolean condition from this builder.</param>
    /// <param name="whenTrue">Value selected when true.</param>
    /// <param name="whenFalse">Value selected when false.</param>
    /// <returns>A value with the selected operands' complete type.</returns>
    /// <exception cref="ArgumentException">The condition is not bool or selected value types differ.</exception>
    public ShaderIrValue Select(ShaderIrValue condition, ShaderIrValue whenTrue, ShaderIrValue whenFalse)
    {
        RequireOwned(condition);
        RequireOwned(whenTrue);
        RequireOwned(whenFalse);
        if (whenTrue.type.storage is not null || whenTrue.type.id.StartsWith("sampled-texture", StringComparison.Ordinal))
            throw new ArgumentException("Resource bindings cannot be selected as ordinary values.", nameof(whenTrue));
        if (!condition.type.IsEquivalentTo(ShaderSourceType.Atomic("bool")) || !whenTrue.type.IsEquivalentTo(whenFalse.type))
            throw new ArgumentException("Selection requires a scalar bool and two values of identical type.");
        ShaderIrValue output = NewValue(whenTrue.type);
        m_instructions.Add(new(ShaderIrOperation.Select, [condition, whenTrue, whenFalse], [output]));
        return output;
    }

    /// <summary>Constructs a complete vector, column-major matrix, structure or fixed array from typed members.</summary>
    /// <param name="type">The complete aggregate type.</param>
    /// <param name="members">Scalar vector/matrix components, declaration-ordered structure fields, or array elements.</param>
    /// <returns>A value of the requested type.</returns>
    /// <exception cref="ArgumentException">The type is not an aggregate or members have incorrect count or types.</exception>
    public ShaderIrValue Construct(ShaderSourceType type, params ShaderIrValue[] members)
    {
        RequireValueType(type);
        ArgumentNullException.ThrowIfNull(members);
        foreach (ShaderIrValue member in members) RequireOwned(member);
        ShaderSourceType[] expected;
        if (type.elementType is not null) expected = Enumerable.Repeat(type.elementType, type.elementCount).ToArray();
        else if (type.fields.Count != 0) expected = type.fields.Select(static field => field.type).ToArray();
        else if (TryNumeric(type, out string scalar, out int columns, out int rows) && columns * rows > 1)
            expected = Enumerable.Repeat(ShaderSourceType.Atomic(scalar), columns * rows).ToArray();
        else throw new ArgumentException("Construction requires a vector, matrix, structure or array type.", nameof(type));
        if (expected.Length != members.Length || expected.Where((value, index) => !value.IsEquivalentTo(members[index].type)).Any())
            throw new ArgumentException("Aggregate members do not match the complete type layout.", nameof(members));
        ShaderIrValue output = NewValue(type);
        m_instructions.Add(new(ShaderIrOperation.Construct, members, [output]));
        return output;
    }

    /// <summary>Extracts one static component, structure member or array element without source-language member syntax.</summary>
    /// <param name="value">Aggregate value from this builder.</param>
    /// <param name="memberIndex">Zero-based member index; a matrix index selects a column vector.</param>
    /// <returns>The precisely typed selected member.</returns>
    /// <exception cref="ArgumentException">The input is not an aggregate.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The member index is outside the aggregate layout.</exception>
    public ShaderIrValue Extract(ShaderIrValue value, int memberIndex)
    {
        RequireOwned(value);
        ArgumentOutOfRangeException.ThrowIfNegative(memberIndex);
        ShaderSourceType type = value.type;
        int count;
        ShaderSourceType memberType;
        if (type.elementType is not null) { count = type.elementCount; memberType = type.elementType; }
        else if (type.fields.Count != 0)
        {
            count = type.fields.Count;
            if (memberIndex >= count) throw new ArgumentOutOfRangeException(nameof(memberIndex));
            memberType = type.fields[memberIndex].type;
        }
        else if (TryNumeric(type, out string scalar, out int columns, out int rows) && columns * rows > 1)
        { count = columns; memberType = ShaderSourceType.Atomic(rows == 1 ? scalar : scalar + rows); }
        else throw new ArgumentException("Extraction requires a vector, matrix, structure or array.", nameof(value));
        if (memberIndex >= count) throw new ArgumentOutOfRangeException(nameof(memberIndex));
        ShaderIrValue output = NewValue(memberType);
        m_instructions.Add(new(ShaderIrOperation.Extract, [value], [output], memberIndex: memberIndex));
        return output;
    }

    /// <summary>Calls a validated source implementation using semantic parameter names, preserving all call side effects.</summary>
    /// <param name="module">A module whose selected implementations and variants share the same interface.</param>
    /// <param name="implementationId">The exact implementation/configuration selected by the target.</param>
    /// <param name="inputs">Exactly the input/inout parameters by public name; output-only parameters are not supplied.</param>
    /// <returns>Named results: return, followed by output.&lt;name&gt; for out/inout parameters.</returns>
    /// <exception cref="ArgumentException">The module failed, implementation is absent, or input names/types do not match.</exception>
    public IReadOnlyDictionary<string, ShaderIrValue> Call(ShaderSourceModuleAnalysis module, string implementationId,
        IReadOnlyDictionary<string, ShaderIrValue> inputs)
    {
        EnsureWritable();
        ArgumentNullException.ThrowIfNull(module);
        ArgumentException.ThrowIfNullOrWhiteSpace(implementationId);
        ArgumentNullException.ThrowIfNull(inputs);
        if (!module.succeeded) throw new ArgumentException("A source call requires a fully validated module interface.", nameof(module));
        ShaderSourceImplementationAnalysis source = module.implementations.FirstOrDefault(value => value.implementationId == implementationId)
            ?? throw new ArgumentException($"Implementation '{implementationId}' is not part of this module.", nameof(implementationId));
        ShaderSourceFunction function = source.analysis.function!;
        ShaderSourceParameter[] parameters = function.parameters.Where(static value => value.direction != ShaderSourceParameterDirection.Output).ToArray();
        if (parameters.Length != inputs.Count) throw new ArgumentException("Source inputs must match every input/inout parameter exactly.", nameof(inputs));
        var operands = new List<ShaderIrValue>();
        foreach (ShaderSourceParameter parameter in parameters)
        {
            if (!inputs.TryGetValue(parameter.name, out ShaderIrValue? value))
                throw new ArgumentException($"Missing source input '{parameter.name}'.", nameof(inputs));
            RequireOwned(value);
            if (!value.type.IsEquivalentTo(parameter.type))
                throw new ArgumentException($"Source input '{parameter.name}' does not match its declared type.", nameof(inputs));
            operands.Add(value);
        }
        var outputs = new Dictionary<string, ShaderIrValue>(StringComparer.Ordinal);
        if (function.returnType.id != "void") outputs.Add("return", NewValue(function.returnType));
        foreach (ShaderSourceParameter parameter in function.parameters)
            if (parameter.direction != ShaderSourceParameterDirection.Input)
                outputs.Add("output." + parameter.name, NewValue(parameter.type));
        m_instructions.Add(new(ShaderIrOperation.SourceCall, operands, outputs.Values, source: source));
        return new ReadOnlyDictionary<string, ShaderIrValue>(outputs);
    }

    /// <summary>Freezes the current region without pruning unused calls or retaining this mutable builder.</summary>
    /// <param name="outputs">Named values returned by the enclosing region.</param>
    /// <returns>An immutable block unaffected by later builder changes.</returns>
    /// <exception cref="ArgumentException">An output name is empty or a value belongs to another builder.</exception>
    public ShaderIrBlock Build(IReadOnlyDictionary<string, ShaderIrValue> outputs)
    {
        ArgumentNullException.ThrowIfNull(outputs);
        foreach ((string name, ShaderIrValue value) in outputs)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            RequireOwned(value);
        }
        return new(m_instructions, outputs);
    }

    private ShaderIrValue ConstantBits(string type, ulong bits)
    {
        ShaderIrValue output = NewValue(ShaderSourceType.Atomic(type));
        m_instructions.Add(new(ShaderIrOperation.Constant, [], [output], constantBits: bits));
        return output;
    }

    private ShaderIrValue NewValue(ShaderSourceType type)
    {
        EnsureWritable();
        return new(m_owner, m_root.m_nextValue++, type);
    }

    internal void RequireOwned(ShaderIrValue value)
    {
        EnsureWritable();
        ArgumentNullException.ThrowIfNull(value);
        for (ShaderIrBuilder? scope = this; scope is not null; scope = scope.m_parent)
            if (ReferenceEquals(value.owner, scope.m_owner)) return;
        throw new ArgumentException("The value is outside this region's visible scope.", nameof(value));
    }

    private void EnsureWritable()
    {
        if (m_closed) throw new InvalidOperationException("A completed nested region cannot be edited after its callback returns.");
        if (m_buildingChild) throw new InvalidOperationException("An enclosing builder cannot be mutated while its nested region is being constructed.");
    }

    private static void RequireValueType(ShaderSourceType type)
    {
        ArgumentNullException.ThrowIfNull(type);
        if (type.id == "void") throw new ArgumentException("Void is not a value type.", nameof(type));
    }

    private static bool TryNumeric(ShaderSourceType type, out string scalar, out int columns, out int rows)
    {
        scalar = string.Empty;
        columns = rows = 1;
        if (type.elementType is not null || type.fields.Count != 0) return false;
        foreach (string name in new[] { "float", "int", "uint", "bool" })
        {
            if (!type.id.StartsWith(name, StringComparison.Ordinal)) continue;
            ReadOnlySpan<char> shape = type.id.AsSpan(name.Length);
            if (shape.Length == 0) { scalar = name; return true; }
            if (shape.Length == 1 && shape[0] is >= '2' and <= '4')
            { scalar = name; columns = shape[0] - '0'; return true; }
            if (name == "float" && shape.Length == 3 && shape[0] is >= '2' and <= '4' && shape[1] == 'x' && shape[2] is >= '2' and <= '4')
            { scalar = name; columns = shape[0] - '0'; rows = shape[2] - '0'; return true; }
        }
        return false;
    }
}
