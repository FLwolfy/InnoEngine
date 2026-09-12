using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Inno.Rendering.Shaders;

/// <summary>Identifies a typed single-assignment value within one intermediate block.</summary>
public sealed class ShaderIrValue
{
    internal ShaderIrValue(object owner, int index, ShaderSourceType type)
    {
        this.owner = owner;
        this.index = index;
        this.type = type;
    }

    /// <summary>Gets the deterministic block-local value index, not a GPU register or resource slot.</summary>
    public int index { get; }
    /// <summary>Gets the complete language-independent value type.</summary>
    public ShaderSourceType type { get; }
    internal object owner { get; }
}

/// <summary>Defines backend-neutral value operations; graph node IDs are not instruction opcodes.</summary>
public enum ShaderIrOperation
{
    /// <summary>Reads a named input supplied by the enclosing function or stage.</summary>
    Input,
    /// <summary>Produces an exact scalar constant stored as bits, not source-language text.</summary>
    Constant,
    /// <summary>Constructs an aggregate from typed scalar or member values.</summary>
    Construct,
    /// <summary>Extracts one statically selected vector component, array element or structure field.</summary>
    Extract,
    /// <summary>Adds two equal numeric types component by component.</summary>
    Add,
    /// <summary>Subtracts two equal numeric types component by component.</summary>
    Subtract,
    /// <summary>Multiplies two equal numeric types component by component; this is not matrix multiplication.</summary>
    Multiply,
    /// <summary>Divides two equal numeric types component by component.</summary>
    Divide,
    /// <summary>Selects the component-wise minimum of two equal numeric types.</summary>
    Minimum,
    /// <summary>Selects the component-wise maximum of two equal numeric types.</summary>
    Maximum,
    /// <summary>Tests equality of two scalar values and produces a scalar Boolean.</summary>
    Equal,
    /// <summary>Compares two equal numeric scalar types and produces a scalar Boolean.</summary>
    LessThan,
    /// <summary>Selects between equal types using a scalar Boolean; it does not introduce control flow.</summary>
    Select,
    /// <summary>Calls a parsed source function with named inputs and ordered multiple outputs.</summary>
    SourceCall,
    /// <summary>Samples a floating-point texture using implicit fragment derivatives.</summary>
    TextureSample,
    /// <summary>Samples a floating-point texture at an explicit mip level.</summary>
    TextureSampleLevel,
    /// <summary>Loads one typed buffer element or formatted image texel in memory-effect order.</summary>
    StorageLoad,
    /// <summary>Stores one typed buffer element or formatted image texel in memory-effect order.</summary>
    StorageStore,
    /// <summary>Atomically adds to an integer buffer element and returns the original value.</summary>
    StorageAtomicAdd,
    /// <summary>Discards the current fragment when the scalar condition is true.</summary>
    Discard,
    /// <summary>Provides a typed iteration index or loop-carried value inside a nested region.</summary>
    RegionInput,
    /// <summary>Executes exactly one nested region and merges its named output values.</summary>
    Branch,
    /// <summary>Executes a counted loop with explicit typed carried values and ordered body effects.</summary>
    Loop
}

/// <summary>Stores one immutable typed instruction without generated stage source or backend expressions.</summary>
public sealed class ShaderIrInstruction
{
    internal ShaderIrInstruction(ShaderIrOperation operation, IEnumerable<ShaderIrValue> inputs,
        IEnumerable<ShaderIrValue> outputs, string? inputName = null, ulong constantBits = 0, int memberIndex = 0,
        ShaderSourceImplementationAnalysis? source = null, IEnumerable<ShaderIrBlock>? regions = null)
    {
        this.operation = operation;
        this.inputs = Array.AsReadOnly(inputs.ToArray());
        this.outputs = Array.AsReadOnly(outputs.ToArray());
        this.inputName = inputName;
        this.constantBits = constantBits;
        this.memberIndex = memberIndex;
        this.source = source;
        this.regions = Array.AsReadOnly(regions?.ToArray() ?? []);
    }

    /// <summary>Gets the semantic operation independent of any source language.</summary>
    public ShaderIrOperation operation { get; }
    /// <summary>Gets immutable operands in operation or function-declaration order.</summary>
    public IReadOnlyList<ShaderIrValue> inputs { get; }
    /// <summary>Gets produced values; a call orders its return value first, then out/inout parameters.</summary>
    public IReadOnlyList<ShaderIrValue> outputs { get; }
    /// <summary>Gets the stable interface or region-parameter identity, only for Input or RegionInput.</summary>
    public string? inputName { get; }
    /// <summary>Gets exact scalar bits for a Constant instruction; the result type determines interpretation.</summary>
    public ulong constantBits { get; }
    /// <summary>Gets the zero-based component/member/element ordinal for an Extract instruction.</summary>
    public int memberIndex { get; }
    /// <summary>Gets a frozen, analyzed function implementation only for a SourceCall instruction.</summary>
    public ShaderSourceImplementationAnalysis? source { get; }
    /// <summary>Gets immutable nested regions: true/false for Branch, or one body for Loop.</summary>
    public IReadOnlyList<ShaderIrBlock> regions { get; }
    /// <summary>Gets whether removing or reordering this instruction may change observable behavior.</summary>
    public bool hasSideEffects => operation is ShaderIrOperation.SourceCall or ShaderIrOperation.StorageLoad
        or ShaderIrOperation.StorageStore or ShaderIrOperation.StorageAtomicAdd or ShaderIrOperation.Discard
        or ShaderIrOperation.Branch or ShaderIrOperation.Loop;
}

/// <summary>Contains immutable ordered instructions and nested structured regions without retaining builder callbacks.</summary>
public sealed class ShaderIrBlock
{
    internal ShaderIrBlock(IEnumerable<ShaderIrInstruction> instructions, IReadOnlyDictionary<string, ShaderIrValue> outputs)
    {
        this.instructions = Array.AsReadOnly(instructions.ToArray());
        this.outputs = new ReadOnlyDictionary<string, ShaderIrValue>(new Dictionary<string, ShaderIrValue>(outputs, StringComparer.Ordinal));
    }

    /// <summary>Gets instructions in evaluation order, including source calls whose outputs are unused.</summary>
    public IReadOnlyList<ShaderIrInstruction> instructions { get; }
    /// <summary>Gets named values returned to the enclosing stage or region.</summary>
    public IReadOnlyDictionary<string, ShaderIrValue> outputs { get; }
}
