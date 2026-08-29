using System;
using System.Collections.Generic;
using System.Linq;
using Inno.Core.Serialization;

namespace Inno.Rendering.Assets;

/// <summary>Persists the shared handwritten and graph-generated Shader IR artifact.</summary>
public static class ShaderIRArtifactSerialization
{
    /// <summary>Encodes one validated shared Shader IR module.</summary>
    /// <param name="module">Module to persist in the common artifact cache.</param>
    /// <returns>Deterministic native artifact bytes.</returns>
    public static byte[] Encode(ShaderIRModule module)
    {
        ArgumentNullException.ThrowIfNull(module);
        return SerializationManager.Serialize(new ModuleData
        {
            definitionData = SerializationManager.Serialize(module.definition),
            passes = module.passes.Select(static pass => new PassData
            {
                name = pass.definition.name,
                generatedVaryingSource = pass.generatedVaryingSource,
                usesAllBindings = pass.usesAllBindings,
                bindingIds = pass.bindingIds.Select(static value => value.value).ToArray(),
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
        });
    }

    /// <summary>Decodes one shared Shader IR artifact.</summary>
    /// <param name="bytes">Complete native artifact bytes.</param>
    /// <returns>The restored backend-neutral module.</returns>
    public static ShaderIRModule Decode(ReadOnlySpan<byte> bytes)
    {
        ModuleData data = SerializationManager.Deserialize<ModuleData>(bytes);
        ShaderDefinition definition = SerializationManager.Deserialize<ShaderDefinition>(data.definitionData);
        Dictionary<string, ShaderPassDefinition> definitions = definition.passes
            .ToDictionary(static value => value.name, StringComparer.Ordinal);
        ShaderIRPass[] passes = data.passes.Select(pass =>
        {
            if (!definitions.TryGetValue(pass.name, out ShaderPassDefinition passDefinition))
                throw new InvalidOperationException($"Shader IR artifact contains undeclared pass '{pass.name}'.");
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
                pass.usesAllBindings
                    ? null
                    : pass.bindingIds.Select(static value => new ShaderPropertyId(value)).ToArray());
        }).ToArray();
        return new ShaderIRModule(definition, passes);
    }

    private sealed class ModuleData : ISerializable
    {
        [SerializableProperty]
        public byte[] definitionData { get; set; } = [];

        [SerializableProperty]
        public PassData[] passes { get; set; } = [];
    }

    private struct PassData
    {
        public string name { get; set; }
        public string? generatedVaryingSource { get; set; }
        public bool usesAllBindings { get; set; }
        public string[] bindingIds { get; set; }
        public StageData[] stages { get; set; }
    }

    private struct StageData
    {
        public ShaderStage stage { get; set; }
        public string entryPoint { get; set; }
        public string source { get; set; }
        public ShaderIRSourceKind sourceKind { get; set; }
        public string assetPath { get; set; }
        public int line { get; set; }
        public int column { get; set; }
        public string nodeId { get; set; }
        public Dictionary<int, string> lineNodeIds { get; set; }
    }
}
