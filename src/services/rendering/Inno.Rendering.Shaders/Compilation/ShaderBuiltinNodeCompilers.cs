using System;
using System.Collections.Generic;
using System.Linq;
using Inno.Core.Graphs;

namespace Inno.Rendering.Shaders;

/// <summary>Lowers an exact scalar constant; its type/value are native graph properties.</summary>
[ShaderNodeCompilerExtension]
public sealed class ShaderConstantNodeCompiler : IShaderNodeCompiler
{
    /// <inheritdoc />
    public string definitionId => "inno.shader.constant";
    /// <inheritdoc />
    public IReadOnlyList<ShaderNodePort> GetPorts(ShaderNodeDescriptionContext context)
        => [new("value", ShaderSourceType.Atomic(context.Read("type", "float")), GraphPortDirection.Output)];
    /// <inheritdoc />
    public IReadOnlyDictionary<string, ShaderIrValue> Lower(ShaderNodeLoweringContext context)
    {
        ShaderNodeDescriptionContext node = context.description;
        ShaderIrValue value = node.Read("type", "float") switch
        {
            "float" => context.builder.Constant(node.Read("value", 0f)),
            "int" => context.builder.Constant(node.Read("value", 0)),
            "uint" => context.builder.Constant(node.Read("value", 0u)),
            "bool" => context.builder.Constant(node.Read("value", false)),
            _ => throw new InvalidOperationException("A scalar constant must declare float, int, uint or bool.")
        };
        return new Dictionary<string, ShaderIrValue> { ["value"] = value };
    }
}

/// <summary>Lowers an explicit arithmetic/comparison operation; operation IDs are node configuration, not backend code.</summary>
[ShaderNodeCompilerExtension]
public sealed class ShaderBinaryNodeCompiler : IShaderNodeCompiler
{
    /// <inheritdoc />
    public string definitionId => "inno.shader.binary";
    /// <inheritdoc />
    public IReadOnlyList<ShaderNodePort> GetPorts(ShaderNodeDescriptionContext context)
    {
        ShaderSourceType type = ShaderSourceType.Atomic(context.Read("type", "float"));
        ShaderIrOperation operation = Operation(context);
        return [new("left", type, GraphPortDirection.Input), new("right", type, GraphPortDirection.Input),
            new("value", operation is ShaderIrOperation.Equal or ShaderIrOperation.LessThan ? ShaderSourceType.Atomic("bool") : type, GraphPortDirection.Output)];
    }
    /// <inheritdoc />
    public IReadOnlyDictionary<string, ShaderIrValue> Lower(ShaderNodeLoweringContext context)
        => new Dictionary<string, ShaderIrValue> { ["value"] = context.builder.Binary(Operation(context.description), context.Input("left"), context.Input("right")) };

    private static ShaderIrOperation Operation(ShaderNodeDescriptionContext context)
        => context.Read("operation", "add") switch
        {
            "add" => ShaderIrOperation.Add, "subtract" => ShaderIrOperation.Subtract,
            "multiply" => ShaderIrOperation.Multiply, "divide" => ShaderIrOperation.Divide,
            "minimum" => ShaderIrOperation.Minimum, "maximum" => ShaderIrOperation.Maximum,
            "equal" => ShaderIrOperation.Equal, "less-than" => ShaderIrOperation.LessThan,
            _ => throw new InvalidOperationException("Unknown binary shader operation.")
        };
}

/// <summary>Constructs a vector or column-major matrix from individually connected scalar components.</summary>
[ShaderNodeCompilerExtension]
public sealed class ShaderConstructNodeCompiler : IShaderNodeCompiler
{
    /// <inheritdoc />
    public string definitionId => "inno.shader.construct";
    /// <inheritdoc />
    public IReadOnlyList<ShaderNodePort> GetPorts(ShaderNodeDescriptionContext context)
    {
        (ShaderSourceType type, ShaderSourceType scalar, int count) = Shape(context);
        return Enumerable.Range(0, count).Select(index => new ShaderNodePort("component." + index, scalar, GraphPortDirection.Input))
            .Append(new("value", type, GraphPortDirection.Output)).ToArray();
    }
    /// <inheritdoc />
    public IReadOnlyDictionary<string, ShaderIrValue> Lower(ShaderNodeLoweringContext context)
    {
        (ShaderSourceType type, _, int count) = Shape(context.description);
        return new Dictionary<string, ShaderIrValue>
        { ["value"] = context.builder.Construct(type, Enumerable.Range(0, count).Select(index => context.Input("component." + index)).ToArray()) };
    }

    private static (ShaderSourceType type, ShaderSourceType scalar, int count) Shape(ShaderNodeDescriptionContext context)
    {
        string id = context.Read("type", "float4");
        foreach (string scalar in new[] { "float", "int", "uint", "bool" })
        {
            if (id.Length == scalar.Length + 1 && id.StartsWith(scalar, StringComparison.Ordinal) && id[^1] is >= '2' and <= '4')
                return (ShaderSourceType.Atomic(id), ShaderSourceType.Atomic(scalar), id[^1] - '0');
        }
        if (id.Length == 8 && id.StartsWith("float", StringComparison.Ordinal) && id[5] is >= '2' and <= '4' && id[6] == 'x' && id[7] is >= '2' and <= '4')
            return (ShaderSourceType.Atomic(id), ShaderSourceType.Atomic("float"), (id[5] - '0') * (id[7] - '0'));
        throw new InvalidOperationException("Construction requires a numeric vector or floating-point matrix.");
    }
}

