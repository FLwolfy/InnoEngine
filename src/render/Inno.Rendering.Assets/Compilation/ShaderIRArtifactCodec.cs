using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Inno.Rendering.Assets;

internal static class ShaderIRArtifactCodec
{
    private static readonly JsonSerializerOptions S_OPTIONS = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    internal static byte[] Encode(ShaderIRModule module)
    {
        ArgumentNullException.ThrowIfNull(module);
        var data = new ModuleData
        {
            definitionJson = ShaderDefinitionCodec.Encode(module.definition),
            passes = module.passes.Select(static pass => new PassData
            {
                name = pass.definition.name,
                generatedVaryingSource = pass.generatedVaryingSource,
                bindingIds = pass.usesAllBindings
                    ? null
                    : pass.bindingIds.Select(static value => value.value).ToArray(),
                stages = pass.stages.Select(static stage => new StageData
                {
                    stage = stage.stage,
                    entryPoint = stage.entryPoint,
                    source = stage.source,
                    sourceKind = stage.sourceKind,
                    assetPath = stage.location.assetPath,
                    line = stage.location.line,
                    column = stage.location.column,
                    nodeId = stage.location.nodeId,
                    lineNodeIds = new Dictionary<int, string>(stage.lineNodeIds)
                }).ToArray()
            }).ToArray()
        };
        return JsonSerializer.SerializeToUtf8Bytes(data, S_OPTIONS);
    }

    internal static ShaderIRModule Decode(ReadOnlySpan<byte> bytes)
    {
        ModuleData data = JsonSerializer.Deserialize<ModuleData>(bytes, S_OPTIONS)
            ?? throw new InvalidOperationException("Shader IR artifact is empty.");
        ShaderDefinition definition = ShaderDefinitionCodec.Decode(data.definitionJson);
        Dictionary<string, ShaderPassDefinition> definitions = definition.passes
            .ToDictionary(static value => value.name, StringComparer.Ordinal);
        ShaderIRPass[] passes = data.passes.Select(pass =>
        {
            if (!definitions.TryGetValue(pass.name, out ShaderPassDefinition? passDefinition))
            {
                throw new InvalidOperationException(
                    $"Shader IR artifact contains undeclared pass '{pass.name}'.");
            }

            ShaderIRStageModule[] stages = pass.stages.Select(stage => new ShaderIRStageModule(
                stage.stage,
                stage.entryPoint,
                stage.source,
                stage.sourceKind,
                new ShaderSourceLocation(
                    stage.assetPath,
                    pass.name,
                    stage.stage,
                    stage.line,
                    stage.column,
                    stage.nodeId),
                stage.lineNodeIds)).ToArray();
            return new ShaderIRPass(
                passDefinition,
                stages,
                pass.generatedVaryingSource,
                pass.bindingIds?.Select(static value => new ShaderPropertyId(value)).ToArray());
        }).ToArray();
        return new ShaderIRModule(definition, passes);
    }

    private sealed class ModuleData
    {
        public string definitionJson { get; set; } = string.Empty;
        public PassData[] passes { get; set; } = [];
    }

    private sealed class PassData
    {
        public string name { get; set; } = string.Empty;
        public string? generatedVaryingSource { get; set; }
        public string[]? bindingIds { get; set; }
        public StageData[] stages { get; set; } = [];
    }

    private sealed class StageData
    {
        public ShaderStage stage { get; set; }
        public string entryPoint { get; set; } = string.Empty;
        public string source { get; set; } = string.Empty;
        public ShaderIRSourceKind sourceKind { get; set; }
        public string assetPath { get; set; } = string.Empty;
        public int line { get; set; }
        public int column { get; set; }
        public string nodeId { get; set; } = string.Empty;
        public Dictionary<int, string> lineNodeIds { get; set; } = [];
    }
}
