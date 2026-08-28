using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using Inno.Core.Graphs;

namespace Inno.Rendering.ShaderGraph;

/// <summary>
/// Provides stable IDs and definitions for the production shader graph node baseline.
/// </summary>
public static class BuiltinShaderNodes
{
    /// <summary>Scalar constant node ID.</summary>
    public const string Float = "inno.shader.constant.float";
    /// <summary>Two-component constant node ID.</summary>
    public const string Float2 = "inno.shader.constant.float2";
    /// <summary>Three-component constant node ID.</summary>
    public const string Float3 = "inno.shader.constant.float3";
    /// <summary>Four-component constant node ID.</summary>
    public const string Float4 = "inno.shader.constant.float4";
    /// <summary>Linear color constant node ID.</summary>
    public const string Color = "inno.shader.constant.color";
    /// <summary>Scalar addition node ID.</summary>
    public const string AddFloat = "inno.shader.math.add-float";
    /// <summary>Four-component multiplication node ID.</summary>
    public const string MultiplyFloat4 = "inno.shader.math.multiply-float4";
    /// <summary>Material property node ID.</summary>
    public const string MaterialProperty = "inno.shader.input.material-property";
    /// <summary>Two-dimensional texture sampling node ID.</summary>
    public const string SampleTexture2D = "inno.shader.texture.sample-2d";
    /// <summary>Mesh UV0 input node ID.</summary>
    public const string TextureCoordinate0 = "inno.shader.input.texture-coordinate-0";
    /// <summary>World-space interpolated normal node ID.</summary>
    public const string WorldNormal = "inno.shader.input.world-normal";
    /// <summary>World-space interpolated position node ID.</summary>
    public const string WorldPosition = "inno.shader.input.world-position";
    /// <summary>Object-space vertex position node ID.</summary>
    public const string ObjectPosition = "inno.shader.input.object-position";
    /// <summary>General vertex position output node ID.</summary>
    public const string VertexOutput = "inno.shader.output.vertex";
    /// <summary>PBR surface output node ID.</summary>
    public const string SurfaceOutput = "inno.shader.output.surface";
    /// <summary>General fragment output node ID.</summary>
    public const string FragmentOutput = "inno.shader.output.fragment";
    /// <summary>Compute kernel output node ID.</summary>
    public const string ComputeOutput = "inno.shader.output.compute";

    /// <summary>Creates fresh generation-scoped built-in definitions.</summary>
    /// <returns>Built-in definitions suitable for one registry candidate snapshot.</returns>
    public static IReadOnlyList<ShaderNodeDefinition> CreateDefinitions()
        =>
        [
            new ConstantDefinition(Float, "Float", ShaderValueType.Float),
            new ConstantDefinition(Float2, "Vector 2", ShaderValueType.Float2),
            new ConstantDefinition(Float3, "Vector 3", ShaderValueType.Float3),
            new ConstantDefinition(Float4, "Vector 4", ShaderValueType.Float4),
            new ConstantDefinition(Color, "Color", ShaderValueType.Color),
            new BinaryDefinition(AddFloat, "Add", ShaderValueType.Float, "+"),
            new BinaryDefinition(MultiplyFloat4, "Multiply", ShaderValueType.Float4, "*"),
            new MaterialPropertyDefinition(),
            new TextureSampleDefinition(),
            new SemanticInputDefinition(
                TextureCoordinate0,
                "Texture Coordinate 0",
                ShaderValueType.Float2,
                ShaderStage.Vertex | ShaderStage.Fragment,
                "a_texcoord0",
                "v_texcoord0"),
            new SemanticInputDefinition(
                WorldNormal,
                "World Normal",
                ShaderValueType.Float3,
                ShaderStage.Fragment,
                string.Empty,
                "normalize(v_worldNormal)"),
            new SemanticInputDefinition(
                WorldPosition,
                "World Position",
                ShaderValueType.Float3,
                ShaderStage.Fragment,
                string.Empty,
                "v_worldPosition"),
            new SemanticInputDefinition(
                ObjectPosition,
                "Object Position",
                ShaderValueType.Float3,
                ShaderStage.Vertex,
                "a_position",
                string.Empty),
            new VertexOutputDefinition(),
            new SurfaceOutputDefinition(),
            new FragmentOutputDefinition(),
            new ComputeOutputDefinition()
        ];

    private static GraphPortDefinition Port(
        string id,
        string name,
        ShaderValueType type,
        GraphPortDirection direction,
        bool required = false)
        => new(
            new GraphPortId(id),
            name,
            ShaderGraphValueTypes.GetId(type),
            direction,
            required: required);

