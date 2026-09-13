using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Inno.Core.Diagnostics;
using Inno.Core.Serialization;
using Inno.Extensibility.Types;
using Inno.Rendering.Shaders;

namespace Inno.Rendering.Assets;

public sealed partial class ShaderCompiler
{
    /// <summary>Captures an imported graph in the current owner generation, then starts native compilation without retaining its asset.</summary>
    /// <param name="shader">Committed graph-backed shader asset.</param>
    /// <param name="target">Target policy and device capabilities.</param>
    /// <param name="variant">Exact material keyword selection.</param>
    /// <param name="types">Shared authoring type generation owner.</param>
    /// <param name="serialization">Owner converter registry.</param>
    /// <param name="context">Complete owner asset/reference context.</param>
    /// <param name="artifacts">Authoring artifact owner supplying the graph output, which is absent from runtime packages.</param>
    /// <param name="cancellationToken">Cancellation before candidate publication.</param>
    /// <returns>A native compilation task for the captured graph, or graph diagnostics without any native work.</returns>
    public ValueTask<ShaderCompilationResult> CompileGraphAsync(ShaderAsset shader, ShaderCompileTarget target,
        RenderShaderVariant variant, TypeCatalog types, SerializationRegistry serialization, SerializationContext context, Inno.Assets.IAssetArtifactLookup artifacts,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(shader);
        return CompileGraphAsync(ShaderGraphArtifact.Read(shader, artifacts), target, variant, types, serialization, context, cancellationToken);
    }

    /// <summary>Compiles an immutable import or preview candidate without reading or mutating a canonical Shader asset.</summary>
    /// <param name="artifact">Frozen target-expanded graph and source bundles.</param>
    /// <param name="target">Target policy and capabilities.</param>
    /// <param name="variant">Exact keyword selection.</param>
    /// <param name="types">Current authoring generation.</param>
    /// <param name="serialization">Owner converter registry.</param>
    /// <param name="context">Complete owner references captured before asynchronous native work.</param>
    /// <param name="cancellationToken">Cancellation of this candidate.</param>
    /// <returns>A complete immutable compilation result, never published by this method.</returns>
    public ValueTask<ShaderCompilationResult> CompileGraphAsync(ReadOnlyMemory<byte> artifact, ShaderCompileTarget target,
        RenderShaderVariant variant, TypeCatalog types, SerializationRegistry serialization, SerializationContext context,
        CancellationToken cancellationToken = default)
    {
        using IDisposable operation = types.AcquireOperation("Capture shader graph compilation");
        using var nodes = new ShaderNodeCompilerRegistry(types);
        using var frontends = new ShaderSourceFrontendRegistry(types);
        ShaderDefinition definition = ShaderGraphArtifact.ReadDefinition(artifact.Span, serialization, context);
        var diagnostics = ShaderDefinitionValidator.Validate(definition, target.capabilities).ToList();
        if (diagnostics.Any(static value => value.severity == DiagnosticSeverity.Error)) return ValueTask.FromResult(new ShaderCompilationResult(null, diagnostics));
        var keywords = definition.keywords.ToDictionary(static value => value.id, StringComparer.Ordinal);
        foreach ((string id, string value) in variant.options)
            if (!keywords.TryGetValue(id, out ShaderKeywordDefinition keyword) || !keyword.options.Contains(value, StringComparer.Ordinal))
                diagnostics.Add(new("SHADER_VARIANT_INVALID", DiagnosticSeverity.Error, $"Shader variant selects undeclared option '{id}={value}'."));
        if (diagnostics.Any(static value => value.severity == DiagnosticSeverity.Error)) return ValueTask.FromResult(new ShaderCompilationResult(null, diagnostics));
        ShaderGraphProgramResult program = ShaderGraphArtifact.Lower(artifact.Span, m_toolchain.implementationId,
            nodes, frontends, serialization, context, variant.options);
        return CompileAsync(definition, program, target, variant, serialization, context, cancellationToken);
    }

