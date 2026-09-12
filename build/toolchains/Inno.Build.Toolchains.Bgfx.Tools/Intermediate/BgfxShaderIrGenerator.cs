using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Inno.Rendering;
using Inno.Rendering.Assets;
using Inno.Rendering.Shaders;

namespace Inno.Build.Toolchains.Bgfx.Tools;

internal sealed record BgfxGeneratedStage(string source, string varying, IReadOnlyList<ShaderStageBinding> bindings,
    IReadOnlyList<ShaderSourcePosition> sourcePositions);

// SC spelling, hardware semantics and module symbol isolation belong only to this adapter.
internal sealed partial class BgfxShaderIrGenerator(ShaderIrStage stage)
{
    private readonly StringBuilder m_types = new();
    private readonly StringBuilder m_modules = new();
    private readonly StringBuilder m_body = new();
    private readonly Dictionary<string, ShaderSourceType> m_structures = new(StringComparer.Ordinal);
    private readonly HashSet<string> m_matrices = new(StringComparer.Ordinal);
    private readonly Dictionary<int, string> m_values = [];
    private readonly Dictionary<string, string> m_functions = new(StringComparer.Ordinal);
    private readonly List<ShaderStageBinding> m_bindings = [];
    private readonly List<ShaderSourcePosition> m_sourcePositions = [];