    private static ShaderValue ConstantValue(
        GraphNodeRecord node,
        ShaderValueType type,
        string propertyId)
    {
        if (!node.TryGetValue(propertyId, out GraphSerializedValue? serialized) || serialized is null)
        {
            return new ShaderValue(type, DefaultExpression(type), node.id);
        }

        using JsonDocument value = JsonDocument.Parse(serialized.json);
        if (type == ShaderValueType.Float)
        {
            return new ShaderValue(
                type,
                value.RootElement.GetSingle().ToString("R", CultureInfo.InvariantCulture),
                node.id);
        }

        float[] components = value.RootElement.EnumerateArray()
            .Select(static element => element.GetSingle())
            .ToArray();
        int expected = type switch
        {
            ShaderValueType.Float2 => 2,
            ShaderValueType.Float3 => 3,
            ShaderValueType.Float4 or ShaderValueType.Color => 4,
            _ => throw new ArgumentOutOfRangeException(nameof(type))
        };
        if (components.Length != expected)
        {
            throw new InvalidOperationException($"A {type} constant requires {expected} numbers.");
        }

        string expression = $"vec{expected}({string.Join(", ", components.Select(Format))})";
        return new ShaderValue(type, expression, node.id);
    }

    private static string Format(float value) => value.ToString("R", CultureInfo.InvariantCulture);

    private static string DefaultExpression(ShaderValueType type)
        => type switch
        {
            ShaderValueType.Float => "0.0",
            ShaderValueType.Float2 => "vec2(0.0)",
            ShaderValueType.Float3 => "vec3(0.0)",
            ShaderValueType.Float4 or ShaderValueType.Color => "vec4(0.0)",
            _ => throw new ArgumentOutOfRangeException(nameof(type))
        };

    private sealed class ConstantDefinition : ShaderNodeDefinition
    {
        private readonly ShaderValueType m_type;

        public ConstantDefinition(string id, string displayName, ShaderValueType type)
            : base(id, displayName, "Input/Constants", ShaderStage.Vertex | ShaderStage.Fragment | ShaderStage.Compute)
        {
            m_type = type;
        }

        public override IReadOnlyList<GraphPortDefinition> GetPorts(GraphNodeRecord node)
            => [Port("value", "Value", m_type, GraphPortDirection.Output)];

        public override void Emit(ShaderNodeEmitContext context)
            => context.SetOutput(
                new GraphPortId("value"),
                ConstantValue(context.node, m_type, "value"));
    }

    private sealed class BinaryDefinition : ShaderNodeDefinition
    {
        private readonly ShaderValueType m_type;
        private readonly string m_operator;

        public BinaryDefinition(string id, string displayName, ShaderValueType type, string binaryOperator)
            : base(id, displayName, "Math", ShaderStage.Vertex | ShaderStage.Fragment | ShaderStage.Compute)
        {
            m_type = type;
            m_operator = binaryOperator;
        }

        public override IReadOnlyList<GraphPortDefinition> GetPorts(GraphNodeRecord node)
            =>
            [
                Port("a", "A", m_type, GraphPortDirection.Input, required: true),
                Port("b", "B", m_type, GraphPortDirection.Input, required: true),
                Port("value", "Value", m_type, GraphPortDirection.Output)
            ];

        public override void Emit(ShaderNodeEmitContext context)
        {
            ShaderValue a = context.GetInput(new GraphPortId("a"));
            ShaderValue b = context.GetInput(new GraphPortId("b"));
            context.SetOutput(
                new GraphPortId("value"),
                new ShaderValue(m_type, $"({a.expression} {m_operator} {b.expression})", context.node.id));
        }
    }

    private sealed class MaterialPropertyDefinition : ShaderNodeDefinition
    {
        public MaterialPropertyDefinition()
            : base(
                MaterialProperty,
                "Material Property",
                "Input/Material",
                ShaderStage.Vertex | ShaderStage.Fragment | ShaderStage.Compute)
        {
        }

        public override IReadOnlyList<GraphPortDefinition> GetPorts(GraphNodeRecord node)
            => [Port("value", "Value", ReadType(node), GraphPortDirection.Output)];

