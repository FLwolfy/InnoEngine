using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Inno.Rendering;
using Inno.Rendering.Shaders;

namespace Inno.Build.Toolchains.Bgfx.Tools;

internal sealed partial class BgfxShaderIrGenerator
{
    private string EmitModule(ShaderSourceImplementationAnalysis implementation)
    {
        if (implementation.languageId != "inno.shader-language.bgfx-sc")
            throw new BgfxSourceSyntaxException($"BGFX SC generation cannot embed language '{implementation.languageId}'.", new(implementation.sourcePath, 1, 1));
        ShaderSourceRequest request = implementation.CreateSourceRequest();
        List<BgfxSourceToken> tokens = new BgfxSourcePreprocessor(request).Process();
        var parser = new BgfxSourceDeclarationParser(tokens, request);
        ShaderSourceFunction parsed = parser.Parse();
        if (!parsed.HasSameInterface(implementation.analysis.function!)) Fail("The frozen source interface differs from its analyzed snapshot.");
        string prefix = "inno_module_" + implementation.contentHash.ToLowerInvariant() + "_";
        var symbols = parser.globals.ToDictionary(static value => value, value => prefix + value, StringComparer.Ordinal);
        // Shaderc injects varying macros before function declarations. Isolate module-local names
        // that collide with those generated bindings, including exported function parameters.
        foreach (string varying in stage.inputs.Where(static input => input.kind == ShaderIrInputKind.Varying)
            .Select(static input => VaryingName(input.semantic, input.location))
            .Concat(stage.outputs.Where(static output => output.kind == ShaderIrOutputKind.Varying)
                .Select(static output => VaryingName(output.semantic, output.location))))
            symbols.TryAdd(varying, prefix + varying);
        foreach ((string name, ShaderSourceType type) in parser.types)
        {
            if (type.elementType is not null)
                throw new BgfxSourceSyntaxException("BGFX SC does not support array typedef declarations; declare fixed arrays at their use sites.", new(implementation.sourcePath, 1, 1));
            symbols.Add(name, TypeName(type));
        }
        var removed = new HashSet<int>();
        foreach ((int start, int end) in parser.typeDeclarations)
            for (int index = start; index < end; index++) removed.Add(index);
        ShaderSourcePosition? position = null;
        string previous = string.Empty;
        for (int index = 0; index < tokens.Count; index++)
        {
            if (removed.Contains(index)) continue;
            BgfxSourceToken token = tokens[index];
            if (position?.assetPath != token.position.assetPath || position?.line != token.position.line)
            {
                // Shaderc disables #line emission in its preprocessing pass. Preserve a neutral source-map marker
                // on the actual code line instead; native diagnostics print these lines on compilation failure.
                m_modules.AppendLine().Append("/*inno_source_").Append(m_sourcePositions.Count).Append("*/ ");
                m_sourcePositions.Add(token.position);
                position = token.position;
            }
            string text = token.text;
            if (previous != "." && symbols.TryGetValue(text, out string? replacement)) text = replacement;
            m_modules.Append(text).Append(' ');
            previous = token.text;
        }
        m_modules.AppendLine();
        return symbols[implementation.entryPoint];
    }

