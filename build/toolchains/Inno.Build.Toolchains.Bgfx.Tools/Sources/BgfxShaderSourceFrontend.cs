using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Inno.Core.Diagnostics;
using Inno.Rendering.Shaders;

namespace Inno.Build.Toolchains.Bgfx.Tools;

/// <summary>Parses BGFX SC function modules without placing BGFX grammar in the common shader model.</summary>
[ShaderSourceFrontendExtension]
public sealed class BgfxShaderSourceFrontend : IShaderSourceFrontend
{
    /// <inheritdoc />
    public string languageId => "inno.shader-language.bgfx-sc";

    /// <inheritdoc />
    public ShaderSourceAnalysis Analyze(ShaderSourceRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var preprocessor = new BgfxSourcePreprocessor(request);
        try
        {
            List<BgfxSourceToken> tokens = preprocessor.Process();
            ShaderSourceFunction function = new BgfxSourceDeclarationParser(tokens, request).Parse();
            return new(function, preprocessor.dependencies, []);
        }
        catch (BgfxSourceSyntaxException failure)
        {
            return new(null, preprocessor.dependencies,
                [new ShaderSourceDiagnostic("SHADER_SOURCE_INTERFACE", DiagnosticSeverity.Error, failure.Message, failure.position)]);
        }
    }
}

internal sealed class BgfxSourceDeclarationParser(List<BgfxSourceToken> tokens, ShaderSourceRequest request)
{
    private readonly Dictionary<string, ShaderSourceType> m_types = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> m_constants = new(StringComparer.Ordinal);
    private readonly List<ShaderSourceFunction> m_exports = [];
    private readonly List<(IReadOnlyList<ShaderSourceParameter> parameters, List<BgfxSourceToken> body)> m_bodies = [];
    private int m_offset;
    internal IReadOnlyDictionary<string, ShaderSourceType> types => m_types;
    internal HashSet<string> globals { get; } = new(StringComparer.Ordinal);
    internal List<(int start, int end)> typeDeclarations { get; } = [];

    internal ShaderSourceFunction Parse()
    {
        while (m_offset < tokens.Count)
        {
            if (Match(";")) continue;
            int declarationStart = m_offset;
            if (Peek("struct")) { ReadStructure(); typeDeclarations.Add((declarationStart, m_offset)); continue; }
            bool alias = Match("typedef");
            bool constant = false;
            while (m_offset < tokens.Count && IsQualifier(tokens[m_offset].text))
                constant |= tokens[m_offset++].text == "const";
            ShaderSourceType type = ReadType();
            BgfxSourceToken name = ReadIdentifier();
            if (Match("("))
            {
                if (alias) Fail("Function typedefs are not source module value types.", name);
                List<ShaderSourceParameter> parameters = ReadParameters();
                if (name.text == "main") Fail("A source module cannot declare main(); shader entries are generated from graph outputs.", name);
                globals.Add(name.text);
                if (Match(";")) continue;
                Require("{");
                m_bodies.Add((parameters, ReadBody()));
                if (name.text == request.entryPoint)
                {
                    if (!StringComparer.Ordinal.Equals(name.position.assetPath, request.source.assetPath))
                        Fail("The exported function must be declared in its selected source file, not an include.", name);
                    m_exports.Add(new(name.text, type, parameters, name.position));
                }
                continue;
            }
            type = ReadArray(type);
            if (alias)
            {
                if (!m_types.TryAdd(name.text, type)) Fail($"Type '{name.text}' is already declared.", name);
                Require(";");
                typeDeclarations.Add((declarationStart, m_offset));
                continue;
            }
            if (!constant) Fail("Module globals must be constants; stage inputs and resource bindings belong to the graph target.", name);
            globals.Add(name.text);
            if (Match("="))
            {
                List<BgfxSourceToken> initializer = ReadUntilSemicolon();
                if (type.id is "int" or "uint")
                    m_constants[name.text] = EvaluateInteger(initializer, name);
            }
            else Require(";");
        }
        if (m_exports.Count != 1)
            throw new BgfxSourceSyntaxException(m_exports.Count == 0
                ? $"Source does not define the selected public function '{request.entryPoint}'."
                : $"Public function '{request.entryPoint}' is overloaded or defined more than once; select an unambiguous export.",
                new(request.source.assetPath, 1, 1));
        ValidateExplicitInterfaces();
        return m_exports[0];
    }

