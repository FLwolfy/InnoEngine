using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Inno.Rendering.Core;

namespace Inno.Rendering;

internal static class ShaderDefinitionCodec
{
    private static readonly JsonSerializerOptions S_OPTIONS = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    internal static string Encode(ShaderDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return JsonSerializer.Serialize(ToData(definition), S_OPTIONS);
    }

    internal static ShaderDefinition Decode(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        DefinitionData data = JsonSerializer.Deserialize<DefinitionData>(json, S_OPTIONS)
            ?? throw new InvalidOperationException("Shader definition state is empty.");
        return new ShaderDefinition(
            data.name,
            data.properties.Select(static value => new ShaderPropertyDefinition(
                new ShaderPropertyId(value.id),
                value.displayName,
                value.type,
                value.stages,
                value.defaultValueJson)).ToArray(),
            data.keywords.Select(static value => new ShaderKeywordDefinition(value.id, value.options)).ToArray(),
            data.passes.Select(static value => new ShaderPassDefinition(
                value.name,
                value.tag,
                value.vertexSource,
                value.fragmentSource,
                value.computeSource,
                value.varyingSource,
                value.requiredFeatures,
                new ShaderRenderState
                {
                    cull = value.renderState.cull,
                    depthCompare = value.renderState.depthCompare,
                    depthWrite = value.renderState.depthWrite,
                    blend = value.renderState.blend,
                    colorWriteMask = value.renderState.colorWriteMask
                },
                value.tags)).ToArray());
    }

    private static DefinitionData ToData(ShaderDefinition definition)
        => new()
        {
            name = definition.name,
            properties = definition.properties.Select(static value => new PropertyData
            {
                id = value.id.value,
                displayName = value.displayName,
                type = value.type,
                stages = value.stages,
                defaultValueJson = value.defaultValueJson
            }).ToArray(),
            keywords = definition.keywords.Select(static value => new KeywordData
            {
                id = value.id,
                options = value.options.ToArray()
            }).ToArray(),
            passes = definition.passes.Select(static value => new PassData
            {
                name = value.name,
                tag = value.tag,
                vertexSource = value.vertexSource,
                fragmentSource = value.fragmentSource,
                computeSource = value.computeSource,
                varyingSource = value.varyingSource,
                requiredFeatures = value.requiredFeatures,
                renderState = new RenderStateData
                {
                    cull = value.renderState.cull,
                    depthCompare = value.renderState.depthCompare,
                    depthWrite = value.renderState.depthWrite,
                    blend = value.renderState.blend,
                    colorWriteMask = value.renderState.colorWriteMask
                },
                tags = new Dictionary<string, string>(value.tags, StringComparer.Ordinal)
            }).ToArray()
        };

    private sealed class DefinitionData
    {
        public string name { get; set; } = string.Empty;
        public PropertyData[] properties { get; set; } = [];
        public KeywordData[] keywords { get; set; } = [];
        public PassData[] passes { get; set; } = [];
    }

    private sealed class PropertyData
    {
        public string id { get; set; } = string.Empty;
        public string displayName { get; set; } = string.Empty;
        public ShaderPropertyType type { get; set; }
        public ShaderStage stages { get; set; }
        public string defaultValueJson { get; set; } = "null";
    }

    private sealed class KeywordData
    {
        public string id { get; set; } = string.Empty;
        public string[] options { get; set; } = [];
    }

    private sealed class PassData
    {
        public string name { get; set; } = string.Empty;
        public string tag { get; set; } = string.Empty;
        public string? vertexSource { get; set; }
        public string? fragmentSource { get; set; }
        public string? computeSource { get; set; }
        public string? varyingSource { get; set; }
        public GraphicsFeature requiredFeatures { get; set; }
        public RenderStateData renderState { get; set; } = new();
        public Dictionary<string, string> tags { get; set; } = new(StringComparer.Ordinal);
    }

    private sealed class RenderStateData
    {
        public ShaderCullMode cull { get; set; }
        public ShaderCompareFunction depthCompare { get; set; }
        public bool depthWrite { get; set; }
        public ShaderBlendMode blend { get; set; }
        public byte colorWriteMask { get; set; }
    }
}