    private string TypeName(ShaderSourceType type)
    {
        if (type.elementType is not null) return TypeName(type.elementType);
        if (type.fields.Count != 0)
        {
            string name = "InnoType_" + Hash(TypeKey(type));
            if (!m_structures.TryGetValue(name, out ShaderSourceType? previous))
            {
                m_structures.Add(name, type);
                var declaration = new StringBuilder("struct ").Append(name).AppendLine(" {");
                foreach (ShaderSourceField field in type.fields)
                    declaration.Append("    ").Append(Declaration(field.type, FieldName(field.name))).AppendLine(";");
                m_types.Append(declaration).AppendLine("};");
            }
            else if (!previous.IsEquivalentTo(type)) Fail("A generated structure identity collided.");
            return name;
        }
        if (type.id is "void" or "float" or "int" or "uint" or "bool") return type.id;
        if (type.id == "sampled-texture2d") return "sampler2D";
        if (type.id == "sampled-texture2d-array") return "sampler2DArray";
        if (type.id == "sampled-texture3d") return "sampler3D";
        if (type.id == "sampled-texture-cube") return "samplerCube";
        if (IsMatrix(type))
        {
            if (type.id[5] == type.id[7]) return "mat" + type.id[5];
            string name = "inno_mat" + type.id[5..];
            if (m_matrices.Add(name))
            {
                m_types.AppendLine("#if BGFX_SHADER_LANGUAGE_GLSL").Append("#define ").Append(name).Append(" mat").AppendLine(type.id[5..]);
                m_types.AppendLine("#elif BGFX_SHADER_MATRIX_COLUMN_MAJOR").Append("#define ").Append(name).Append(" float").AppendLine(type.id[5..]);
                m_types.AppendLine("#else").Append("#define ").Append(name).Append(" float").Append(type.id[7]).Append('x').Append(type.id[5]).AppendLine();
                m_types.AppendLine("#endif");
            }
            return name;
        }
        foreach ((string prefix, string native) in new[] { ("float", "vec"), ("int", "ivec"), ("uint", "uvec"), ("bool", "bvec") })
            if (type.id.Length == prefix.Length + 1 && type.id.StartsWith(prefix, StringComparison.Ordinal) && type.id[^1] is >= '2' and <= '4')
                return native + type.id[^1];
        throw Error($"BGFX SC cannot represent IR value type '{type.id}'.");
    }

    private string Declaration(ShaderSourceType type, string name)
    {
        var suffix = new StringBuilder();
        while (type.elementType is not null) { suffix.Append('[').Append(type.elementCount).Append(']'); type = type.elementType; }
        return TypeName(type) + " " + name + suffix;
    }

    private void Assign(StringBuilder text, ShaderSourceType type, string destination, string source)
    {
        if (type.elementType is not null)
        {
            for (int index = 0; index < type.elementCount; index++) Assign(text, type.elementType, $"{destination}[{index}]", $"{source}[{index}]");
        }
        else text.Append("    ").Append(destination).Append(" = ").Append(source).AppendLine(";");
    }

    private string NewLocal(ShaderIrValue value)
    {
        string name = "inno_value_" + value.index.ToString(CultureInfo.InvariantCulture);
        m_values.Add(value.index, name);
        return name;
    }

    private string Value(ShaderIrValue value) => m_values[value.index];
    private static string Binary(ShaderIrOperation operation, string left, string right) => operation switch
    {
        ShaderIrOperation.Add => $"({left} + {right})", ShaderIrOperation.Subtract => $"({left} - {right})",
        ShaderIrOperation.Multiply => $"({left} * {right})", ShaderIrOperation.Divide => $"({left} / {right})",
        ShaderIrOperation.Minimum => $"min({left}, {right})", ShaderIrOperation.Maximum => $"max({left}, {right})",
        ShaderIrOperation.Equal => $"({left} == {right})", ShaderIrOperation.LessThan => $"({left} < {right})",
        _ => throw Error($"Unsupported typed operation '{operation}'.")
    };

    private static string Constant(ShaderSourceType type, ulong bits) => type.id switch
    {
        "bool" => bits == 0 ? "false" : "true",
        "uint" => ((uint)bits).ToString(CultureInfo.InvariantCulture) + "u",
        "int" => unchecked((int)bits) == int.MinValue ? "(-2147483647 - 1)" : unchecked((int)bits).ToString(CultureInfo.InvariantCulture),
        "float" => FloatLiteral(BitConverter.UInt32BitsToSingle((uint)bits)),
        _ => throw Error("Only scalar constants have literal bit representations.")
    };

    private static string FloatLiteral(float value)
    {
        string text = value.ToString("R", CultureInfo.InvariantCulture);
        return text.Contains('.') || text.Contains('E') || text.Contains('e') ? text : text + ".0";
    }

    private static string TypeKey(ShaderSourceType type)
        => type.elementType is not null ? $"{type.id.Length}:{type.id}[{type.elementCount}]{TypeKey(type.elementType)}"
            : $"{type.id.Length}:{type.id}" + string.Concat(type.fields.Select(field => $"{field.name.Length}:{field.name}{TypeKey(field.type)}"));

    private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