    private List<ShaderSourceParameter> ReadParameters()
    {
        var parameters = new List<ShaderSourceParameter>();
        if (Match(")")) return parameters;
        if (Peek("void") && m_offset + 1 < tokens.Count && tokens[m_offset + 1].text == ")")
        { m_offset += 2; return parameters; }
        do
        {
            ShaderSourceParameterDirection direction = ShaderSourceParameterDirection.Input;
            bool directionAssigned = false;
            while (m_offset < tokens.Count)
            {
                BgfxSourceToken qualifier = tokens[m_offset];
                if (qualifier.text is "in" or "out" or "inout")
                {
                    if (directionAssigned) Fail("A parameter has more than one direction qualifier.", qualifier);
                    directionAssigned = true;
                    direction = qualifier.text switch
                    {
                        "out" => ShaderSourceParameterDirection.Output,
                        "inout" => ShaderSourceParameterDirection.InputOutput,
                        _ => ShaderSourceParameterDirection.Input
                    };
                    m_offset++;
                }
                else if (IsQualifier(qualifier.text)) m_offset++;
                else break;
            }
            ShaderSourceType type = ReadType();
            BgfxSourceToken name = ReadIdentifier();
            if (type.id == "void") Fail("A parameter cannot have void type.", name);
            type = ReadArray(type);
            if (parameters.Any(parameter => parameter.name == name.text)) Fail("Duplicate public parameter name.", name);
            parameters.Add(new(name.text, type, direction));
        } while (Match(","));
        Require(")");
        return parameters;
    }

    private void ReadStructure()
    {
        Require("struct");
        BgfxSourceToken name = ReadIdentifier();
        Require("{");
        var fields = new List<ShaderSourceField>();
        while (!Match("}"))
        {
            while (m_offset < tokens.Count && IsQualifier(tokens[m_offset].text)) m_offset++;
            ShaderSourceType type = ReadType();
            if (type.id == "void") Fail("A structure field cannot have void type.", name);
            do
            {
                BgfxSourceToken field = ReadIdentifier();
                ShaderSourceType fieldType = ReadArray(type);
                if (fields.Any(existing => existing.name == field.text)) Fail("Duplicate structure member name.", field);
                fields.Add(new(field.text, fieldType));
            } while (Match(","));
            Require(";");
        }
        Require(";");
        if (fields.Count == 0) Fail("A structure requires at least one field.", name);
        if (!m_types.TryAdd(name.text, ShaderSourceType.Structure("struct:" + name.text, fields)))
            Fail($"Type '{name.text}' is already declared.", name);
    }

    private ShaderSourceType ReadType()
    {
        BgfxSourceToken token = ReadIdentifier();
        if (m_types.TryGetValue(token.text, out ShaderSourceType? declared)) return declared;
        string? canonical = token.text switch
        {
            "void" or "bool" or "int" or "uint" or "float" or "double" => token.text,
            "half" => "float16",
            "vec2" or "float2" => "float2", "vec3" or "float3" => "float3", "vec4" or "float4" => "float4",
            "ivec2" or "int2" => "int2", "ivec3" or "int3" => "int3", "ivec4" or "int4" => "int4",
            "uvec2" or "uint2" => "uint2", "uvec3" or "uint3" => "uint3", "uvec4" or "uint4" => "uint4",
            "bvec2" or "bool2" => "bool2", "bvec3" or "bool3" => "bool3", "bvec4" or "bool4" => "bool4",
            "mat2" or "float2x2" => "float2x2", "mat3" or "float3x3" => "float3x3", "mat4" or "float4x4" => "float4x4",
            "sampler2D" or "BgfxSampler2D" => "sampled-texture2d",
            "sampler2DArray" or "BgfxSampler2DArray" => "sampled-texture2d-array",
            "sampler3D" or "BgfxSampler3D" => "sampled-texture3d",
            "samplerCube" or "BgfxSamplerCube" => "sampled-texture-cube",
            _ => null
        };
        if (canonical is null) Fail($"Unsupported or undeclared source type '{token.text}'.", token);
        return ShaderSourceType.Atomic(canonical!);
    }

    private ShaderSourceType ReadArray(ShaderSourceType element)
    {
        var lengths = new List<int>();
        while (Match("["))
        {
            BgfxSourceToken start = Current();
            if (element.id == "void") Fail("An array cannot contain void values.", start);
            var expression = new List<BgfxSourceToken>();
            while (!Match("]")) expression.Add(Next());
            long count = EvaluateInteger(expression, start);
            if (count <= 0 || count > int.MaxValue) Fail("Function interface arrays require a positive, fixed 32-bit length.", start);
            lengths.Add((int)count);
        }
        for (int i = lengths.Count - 1; i >= 0; i--) element = ShaderSourceType.ArrayOf(element, lengths[i]);
        return element;
    }