    /// <summary>Compiles every graph-lowered pass through the adapter's typed stage interface.</summary>
    /// <param name="definition">Source-free material, technique, capability and pass contract.</param>
    /// <param name="program">Complete graph lowering result for this exact variant.</param>
    /// <param name="target">Target compiler policy and capabilities.</param>
    /// <param name="variant">Keyword selection already applied while lowering source functions and graph nodes.</param>
    /// <param name="serialization">Current owner registry used to capture the runtime contract before asynchronous work.</param>
    /// <param name="context">Complete owner reference context; only the encoded stable references cross the asynchronous boundary.</param>
    /// <param name="cancellationToken">Cancellation before candidate publication.</param>
    /// <returns>One complete immutable target artifact, or errors without a partial candidate.</returns>
    public ValueTask<ShaderCompilationResult> CompileAsync(ShaderDefinition definition, ShaderGraphProgramResult program,
        ShaderCompileTarget target, RenderShaderVariant variant, SerializationRegistry serialization,
        SerializationContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(serialization);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(program);
        ArgumentNullException.ThrowIfNull(target);
        var diagnostics = ShaderDefinitionValidator.Validate(definition, target.capabilities).ToList();
        if (diagnostics.Any(static value => value.severity == DiagnosticSeverity.Error))
            return ValueTask.FromResult(new ShaderCompilationResult(null, diagnostics));
        foreach ((string id, string value) in variant.options)
            if (!definition.keywords.Any(keyword => keyword.id == id && keyword.options.Contains(value, StringComparer.Ordinal)))
                diagnostics.Add(new("SHADER_VARIANT_INVALID", DiagnosticSeverity.Error, $"Shader variant selects undeclared option '{id}={value}'."));
        if (diagnostics.Count != 0 && diagnostics.Any(static value => value.severity == DiagnosticSeverity.Error))
            return ValueTask.FromResult(new ShaderCompilationResult(null, diagnostics));
        byte[] definitionData = serialization.Serialize(definition, context);
        var captured = new ShaderDefinition(definition.name,
            definition.properties.Select(static value => new ShaderPropertyDefinition(value.id, value.displayName,
                value.type, value.stages, default, value.bindingKind, value.storageAccess, value.bindingOwner)),
            definition.keywords.Select(static value => new ShaderKeywordDefinition(value.id, value.options.ToArray())),
            definition.passes.Select(static value => new ShaderPassDefinition(value.name, value.programKind,
                value.requiredFeatures, value.renderState, value.metadata)),
            definition.techniques.Select(static value => new ShaderTechniqueDefinition(value.id, value.contract,
                value.passes.ToArray(), value.requiredFeatures)));
        return CompileCapturedAsync(captured, definitionData, program, target, variant, cancellationToken);
    }