    internal BgfxGeneratedStage Generate()
    {
        var header = new StringBuilder();
        var varying = new StringBuilder();
        // Shaderc requires a nonempty file even for stages with no interpolators. A comment declares no fake input.
        varying.AppendLine("// Generated stage interface; an empty declaration set is intentional.");
        var declarations = new StringBuilder();
        var stageInputs = new Dictionary<string, string>(StringComparer.Ordinal);
        var inputNames = new List<string>();
        var outputNames = new List<string>();
        foreach (ShaderIrStageInput input in stage.inputs.OrderBy(static input => input.id, StringComparer.Ordinal))
        {
            string name;
            switch (input.kind)
            {
                case ShaderIrInputKind.VertexAttribute:
                    name = AttributeName(input);
                    inputNames.Add(name);
                    varying.Append(Declaration(input.type, name)).Append(" : ").Append(Semantic(input.semantic, input.location)).AppendLine(";");
                    break;
                case ShaderIrInputKind.Varying:
                    name = VaryingName(input.semantic, input.location);
                    inputNames.Add(name);
                    varying.Append(Declaration(input.type, name)).Append(" : ").Append(Semantic(input.semantic, input.location)).AppendLine(";");
                    break;
                case ShaderIrInputKind.Uniform:
                    name = BindingName("u_inno_", input.id);
                    bool packed = input.type.id is "float" or "float2" or "float3";
                    if (!packed && input.type.id is not ("float4" or "float3x3" or "float4x4") && input.type.elementType?.id is not ("float4" or "float3x3" or "float4x4"))
                        Fail("BGFX uniform layout requires vec4, mat3 or mat4 storage (or fixed arrays); lower scalar/vector parameters through explicit layout components.");
                    declarations.Append("uniform ").Append(Declaration(packed ? ShaderSourceType.Atomic("float4") : input.type, name)).AppendLine(";");
                    m_bindings.Add(new(input.id, name, input.location));
                    if (packed) name += input.type.id switch { "float" => ".x", "float2" => ".xy", _ => ".xyz" };
                    break;
                case ShaderIrInputKind.SampledTexture:
                    name = BindingName("s_inno_", input.id);
                    string macro = input.type.id switch
                    {
                        "sampled-texture2d" => "SAMPLER2D", "sampled-texture2d-array" => "SAMPLER2DARRAY",
                        "sampled-texture3d" => "SAMPLER3D", "sampled-texture-cube" => "SAMPLERCUBE",
                        _ => throw Error($"Unsupported sampled resource '{input.type.id}'.")
                    };
                    declarations.Append(macro).Append('(').Append(name).Append(", ").Append(input.location).AppendLine(");");
                    m_bindings.Add(new(input.id, name, input.location));
                    break;
                case ShaderIrInputKind.Builtin:
                    (name, string type) = Builtin(input.semantic, stage.stage);
                    if (!input.type.IsEquivalentTo(ShaderSourceType.Atomic(type))) Fail($"Builtin '{input.semantic}' requires {type}, not {input.type.id}.");
                    break;
                case ShaderIrInputKind.Storage:
                    name = BindingName("r_inno_", input.id);
                    EmitStorageDeclaration(declarations, input, name);
                    m_bindings.Add(new(input.id, name, input.location));
                    break;
                default: throw Error("Unsupported stage input kind.");
            }
            stageInputs.Add(input.id, name);
        }
        foreach (ShaderIrStageOutput output in stage.outputs)
            if (output.kind == ShaderIrOutputKind.Varying)
            {
                string name = VaryingName(output.semantic, output.location);
                outputNames.Add(name);
                varying.Append(Declaration(stage.body.outputs[output.id].type, name)).Append(" : ")
                    .Append(Semantic(output.semantic, output.location)).AppendLine(";");
            }
        if (inputNames.Count != 0) header.Append("$input ").AppendJoin(", ", inputNames.Order(StringComparer.Ordinal)).AppendLine();
        if (outputNames.Count != 0) header.Append("$output ").AppendJoin(", ", outputNames.Order(StringComparer.Ordinal)).AppendLine();
        header.AppendLine("#include <bgfx_shader.sh>");
        header.AppendLine("#if BGFX_SHADER_MATRIX_COLUMN_MAJOR\n#define inno_matrix_element(m,c,r) ((m)[c][r])\n#else\n#define inno_matrix_element(m,c,r) ((m)[r][c])\n#endif");
        if (stage.stage == ShaderStage.Compute) header.AppendLine("#include <bgfx_compute.sh>");
        foreach (ShaderIrInstruction instruction in stage.body.instructions)
        {
            if (instruction.operation == ShaderIrOperation.Input)
            {
                m_values.Add(instruction.outputs[0].index, stageInputs[instruction.inputName!]);
                continue;
            }
            EmitInstruction(instruction);
        }
        foreach (ShaderIrStageOutput output in stage.outputs)
        {
            string destination = output.kind switch
            {
                ShaderIrOutputKind.ClipPosition => "gl_Position",
                ShaderIrOutputKind.Varying => VaryingName(output.semantic, output.location),
                ShaderIrOutputKind.Depth => "gl_FragDepth",
                ShaderIrOutputKind.Color => stage.outputs.Count(static value => value.kind == ShaderIrOutputKind.Color) == 1 && output.location == 0
                    ? "gl_FragColor" : $"gl_FragData[{output.location}]",
                _ => throw Error("Unsupported stage output.")
            };
            ShaderIrValue value = stage.body.outputs[output.id];
            Assign(m_body, value.type, destination, Value(value));
        }
        var source = new StringBuilder().Append(header).Append(m_types).Append(declarations).Append(m_modules);
        source.AppendLine("#line 1 \"inno-generated-stage\"");
        if (stage.stage == ShaderStage.Compute) source.Append("NUM_THREADS(").Append(stage.threadsX).Append(", ").Append(stage.threadsY).Append(", ").Append(stage.threadsZ).AppendLine(")");
        source.AppendLine("void main() {").Append(m_body).AppendLine("}");
        return new(source.ToString(), varying.ToString(), m_bindings.AsReadOnly(), m_sourcePositions.AsReadOnly());
    }