    private static string BindingName(string prefix, string id)
    {
        // BGFX UniformRef stores only 63 visible bytes. Base32 preserves all 256 hash bits
        // while producing a valid SC identifier that fits that native reflection contract.
        const string alphabet = "abcdefghijklmnopqrstuvwxyz234567";
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(id));
        var result = new StringBuilder(prefix, 59);
        uint bits = 0;
        int count = 0;
        foreach (byte value in hash)
        {
            bits = (bits << 8) | value;
            count += 8;
            while (count >= 5)
            {
                count -= 5;
                result.Append(alphabet[(int)((bits >> count) & 31)]);
            }
        }
        if (count != 0) result.Append(alphabet[(int)((bits << (5 - count)) & 31)]);
        return result.ToString();
    }
    private static string MatrixElement(string matrix, int column, int row) => $"inno_matrix_element({matrix},{column},{row})";
    private static bool IsMatrix(ShaderSourceType type)
        => type.id.Length == 8 && type.id.StartsWith("float", StringComparison.Ordinal) && type.id[5] is >= '2' and <= '4' && type.id[6] == 'x' && type.id[7] is >= '2' and <= '4';

    private static string FieldName(string name)
    {
        if (name.Length == 0 || !(char.IsAsciiLetter(name[0]) || name[0] == '_') || name.Any(static c => !char.IsAsciiLetterOrDigit(c) && c != '_'))
            throw Error("BGFX SC structure field names must be identifiers.");
        return name;
    }

    private static string AttributeName(ShaderIrStageInput input) => (input.semantic, input.location) switch
    {
        ("position", 0) => "a_position", ("normal", 0) => "a_normal", ("tangent", 0) => "a_tangent", ("bitangent", 0) => "a_bitangent",
        ("indices", 0) => "a_indices", ("weight", 0) => "a_weight",
        ("color", >= 0 and <= 3) => "a_color" + input.location,
        ("texcoord", >= 0 and <= 7) => "a_texcoord" + input.location,
        ("instance-data", >= 0 and <= 4) => "i_data" + input.location,
        _ => throw Error($"BGFX cannot bind vertex semantic '{input.semantic}:{input.location}'.")
    };

    private static string Semantic(string semantic, int location) => (semantic, location) switch
    {
        ("position", 0) => "POSITION", ("normal", 0) => "NORMAL", ("tangent", 0) => "TANGENT", ("bitangent", 0) => "BITANGENT",
        ("indices", 0) => "BLENDINDICES", ("weight", 0) => "BLENDWEIGHT",
        ("texcoord", >= 0 and <= 15) => "TEXCOORD" + location,
        ("color", >= 0 and <= 3) => "COLOR" + location,
        ("instance-data", >= 0 and <= 4) => "TEXCOORD" + (7 - location),
        _ => throw Error($"BGFX cannot represent interface semantic '{semantic}:{location}'.")
    };

    private static string VaryingName(string semantic, int location)
    {
        if (semantic is not ("texcoord" or "color")) throw Error($"BGFX cannot interpolate semantic '{semantic}'.");
        _ = Semantic(semantic, location);
        return "v_" + semantic + location;
    }

    private static (string name, string type) Builtin(string semantic, ShaderStage kind) => (semantic, kind) switch
    {
        ("view-projection", ShaderStage.Vertex) => ("u_viewProj", "float4x4"),
        ("vertex-id", ShaderStage.Vertex) => ("gl_VertexID", "int"),
        ("instance-id", ShaderStage.Vertex) => ("gl_InstanceID", "int"),
        ("fragment-coordinate", ShaderStage.Fragment) => ("gl_FragCoord", "float4"),
        ("view-rectangle", ShaderStage.Fragment) => ("u_viewRect", "float4"),
        ("view-texel", ShaderStage.Fragment) => ("u_viewTexel", "float4"),
        ("front-facing", ShaderStage.Fragment) => ("gl_FrontFacing", "bool"),
        ("global-invocation-id", ShaderStage.Compute) => ("gl_GlobalInvocationID", "uint3"),
        ("local-invocation-id", ShaderStage.Compute) => ("gl_LocalInvocationID", "uint3"),
        ("workgroup-id", ShaderStage.Compute) => ("gl_WorkGroupID", "uint3"),
        _ => throw Error($"BGFX has no '{semantic}' builtin for stage '{kind}'.")
    };

    private static BgfxSourceSyntaxException Error(string message) => new(message, new("inno-generated-stage", 1, 1));
    private static void Fail(string message) => throw Error(message);
}
