using System;
using System.Collections.Generic;
using System.Linq;
using Inno.Core.Serialization;

namespace Inno.Rendering.Assets;

/// <summary>
/// Persists the shared handwritten and graph-generated Shader IR artifact.
/// </summary>
public static class ShaderIRArtifactSerialization
{
    /// <summary>
    /// Encodes one validated shared Shader IR module.
    /// </summary>
    /// <param name="module">
    /// Module to persist in the common artifact cache.
    /// </param>
    /// <param name="serialization">
    /// The serialization registry that owns the active Shader contract generation.
    /// </param>
    /// <returns>
    /// Deterministic native artifact bytes.
    /// </returns>
    public static byte[] Encode(ShaderIRModule module, SerializationRegistry serialization)
    {
        ArgumentNullException.ThrowIfNull(module);
        ArgumentNullException.ThrowIfNull(serialization);
        return serialization.Serialize(new ModuleData
        {
            definitionData = serialization.Serialize(CreateArtifactDefinition(module.definition)),
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

    /// <summary>
    /// Decodes one shared Shader IR artifact.
    /// </summary>
    /// <param name="bytes">
    /// Complete native artifact bytes.
    /// </param>
    /// <param name="serialization">
    /// The serialization registry that owns the active Shader contract generation.
    /// </param>
    /// <returns>
    /// The restored backend-neutral module.
    /// </returns>
    public static ShaderIRModule Decode(
        ReadOnlySpan<byte> bytes,
        SerializationRegistry serialization)
    {
        ArgumentNullException.ThrowIfNull(serialization);
        ModuleData data = serialization.Deserialize<ModuleData>(bytes);
        ShaderDefinition definition = serialization.Deserialize<ShaderDefinition>(data.definitionData);
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

    private static ShaderDefinition CreateArtifactDefinition(ShaderDefinition definition)
        => new(
            definition.name,
            definition.properties,
            definition.keywords,
            definition.passes.Select(static pass => new ShaderPassDefinition(
                pass.name,
                pass.programKind,
                requiredFeatures: pass.requiredFeatures,
                renderState: pass.renderState,
                metadata: pass.metadata)),
            definition.techniques);

    private sealed class ModuleData : ISerializable
    {
        /// <summary>
        /// Gets the serialized shader definition bytes stored in the artifact.
        /// </summary>
        [SerializableProperty]
        public byte[] definitionData { get; set; } = [];

        /// <summary>
        /// Gets the ordered compiled shader passes stored in the artifact.
        /// </summary>
        [SerializableProperty]
        public PassData[] passes { get; set; } = [];
    }

    private struct PassData
    {
        /// <summary>
        /// Gets the human-readable name used for presentation and diagnostics.
        /// </summary>
        public string name { get; set; }
        /// <summary>
        /// Gets generated varying declarations when the source required synthesis.
        /// </summary>
        public string? generatedVaryingSource { get; set; }
        /// <summary>
        /// Gets whether the caller-visible condition represented by this property is satisfied.
        /// </summary>
        public bool usesAllBindings { get; set; }
        /// <summary>
        /// Gets the stable shader binding identities used by the compiled stage.
        /// </summary>
        public string[] bindingIds { get; set; }
        /// <summary>
        /// Gets the compiled shader stages that form this pass.
        /// </summary>
        public StageData[] stages { get; set; }
    }

    private struct StageData
    {
        /// <summary>
        /// Gets the shader stage represented by this compiled payload.
        /// </summary>
        public ShaderStage stage { get; set; }
        /// <summary>
        /// Gets text used for stable identity, presentation, or diagnostics by this contract.
        /// </summary>
        public string entryPoint { get; set; }
        /// <summary>
        /// Gets text used for stable identity, presentation, or diagnostics by this contract.
        /// </summary>
        public string source { get; set; }
        /// <summary>
        /// Gets whether the Shader IR originated from handwritten or generated source.
        /// </summary>
        public ShaderIRSourceKind sourceKind { get; set; }
        /// <summary>
        /// Gets the normalized asset path used by the current operation.
        /// </summary>
        public string assetPath { get; set; }
        /// <summary>
        /// Gets the scalar measurement or identity associated with the current state.
        /// </summary>
        public int line { get; set; }
        /// <summary>
        /// Gets the scalar measurement or identity associated with the current state.
        /// </summary>
        public int column { get; set; }
        /// <summary>
        /// Gets text used for stable identity, presentation, or diagnostics by this contract.
        /// </summary>
        public string nodeId { get; set; }
        /// <summary>
        /// Gets the mapping from generated source lines to stable Shader Graph node identities.
        /// </summary>
        public Dictionary<int, string> lineNodeIds { get; set; }
    }
}