    private void EmitInstruction(ShaderIrInstruction instruction)
    {
        if (instruction.operation == ShaderIrOperation.SourceCall) { EmitCall(instruction); return; }
        if (EmitControlFlow(instruction)) return;
        if (EmitResourceInstruction(instruction)) return;
        ShaderIrValue output = instruction.outputs[0];
        string name = NewLocal(output);
        m_body.Append("    ").Append(Declaration(output.type, name)).AppendLine(";");
        string[] operands = instruction.inputs.Select(Value).ToArray();
        switch (instruction.operation)
        {
            case ShaderIrOperation.Constant:
                Assign(m_body, output.type, name, Constant(output.type, instruction.constantBits));
                break;
            case ShaderIrOperation.Construct:
                if (output.type.elementType is not null)
                    for (int index = 0; index < operands.Length; index++) Assign(m_body, output.type.elementType, $"{name}[{index}]", operands[index]);
                else if (output.type.fields.Count != 0)
                    for (int index = 0; index < operands.Length; index++) Assign(m_body, output.type.fields[index].type, name + "." + FieldName(output.type.fields[index].name), operands[index]);
                else if (IsMatrix(output.type))
                {
                    int columns = output.type.id[5] - '0';
                    int rows = output.type.id[7] - '0';
                    for (int column = 0; column < columns; column++)
                        for (int row = 0; row < rows; row++) Assign(m_body, ShaderSourceType.Atomic("float"),
                            MatrixElement(name, column, row), operands[column * rows + row]);
                }
                else Assign(m_body, output.type, name, TypeName(output.type) + "(" + string.Join(", ", operands) + ")");
                break;
            case ShaderIrOperation.Extract:
                ShaderSourceType aggregate = instruction.inputs[0].type;
                if (IsMatrix(aggregate))
                {
                    int rows = aggregate.id[7] - '0';
                    for (int row = 0; row < rows; row++) Assign(m_body, ShaderSourceType.Atomic("float"), $"{name}[{row}]",
                        MatrixElement(operands[0], instruction.memberIndex, row));
                    break;
                }
                string suffix = aggregate.fields.Count != 0 ? "." + FieldName(aggregate.fields[instruction.memberIndex].name) : $"[{instruction.memberIndex}]";
                Assign(m_body, output.type, name, operands[0] + suffix);
                break;
            case ShaderIrOperation.Select:
                m_body.Append("    if (").Append(operands[0]).AppendLine(") {");
                Assign(m_body, output.type, name, operands[1]);
                m_body.AppendLine("    } else {");
                Assign(m_body, output.type, name, operands[2]);
                m_body.AppendLine("    }");
                break;
            default:
                if (IsMatrix(output.type))
                {
                    int columns = output.type.id[5] - '0';
                    int rows = output.type.id[7] - '0';
                    for (int column = 0; column < columns; column++)
                        for (int row = 0; row < rows; row++)
                        {
                            Assign(m_body, ShaderSourceType.Atomic("float"), MatrixElement(name, column, row),
                                Binary(instruction.operation, MatrixElement(operands[0], column, row), MatrixElement(operands[1], column, row)));
                        }
                }
                else Assign(m_body, output.type, name, Binary(instruction.operation, operands[0], operands[1]));
                break;
        }
    }

    private void EmitCall(ShaderIrInstruction instruction)
    {
        ShaderSourceImplementationAnalysis implementation = instruction.source!;
        if (!m_functions.TryGetValue(implementation.contentHash, out string? functionName))
        {
            functionName = EmitModule(implementation);
            m_functions.Add(implementation.contentHash, functionName);
        }
        ShaderSourceFunction function = implementation.analysis.function!;
        int inputIndex = 0;
        int outputIndex = function.returnType.id == "void" ? 0 : 1;
        foreach (ShaderIrValue output in instruction.outputs)
            m_body.Append("    ").Append(Declaration(output.type, NewLocal(output))).AppendLine(";");
        var arguments = new List<string>();
        foreach (ShaderSourceParameter parameter in function.parameters)
        {
            if (parameter.direction == ShaderSourceParameterDirection.Input) arguments.Add(Value(instruction.inputs[inputIndex++]));
            else
            {
                string output = Value(instruction.outputs[outputIndex++]);
                if (parameter.direction == ShaderSourceParameterDirection.InputOutput)
                    Assign(m_body, parameter.type, output, Value(instruction.inputs[inputIndex++]));
                arguments.Add(output);
            }
        }
        string call = functionName + "(" + string.Join(", ", arguments) + ")";
        if (function.returnType.id == "void") m_body.Append("    ").Append(call).AppendLine(";");
        else Assign(m_body, function.returnType, Value(instruction.outputs[0]), call);
    }
}