        public override void Emit(ShaderNodeEmitContext context)
        {
            string id = RequireString(context.node, "id");
            if (!IsShaderIdentifier(id) || id.StartsWith("inno_", StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Material property ID '{id}' must be a shader identifier and cannot use the reserved 'inno_' prefix.");
            }

            ShaderValueType type = ReadType(context.node);
            string displayName = ReadOptionalString(context.node, "displayName") ?? id;
            string defaultJson = context.node.TryGetValue("default", out GraphSerializedValue? defaultValue)
                && defaultValue is not null
                    ? defaultValue.json
                    : type switch
                    {
                        ShaderValueType.Float => "0.0",
                        ShaderValueType.Float2 => "[0,0]",
                        ShaderValueType.Float3 => "[0,0,0]",
                        ShaderValueType.Float4 or ShaderValueType.Color => "[0,0,0,0]",
                        ShaderValueType.Matrix4x4 => "[1,0,0,0,0,1,0,0,0,0,1,0,0,0,0,1]",
                        _ => "null"
                    };
            ShaderPropertyType propertyType = type switch
            {
                ShaderValueType.Float => ShaderPropertyType.Float,
                ShaderValueType.Float2 => ShaderPropertyType.Vector2,
                ShaderValueType.Float3 => ShaderPropertyType.Vector3,
                ShaderValueType.Float4 => ShaderPropertyType.Vector4,
                ShaderValueType.Color => ShaderPropertyType.Color,
                ShaderValueType.Matrix4x4 => ShaderPropertyType.Matrix4x4,
                ShaderValueType.Texture2D => ShaderPropertyType.Texture2D,
                ShaderValueType.TextureCube => ShaderPropertyType.TextureCube,
                ShaderValueType.Sampler => ShaderPropertyType.Sampler,
                ShaderValueType.Buffer => ShaderPropertyType.Buffer,
                _ => throw new ArgumentOutOfRangeException(nameof(type))
            };
            context.DeclareProperty(new ShaderPropertyDefinition(
                new ShaderPropertyId(id),
                displayName,
                propertyType,
                context.stage,
                defaultJson));
            string expression = type switch
            {
                ShaderValueType.Float => $"{id}.x",
                ShaderValueType.Float2 => $"{id}.xy",
                ShaderValueType.Float3 => $"{id}.xyz",
                _ => id
            };
            context.SetOutput(new GraphPortId("value"), new ShaderValue(type, expression, context.node.id));
        }

        private static ShaderValueType ReadType(GraphNodeRecord node)
        {
            string value = RequireString(node, "type");
            return Enum.TryParse(value, out ShaderValueType type)
                ? type
                : throw new InvalidOperationException($"Unknown shader property type '{value}'.");
        }
    }

    private sealed class SemanticInputDefinition : ShaderNodeDefinition
    {
        private readonly ShaderValueType m_type;
        private readonly string m_vertexExpression;
        private readonly string m_fragmentExpression;

        public SemanticInputDefinition(
            string id,
            string displayName,
            ShaderValueType type,
            ShaderStage stages,
            string vertexExpression,
            string fragmentExpression)
            : base(id, displayName, "Input/Geometry", stages)
        {
            m_type = type;
            m_vertexExpression = vertexExpression;
            m_fragmentExpression = fragmentExpression;
        }

        public override IReadOnlyList<GraphPortDefinition> GetPorts(GraphNodeRecord node)
            => [Port("value", "Value", m_type, GraphPortDirection.Output)];

        public override void Emit(ShaderNodeEmitContext context)
        {
            string expression = context.stage == ShaderStage.Vertex
                ? m_vertexExpression
                : m_fragmentExpression;
            if (string.IsNullOrWhiteSpace(expression))
            {
                throw new InvalidOperationException($"Input '{displayName}' is unavailable in {context.stage}.");
            }

            context.SetOutput(
                new GraphPortId("value"),
                new ShaderValue(m_type, expression, context.node.id));
        }
    }

    private sealed class VertexOutputDefinition : ShaderNodeDefinition
    {
        public VertexOutputDefinition()
            : base(VertexOutput, "Vertex Output", "Output", ShaderStage.Vertex)
        {
        }

        public override ShaderGraphOutputKind? outputKind => ShaderGraphOutputKind.Vertex;

        public override IReadOnlyList<GraphPortDefinition> GetPorts(GraphNodeRecord node)
            => [Port("position", "Object Position", ShaderValueType.Float3, GraphPortDirection.Input)];

        public override void Emit(ShaderNodeEmitContext context)
            => context.SetSemantic(
                "position",
                context.TryGetInput(new GraphPortId("position"), out ShaderValue position)
                    ? position
                    : new ShaderValue(ShaderValueType.Float3, "a_position", context.node.id));
    }

    private sealed class TextureSampleDefinition : ShaderNodeDefinition
    {
        public TextureSampleDefinition()
            : base(SampleTexture2D, "Sample Texture 2D", "Texture", ShaderStage.Fragment | ShaderStage.Compute)
        {
        }

        public override IReadOnlyList<GraphPortDefinition> GetPorts(GraphNodeRecord node)
            =>
            [
                Port("texture", "Texture", ShaderValueType.Texture2D, GraphPortDirection.Input, required: true),
                Port("uv", "UV", ShaderValueType.Float2, GraphPortDirection.Input, required: true),
                Port("rgba", "RGBA", ShaderValueType.Color, GraphPortDirection.Output)
            ];