    private long EvaluateInteger(List<BgfxSourceToken> expression, BgfxSourceToken location)
    {
        if (expression.Count == 0) Fail("Expected a fixed integer expression.", location);
        var values = new List<BgfxSourceToken>();
        foreach (BgfxSourceToken token in expression)
        {
            if (!token.isIdentifier) { values.Add(token); continue; }
            if (!m_constants.TryGetValue(token.text, out long constant)) Fail($"Unresolved array constant '{token.text}'.", token);
            values.Add(new("(", token.position));
            foreach (BgfxSourceToken literal in BgfxSourceLexer.Tokenize(new(token.position.assetPath,
                         constant.ToString(CultureInfo.InvariantCulture))))
                values.Add(literal with { position = token.position });
            values.Add(new(")", token.position));
        }
        return new BgfxPreprocessorExpression(values).Evaluate();
    }

    private List<BgfxSourceToken> ReadUntilSemicolon()
    {
        var result = new List<BgfxSourceToken>();
        int braces = 0;
        int parentheses = 0;
        while (true)
        {
            BgfxSourceToken token = Next();
            if (token.text == ";" && braces == 0 && parentheses == 0) return result;
            if (token.text == "{") braces++;
            if (token.text == "}") braces--;
            if (token.text == "(") parentheses++;
            if (token.text == ")") parentheses--;
            result.Add(token);
        }
    }

    private List<BgfxSourceToken> ReadBody()
    {
        var body = new List<BgfxSourceToken>();
        int nesting = 1;
        while (nesting != 0)
        {
            BgfxSourceToken token = Next();
            if (token.text == "{") nesting++;
            if (token.text == "}") nesting--;
            body.Add(token);
        }
        return body;
    }

    private void ValidateExplicitInterfaces()
    {
        foreach ((IReadOnlyList<ShaderSourceParameter> parameters, List<BgfxSourceToken> body) in m_bodies)
        {
            var explicitNames = new HashSet<string>(parameters.Select(static value => value.name), StringComparer.Ordinal);
            explicitNames.UnionWith(globals);
            for (int index = 0; index < body.Count; index++)
            {
                BgfxSourceToken token = body[index];
                if (index != 0 && body[index - 1].text == ".") continue;
                if (!explicitNames.Contains(token.text) && IsImplicitStageName(token.text))
                    Fail($"Source functions must receive '{token.text}' through an explicit parameter; native stage bindings are generated by the graph target.", token);
            }
        }
    }

    private static bool IsImplicitStageName(string name)
        => name.StartsWith("gl_", StringComparison.Ordinal) || name.StartsWith("inno_", StringComparison.Ordinal)
            || name.StartsWith("u_inno_", StringComparison.Ordinal) || name.StartsWith("s_inno_", StringComparison.Ordinal)
            || name.StartsWith("v_texcoord", StringComparison.Ordinal) || name.StartsWith("v_color", StringComparison.Ordinal)
            || name.StartsWith("i_data", StringComparison.Ordinal) || name.StartsWith("a_texcoord", StringComparison.Ordinal)
            || name.StartsWith("a_color", StringComparison.Ordinal)
            || name is "a_position" or "a_normal" or "a_tangent" or "a_bitangent" or "a_weight" or "a_indices"
                or "u_viewRect" or "u_viewTexel" or "u_view" or "u_invView" or "u_proj" or "u_invProj" or "u_viewProj"
                or "u_invViewProj" or "u_model" or "u_modelView" or "u_invModelView" or "u_modelViewProj" or "u_alphaRef4";

    private BgfxSourceToken ReadIdentifier()
    {
        BgfxSourceToken token = Next();
        if (!token.isIdentifier) Fail($"Expected an identifier, found '{token.text}'.", token);
        return token;
    }

    private BgfxSourceToken Current()
    {
        if (m_offset >= tokens.Count)
            throw new BgfxSourceSyntaxException("Unexpected end of source declaration.", tokens.Count == 0
                ? new(request.source.assetPath, 1, 1) : tokens[^1].position);
        return tokens[m_offset];
    }

    private BgfxSourceToken Next() { BgfxSourceToken token = Current(); m_offset++; return token; }
    private bool Peek(string value) => m_offset < tokens.Count && tokens[m_offset].text == value;
    private bool Match(string value) { if (!Peek(value)) return false; m_offset++; return true; }
    private void Require(string value) { if (!Match(value)) Fail($"Expected '{value}'.", Current()); }
    private static bool IsQualifier(string value) => value is "const" or "static" or "inline" or "highp" or "mediump" or "lowp";
    private static void Fail(string message, BgfxSourceToken token) => throw new BgfxSourceSyntaxException(message, token.position);
}