/// <summary>Reads one target-assigned stage/resource input without choosing a native variable name.</summary>
[ShaderNodeCompilerExtension]
public sealed class ShaderStageInputNodeCompiler : IShaderNodeCompiler
{
    /// <inheritdoc />
    public string definitionId => "inno.shader.stage-input";
    /// <inheritdoc />
    public IReadOnlyList<ShaderNodePort> GetPorts(ShaderNodeDescriptionContext context)
        => [new("value", RequireBinding(context).type, GraphPortDirection.Output)];
    /// <inheritdoc />
    public IReadOnlyDictionary<string, ShaderIrValue> Lower(ShaderNodeLoweringContext context)
    {
        ShaderIrStageInput input = RequireBinding(context.description);
        return new Dictionary<string, ShaderIrValue> { ["value"] = context.builder.Input(input.id, input.type) };
    }
    private static ShaderIrStageInput RequireBinding(ShaderNodeDescriptionContext context)
        => context.stageInput ?? throw new InvalidOperationException("The target has not resolved this stage input binding.");
}

/// <summary>Selects between equal typed values; all producer effects remain evaluated before selection.</summary>
[ShaderNodeCompilerExtension]
public sealed class ShaderSelectNodeCompiler : IShaderNodeCompiler
{
    /// <inheritdoc />
    public string definitionId => "inno.shader.select";
    /// <inheritdoc />
    public IReadOnlyList<ShaderNodePort> GetPorts(ShaderNodeDescriptionContext context)
    {
        ShaderSourceType type = ShaderSourceType.Atomic(context.Read("type", "float"));
        return [new("condition", ShaderSourceType.Atomic("bool"), GraphPortDirection.Input), new("true", type, GraphPortDirection.Input),
            new("false", type, GraphPortDirection.Input), new("value", type, GraphPortDirection.Output)];
    }
    /// <inheritdoc />
    public IReadOnlyDictionary<string, ShaderIrValue> Lower(ShaderNodeLoweringContext context)
        => new Dictionary<string, ShaderIrValue> { ["value"] = context.builder.Select(context.Input("condition"), context.Input("true"), context.Input("false")) };
}

/// <summary>Lowers a parsed function module with name-based ports and explicit aggregate/member connection alternatives.</summary>
[ShaderNodeCompilerExtension]
public sealed class ShaderSourceNodeCompiler : IShaderNodeCompiler
{
    /// <inheritdoc />
    public string definitionId => "inno.shader.source";
    /// <inheritdoc />
    public IReadOnlyList<ShaderNodePort> GetPorts(ShaderNodeDescriptionContext context)
    {
        ShaderSourceFunction function = RequireModule(context).function!;
        var ports = new List<ShaderNodePort>();
        foreach (ShaderSourceParameter parameter in function.parameters)
        {
            if (parameter.direction != ShaderSourceParameterDirection.Output) Add("input." + parameter.name, parameter.type, GraphPortDirection.Input);
            if (parameter.direction != ShaderSourceParameterDirection.Input) Add("output." + parameter.name, parameter.type, GraphPortDirection.Output);
        }
        if (function.returnType.id != "void") Add("return", function.returnType, GraphPortDirection.Output);
        return ports.AsReadOnly();
        void Add(string name, ShaderSourceType type, GraphPortDirection direction)
        {
            ports.Add(new(name, type, direction, required: false));
            foreach (ShaderSourceField field in type.fields) Add(name + "." + field.name, field.type, direction);
        }
    }
    /// <inheritdoc />
    public IReadOnlyDictionary<string, ShaderIrValue> Lower(ShaderNodeLoweringContext context)
    {
        ShaderSourceModuleAnalysis module = RequireModule(context.description);
        var arguments = new Dictionary<string, ShaderIrValue>(StringComparer.Ordinal);
        foreach (ShaderSourceParameter parameter in module.function!.parameters)
            if (parameter.direction != ShaderSourceParameterDirection.Output)
                arguments.Add(parameter.name, Resolve("input." + parameter.name, parameter.type));
        IReadOnlyDictionary<string, ShaderIrValue> values = context.builder.Call(module, context.description.implementationId, arguments);
        var outputs = new Dictionary<string, ShaderIrValue>(StringComparer.Ordinal);
        foreach ((string name, ShaderIrValue value) in values) Add(name, value);
        return outputs;

        ShaderIrValue Resolve(string name, ShaderSourceType type)
        {
            if (context.inputs.TryGetValue(name, out ShaderIrValue? aggregate))
            {
                if (context.inputs.Keys.Any(key => key.StartsWith(name + ".", StringComparison.Ordinal)))
                    throw new InvalidOperationException($"'{name}' connects both its aggregate and a member; choose one representation explicitly.");
                return aggregate;
            }
            if (type.fields.Count == 0) return context.Input(name);
            return context.builder.Construct(type, type.fields.Select(field => Resolve(name + "." + field.name, field.type)).ToArray());
        }

        void Add(string name, ShaderIrValue value)
        {
            outputs.Add(name, value);
            for (int index = 0; index < value.type.fields.Count; index++)
                Add(name + "." + value.type.fields[index].name, context.builder.Extract(value, index));
        }
    }

    private static ShaderSourceModuleAnalysis RequireModule(ShaderNodeDescriptionContext context)
    {
        ShaderSourceModuleAnalysis? module = context.sourceModule;
        if (module is null) throw new InvalidOperationException("The source module is unassigned or unavailable; its stored reference and connections must remain intact.");
        if (!module.succeeded) throw new InvalidOperationException("The source module has no valid common interface: " + string.Join("; ", module.diagnostics.Select(static value => value.message)));
        return module;
    }
}