        public override void Emit(ShaderNodeEmitContext context)
        {
            ShaderValue texture = context.GetInput(new GraphPortId("texture"));
            ShaderValue uv = context.GetInput(new GraphPortId("uv"));
            context.SetOutput(
                new GraphPortId("rgba"),
                new ShaderValue(
                    ShaderValueType.Color,
                    $"texture2D({texture.expression}, {uv.expression})",
                    context.node.id));
        }
    }

    private sealed class SurfaceOutputDefinition : ShaderNodeDefinition
    {
        public SurfaceOutputDefinition()
            : base(SurfaceOutput, "PBR Surface", "Output", ShaderStage.Fragment)
        {
        }

        public override ShaderGraphOutputKind? outputKind => ShaderGraphOutputKind.Surface;

        public override IReadOnlyList<GraphPortDefinition> GetPorts(GraphNodeRecord node)
            =>
            [
                Port("baseColor", "Base Color", ShaderValueType.Color, GraphPortDirection.Input),
                Port("metallic", "Metallic", ShaderValueType.Float, GraphPortDirection.Input),
                Port("roughness", "Roughness", ShaderValueType.Float, GraphPortDirection.Input),
                Port("emission", "Emission", ShaderValueType.Color, GraphPortDirection.Input),
                Port("occlusion", "Occlusion", ShaderValueType.Float, GraphPortDirection.Input),
                Port("alpha", "Alpha", ShaderValueType.Float, GraphPortDirection.Input)
            ];

        public override void Emit(ShaderNodeEmitContext context)
        {
            SetOrDefault(context, "baseColor", ShaderValueType.Color, "vec4(1.0)");
            SetOrDefault(context, "metallic", ShaderValueType.Float, "0.0");
            SetOrDefault(context, "roughness", ShaderValueType.Float, "0.5");
            SetOrDefault(context, "emission", ShaderValueType.Color, "vec4(0.0)");
            SetOrDefault(context, "occlusion", ShaderValueType.Float, "1.0");
            SetOrDefault(context, "alpha", ShaderValueType.Float, "1.0");
        }
    }

    private sealed class FragmentOutputDefinition : ShaderNodeDefinition
    {
        public FragmentOutputDefinition()
            : base(FragmentOutput, "Fragment Output", "Output", ShaderStage.Fragment)
        {
        }

        public override ShaderGraphOutputKind? outputKind => ShaderGraphOutputKind.Fragment;

        public override IReadOnlyList<GraphPortDefinition> GetPorts(GraphNodeRecord node)
            => [Port("color", "Color", ShaderValueType.Color, GraphPortDirection.Input, required: true)];

        public override void Emit(ShaderNodeEmitContext context)
            => context.SetSemantic("color", context.GetInput(new GraphPortId("color")));
    }

    private sealed class ComputeOutputDefinition : ShaderNodeDefinition
    {
        public ComputeOutputDefinition()
            : base(ComputeOutput, "Compute Output", "Output", ShaderStage.Compute)
        {
        }

        public override ShaderGraphOutputKind? outputKind => ShaderGraphOutputKind.Compute;

        public override IReadOnlyList<GraphPortDefinition> GetPorts(GraphNodeRecord node)
            => [Port("value", "Value", ShaderValueType.Float4, GraphPortDirection.Input, required: true)];

        public override void Emit(ShaderNodeEmitContext context)
        {
            context.DeclareProperty(new ShaderPropertyDefinition(
                new ShaderPropertyId("inno_compute_output"),
                "Compute Output",
                ShaderPropertyType.Buffer,
                ShaderStage.Compute,
                "null"));
            context.SetSemantic("value", context.GetInput(new GraphPortId("value")));
        }
    }

    private static void SetOrDefault(
        ShaderNodeEmitContext context,
        string port,
        ShaderValueType type,
        string expression)
    {
        ShaderValue value = context.TryGetInput(new GraphPortId(port), out ShaderValue connected)
            ? connected
            : new ShaderValue(type, expression, context.node.id);
        context.SetSemantic(port, value);
    }

    private static string RequireString(GraphNodeRecord node, string propertyId)
        => ReadOptionalString(node, propertyId)
            ?? throw new InvalidOperationException($"Node property '{propertyId}' is required.");

    private static string? ReadOptionalString(GraphNodeRecord node, string propertyId)
        => node.TryGetValue(propertyId, out GraphSerializedValue? value) && value is not null
            ? value.Deserialize<string>()
            : null;

    private static bool IsShaderIdentifier(string value)
    {
        if (value.Length == 0 || !(char.IsAsciiLetter(value[0]) || value[0] == '_'))
        {
            return false;
        }

        for (int index = 1; index < value.Length; index++)
        {
            if (!(char.IsAsciiLetterOrDigit(value[index]) || value[index] == '_'))
            {
                return false;
            }
        }

        return true;
    }
}