    private async ValueTask<ShaderCompilationResult> CompileCapturedAsync(ShaderDefinition definition, byte[] definitionData,
        ShaderGraphProgramResult program, ShaderCompileTarget target, RenderShaderVariant variant, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(program);
        ArgumentNullException.ThrowIfNull(target);
        var diagnostics = program.diagnostics.Select(static value => new ShaderDiagnostic(value.code, value.severity, value.message)).ToList();
        diagnostics.AddRange(ShaderDefinitionValidator.Validate(definition, target.capabilities));
        if (diagnostics.Any(static value => value.severity == DiagnosticSeverity.Error)) return new(null, diagnostics);
        if (!program.succeeded) return new(null, diagnostics);
        string shaderName = definition.name;
        // Only value contracts cross the asynchronous native compiler boundary. Texture defaults remain owned by the imported asset.
        ShaderPropertyDefinition[] properties = definition.properties.Select(static value => new ShaderPropertyDefinition(value.id,
            value.displayName, value.type, value.stages, default, value.bindingKind, value.storageAccess, value.bindingOwner)).ToArray();
        var passes = definition.passes.Select(static value => new ShaderPassDefinition(value.name, value.programKind,
            requiredFeatures: value.requiredFeatures, renderState: value.renderState, metadata: value.metadata)).ToDictionary(static value => value.name, StringComparer.Ordinal);
        var compiledPasses = new List<CompiledShaderPass>();
        var allBindings = new Dictionary<string, ShaderInterfaceBinding>(StringComparer.Ordinal);
        foreach (ShaderGraphPass pass in program.passes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!passes.TryGetValue(pass.name, out ShaderPassDefinition passDefinition))
                throw new InvalidOperationException($"The graph produced undeclared pass '{pass.name}'.");
            if ((passDefinition.requiredFeatures & ~target.capabilities.features) != 0) continue;
            var binaries = new List<ShaderStageArtifact>();
            var bindings = new Dictionary<string, ShaderInterfaceBinding>(StringComparer.Ordinal);
            foreach (ShaderIrStage stage in pass.stages)
            {
                ShaderStageToolResult result = await CompileAsync(stage, target, cancellationToken).ConfigureAwait(false);
                diagnostics.AddRange(result.diagnostics.Select(value => new ShaderDiagnostic(value.code, value.severity, value.message,
                    new ShaderSourceLocation(value.location.assetPath, pass.name, stage.stage, value.location.line, value.location.column))));
                if (!result.succeeded) return new(null, diagnostics);
                binaries.Add(new(stage.stage, result.bytes.Span, new ShaderSourceLocation(shaderName, pass.name, stage.stage)));
                foreach (ShaderStageBinding generated in result.bindings)
                {
                    ShaderPropertyDefinition[] matches = properties.Where(value => value.id.value == generated.id).ToArray();
                    if (matches.Length != 1)
                    {
                        diagnostics.Add(new("SHADER_GRAPH_BINDING", DiagnosticSeverity.Error,
                            $"Generated binding '{generated.id}' requires exactly one declared shader property."));
                        continue;
                    }
                    ShaderPropertyDefinition property = matches[0];
                    ShaderIrStageInput input = stage.inputs.Single(value => value.id == generated.id);
                    if ((property.stages & stage.stage) == 0 || !Matches(property, input))
                    {
                        diagnostics.Add(new("SHADER_GRAPH_BINDING_TYPE", DiagnosticSeverity.Error,
                            $"Property '{generated.id}' does not match the {stage.stage} graph binding's type, access or visibility."));
                        continue;
                    }
                    var binding = new ShaderInterfaceBinding(property.id, property.type, stage.stage,
                        input.type.elementType is null ? 1 : input.type.elementCount,
                        property.bindingKind, property.storageAccess, generated.nativeName,
                        property.bindingKind == ShaderPropertyBindingKind.Uniform ? null : generated.location);
                    Merge(bindings, binding);
                    Merge(allBindings, binding);
                }
            }
            compiledPasses.Add(new(passDefinition, binaries, new(bindings.Values.ToArray())));
        }
        if (compiledPasses.Count == 0) diagnostics.Add(new("SHADER_NO_SUPPORTED_PASS", DiagnosticSeverity.Error,
            $"Shader '{shaderName}' has no pass supported by '{target.key}'."));
        if (diagnostics.Any(static value => value.severity == DiagnosticSeverity.Error)) return new(null, diagnostics);
        return new(new(shaderName, target.key, variant, new(allBindings.Values.ToArray()), compiledPasses, definitionData), diagnostics);
    }

    private static bool Matches(ShaderPropertyDefinition property, ShaderIrStageInput input)
    {
        if (property.bindingKind == ShaderPropertyBindingKind.StorageBuffer)
            return input.kind == ShaderIrInputKind.Storage && input.type.storage is { isImage: false } buffer && buffer.access == property.storageAccess;
        if (property.bindingKind == ShaderPropertyBindingKind.StorageTexture)
            return input.kind == ShaderIrInputKind.Storage && input.type.storage is { isImage: true } image && image.access == property.storageAccess;
        if ((property.bindingKind == ShaderPropertyBindingKind.SampledTexture) != (input.kind == ShaderIrInputKind.SampledTexture)) return false;
        string type = (input.type.elementType ?? input.type).id;
        return property.type switch
        {
            ShaderPropertyType.Float => type == "float",
            ShaderPropertyType.Vector2 => type == "float2",
            ShaderPropertyType.Vector3 => type == "float3",
            ShaderPropertyType.Vector4 or ShaderPropertyType.Color => type == "float4",
            ShaderPropertyType.Matrix4x4 => type == "float4x4",
            ShaderPropertyType.Texture2D => type == "sampled-texture2d",
            ShaderPropertyType.Texture2DArray => type == "sampled-texture2d-array",
            ShaderPropertyType.Texture3D => type == "sampled-texture3d",
            ShaderPropertyType.TextureCube => type == "sampled-texture-cube",
            _ => false
        };
    }

    private static void Merge(Dictionary<string, ShaderInterfaceBinding> target, ShaderInterfaceBinding binding)
    {
        if (target.TryGetValue(binding.id.value, out ShaderInterfaceBinding? previous))
        {
            if (previous.type != binding.type || previous.arrayCount != binding.arrayCount || previous.bindingKind != binding.bindingKind
                || previous.storageAccess != binding.storageAccess || previous.nativeName != binding.nativeName || previous.location != binding.location)
                throw new InvalidOperationException($"Shader binding '{binding.id}' has inconsistent native layouts between stages or passes.");
            binding = new(binding.id, binding.type, previous.stages | binding.stages, binding.arrayCount, binding.bindingKind,
                binding.storageAccess, binding.nativeName, binding.location);
        }
        target[binding.id.value] = binding;
    }
}
