using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Inno.Core.Diagnostics;
using Inno.Core.Execution;
using Inno.Core.Mathematics;
using Inno.Rendering;

namespace Inno.Rendering.Runtime;

internal sealed class RenderMaterialOwner : RenderResourceProvider, IDisposable
{
    internal delegate bool TextureResolver(TextureAsset asset, out PersistentTextureHandle texture);

    private readonly IRenderDevice m_device;
    private readonly IDiagnosticReporter m_diagnostics;
    private readonly IRenderTargetArtifactProvider? m_targetArtifacts;
    private readonly TextureResolver m_textureResolver;
    private readonly RenderResourceCache<ProgramKey, ProgramEntry> m_programs;
    private ulong m_frameIndex;
    private bool m_stopping;

    internal RenderMaterialOwner(IRenderDevice device, IDiagnosticReporter diagnostics,
        IRenderTargetArtifactProvider? targetArtifacts, TextureResolver textureResolver, int capacity)
    {
        m_device = device;
        m_diagnostics = diagnostics;
        m_targetArtifacts = targetArtifacts;
        m_textureResolver = textureResolver;
        m_programs = new(DestroyProgram, capacity);
    }

    internal int count => m_programs.Count;
    internal int retiringCount => m_programs.pendingCount;
    internal long rejectedCount => m_programs.rejectedCount;

    internal void BeginFrame(ulong frameIndex) { Drain(); m_frameIndex = frameIndex; }
    internal void Sweep(ulong oldest) => m_programs.Sweep(entry => entry.lastUsedFrame < oldest);
    internal void Drain() { ObjectDisposedException.ThrowIf(m_stopping, this); m_programs.Drain(); }

    /// <summary>
    /// Releases the resources owned by this implementation.
    /// </summary>
    public void Dispose() { m_stopping = true; m_programs.Dispose(); }

    internal bool TryResolveMaterial(
        MaterialAsset material,
        ShaderContractId contractId,
        ShaderPassRoleId passRoleId,
        ShaderProgramKind expectedKind,
        RenderVertexLayout? vertexLayout,
        MaterialPropertyBlock? overrides,
        out RenderMaterialPass? materialPass)
    {
        Drain();
        ArgumentNullException.ThrowIfNull(material);
        materialPass = null;
        MaterialPassResolution? resolution = MaterialPassResolver.Resolve(
            material,
            contractId,
            passRoleId,
            m_device.capabilities);
        if (resolution is null)
        {
            Publish(
                "RENDER_MATERIAL_PASS_UNAVAILABLE",
                $"Material '{material.assetPath.ToString()}' does not implement contract '{contractId}' role '{passRoleId}'.",
                material.assetPath.ToString());
            return false;
        }
        if (resolution.pass.programKind != expectedKind)
        {
            Publish(
                "RENDER_MATERIAL_PASS_KIND_MISMATCH",
                $"Material pass '{resolution.pass.name}' is {resolution.pass.programKind}, not {expectedKind}.",
                material.assetPath.ToString());
            return false;
        }

        ShaderAsset shader = material.shader!;
        RenderShaderVariant variant;
        try
        {
            variant = RenderShaderVariant.FromMaterial(material);
        }
        catch (Exception pending) when (RetirementPendingException.Find(pending) is not null) { throw; }
        catch (Exception exception)
        {
            Publish("RENDER_MATERIAL_VARIANT_INVALID", exception.Message, material.assetPath.ToString());
            return false;
        }

        if (!TryResolveProgram(shader, resolution.pass, variant, vertexLayout, out ProgramEntry? program))
            return false;
        ProgramEntry activeProgram = program!;
        if (!TryBuildBindings(material, overrides, activeProgram.shaderInterface, out MaterialBinding[] bindings))
            return false;
        materialPass = CreateMaterialPass(
            resolution.pass,
            activeProgram.graphicsPipeline,
            activeProgram.computePipeline,
            bindings);
        return true;
    }

    private bool TryResolveProgram(
        ShaderAsset shader,
        ShaderPassDefinition pass,
        RenderShaderVariant variant,
        RenderVertexLayout? vertexLayout,
        out ProgramEntry? program)
    {
        program = null;
        Guid shaderId = shader.identity.persistentId;
        if (shaderId == Guid.Empty)
        {
            Publish("RENDER_SHADER_ID_MISSING", "Shader must have a persistent asset identity.", shader.assetPath.ToString());
            return false;
        }

        string sourceId = shader.assetPath.ToString();
        RenderTargetArtifactStatus status;
        RenderShaderArtifact? artifact;
        try
        {
            artifact = null;
            status = m_targetArtifacts is null
                ? RenderTargetArtifactStatus.Unavailable
                : m_targetArtifacts.GetShaderArtifact(
                    shader,
                    variant,
                    m_device.capabilities,
                    out artifact);
        }
        catch (Exception pending) when (RetirementPendingException.Find(pending) is not null) { throw; }
        catch (Exception exception)
        {
            Publish("RENDER_SHADER_TARGET_INVALID", exception.Message, sourceId);
            return TryGetLastGoodProgram(
                shaderId,
                variant,
                pass,
                vertexLayout,
                out program);
        }

        if (status != RenderTargetArtifactStatus.Unavailable)
            m_diagnostics.Resolve("RENDER_SHADER_TARGET_UNAVAILABLE", sourceId);
        if (status != RenderTargetArtifactStatus.Ready)
        {
            if (status == RenderTargetArtifactStatus.Unavailable)
            {
                Publish(
                    "RENDER_SHADER_TARGET_UNAVAILABLE",
                    m_targetArtifacts is null
                        ? "No render target artifact provider is configured for this runtime."
                        : $"No deployed target shader artifact exists for '{shader.assetPath}' " +
                          $"variant '{variant}' and backend '{m_device.capabilities.backend}'.",
                    sourceId);
            }
            return TryGetLastGoodProgram(
                shaderId,
                variant,
                pass,
                vertexLayout,
                out program);
        }
        if (artifact is null)
        {
            Publish(
                "RENDER_SHADER_TARGET_INVALID",
                $"Target artifact provider returned a null ready shader for '{shader.assetPath}'.",
                sourceId);
            return TryGetLastGoodProgram(
                shaderId,
                variant,
                pass,
                vertexLayout,
                out program);
        }
        m_diagnostics.Resolve("RENDER_SHADER_TARGET_INVALID", sourceId);

        var key = new ProgramKey(
            shaderId,
            artifact.targetKey,
            variant.value,
            pass.name,
            pass.programKind,
            vertexLayout);
        m_programs.TryGetValue(key, out ProgramEntry? current);
        if (current is not null
            && current.shaderContentVersion == shader.contentVersion)
        {
            current.lastUsedFrame = m_frameIndex;
            program = current;
            return true;
        }

        RenderShaderPassArtifact? compiledPass = artifact.passes.FirstOrDefault(candidate =>
            string.Equals(candidate.name, pass.name, StringComparison.Ordinal));
        if (compiledPass is null)
        {
            Publish(
                "RENDER_SHADER_PASS_ARTIFACT_MISSING",
                $"Compiled shader '{shader.assetPath.ToString()}' has no pass '{pass.name}'.",
                shader.assetPath.ToString());
            if (current is not null)
            {
                current.lastUsedFrame = m_frameIndex;
                program = current;
                return true;
            }
            return false;
        }
        m_diagnostics.Resolve("RENDER_SHADER_PASS_ARTIFACT_MISSING", sourceId);

        ProgramEntry candidate;
        m_programs.RequireCapacity(key);
        try
        {
            candidate = CreateProgram(
                shader,
                compiledPass,
                vertexLayout,
                shader.contentVersion);
            candidate.lastUsedFrame = m_frameIndex;

        }
        catch (Exception pending) when (RetirementPendingException.Find(pending) is not null) { throw; }
        catch (Exception exception)
        {
            Publish(
                "RENDER_PROGRAM_CREATE_FAILED",
                $"Shader '{shader.assetPath.ToString()}' pass '{pass.name}' kept its last-good program: {exception.Message}",
                shader.assetPath.ToString());
            if (current is not null)
            {
                current.lastUsedFrame = m_frameIndex;
                program = current;
                return true;
            }
            return false;
        }
        m_programs.Replace(key, candidate);
        m_programs.Drain();
        program = candidate;
        m_diagnostics.Resolve("RENDER_PROGRAM_CREATE_FAILED", sourceId);
        return true;
    }

    private bool TryGetLastGoodProgram(
        Guid shaderId,
        RenderShaderVariant variant,
        ShaderPassDefinition pass,
        RenderVertexLayout? vertexLayout,
        out ProgramEntry? program)
    {
        foreach ((ProgramKey key, ProgramEntry candidate) in m_programs)
        {
            if (key.shaderId != shaderId ||
                !string.Equals(key.variantKey, variant.value, StringComparison.Ordinal) ||
                !string.Equals(key.passName, pass.name, StringComparison.Ordinal) ||
                key.kind != pass.programKind ||
                !Equals(key.vertexLayout, vertexLayout))
            {
                continue;
            }
            candidate.lastUsedFrame = m_frameIndex;
            program = candidate;
            return true;
        }
        program = null;
        return false;
    }

    private ProgramEntry CreateProgram(
        ShaderAsset shader,
        RenderShaderPassArtifact pass,
        RenderVertexLayout? vertexLayout,
        long shaderContentVersion)
    {
        IReadOnlyList<RenderShaderBindingDescriptor> bindings = BuildBindingDescriptors(pass.shaderInterface);
        if (pass.programKind == ShaderProgramKind.Raster)
        {
            ReadOnlyMemory<byte> vertex = pass.stages.Single(stage => stage.stage == ShaderStage.Vertex).bytes;
            ReadOnlyMemory<byte> fragment = pass.stages.Single(stage => stage.stage == ShaderStage.Fragment).bytes;
            GraphicsPipelineHandle pipeline = m_device.CreateGraphicsPipeline(
                new GraphicsPipelineDescriptor(
                    vertex.Span,
                    fragment.Span,
                    bindings,
                    vertexLayout,
                    pass.rasterState),
                $"{shader.name}/{pass.name}");
            return new ProgramEntry(
                pipeline,
                default,
                pass.shaderInterface,
                shaderContentVersion,
                m_frameIndex);
        }

        ReadOnlyMemory<byte> compute = pass.stages.Single(stage => stage.stage == ShaderStage.Compute).bytes;
        ComputePipelineHandle computePipeline = m_device.CreateComputePipeline(
            new ComputePipelineDescriptor(compute.Span, bindings),
            $"{shader.name}/{pass.name}");
        return new ProgramEntry(
            default,
            computePipeline,
            pass.shaderInterface,
            shaderContentVersion,
            m_frameIndex);
    }

    private bool TryBuildBindings(
        MaterialAsset material,
        MaterialPropertyBlock? overrides,
        ShaderInterface shaderInterface,
        out MaterialBinding[] bindings)
    {
        var result = new List<MaterialBinding>();
        ShaderDefinition definition = material.shader!.definition!;
        Dictionary<ShaderPropertyId, ShaderPropertyDefinition> definitions = definition.properties
            .ToDictionary(static property => property.id);
        foreach (ShaderInterfaceBinding binding in shaderInterface.bindings)
        {
            if (!definitions.TryGetValue(binding.id, out ShaderPropertyDefinition property))
                continue;
            if (property.bindingOwner == ShaderPropertyBindingOwner.RenderPass)
                continue;
            MaterialValue value = overrides is not null && overrides.TryGet(binding.id, out MaterialValue overridden)
                ? overridden
                : material.TryGet(binding.id, out MaterialValue materialValue)
                    ? materialValue
                    : property.defaultValue;
            if (binding.bindingKind == ShaderPropertyBindingKind.SampledTexture)
            {
                if (value.kind != MaterialValueKind.Texture || value.texture is null)
                    continue;
                if (!m_textureResolver(value.texture, out PersistentTextureHandle texture))
                {
                    bindings = [];
                    return false;
                }
                result.Add(CreateTextureBinding(
                    new RenderBindingId(binding.id.value),
                    texture,
                    value.sampler));
                continue;
            }
            if (binding.bindingKind is ShaderPropertyBindingKind.StorageBuffer
                or ShaderPropertyBindingKind.StorageTexture)
                continue;
            if (!TryEncodeUniform(binding.type, value, out byte[]? bytes))
            {
                Publish(
                    "RENDER_MATERIAL_PROPERTY_TYPE_MISMATCH",
                    $"Material '{material.assetPath.ToString()}' property '{binding.id}' does not match {binding.type}.",
                    material.assetPath.ToString());
                bindings = [];
                return false;
            }
            result.Add(CreateUniformBinding(
                new RenderBindingId(binding.id.value),
                bytes));
        }
        bindings = result.ToArray();
        return true;
    }

    private static bool TryEncodeUniform(ShaderPropertyType type, MaterialValue value, out byte[] bytes)
    {
        if (type == ShaderPropertyType.Matrix4x4)
        {
            if (value.kind != MaterialValueKind.Matrix)
            {
                bytes = [];
                return false;
            }
            Matrix matrix = value.matrix;
            bytes = MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref matrix, 1)).ToArray();
            return true;
        }
        if (value.kind is not (MaterialValueKind.Float or MaterialValueKind.Vector or MaterialValueKind.Color))
        {
            bytes = [];
            return false;
        }
        Vector4 vector = value.vector;
        bytes = MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref vector, 1)).ToArray();
        return true;
    }

    private static IReadOnlyList<RenderShaderBindingDescriptor> BuildBindingDescriptors(
        ShaderInterface shaderInterface)
    {
        var result = new List<RenderShaderBindingDescriptor>(shaderInterface.bindings.Count);
        int textureSlot = 0;
        int bufferSlot = 0;
        foreach (ShaderInterfaceBinding binding in shaderInterface.bindings)
        {
            RenderBindingId id = new(binding.id.value);
            switch (binding.bindingKind)
            {
                case ShaderPropertyBindingKind.Uniform:
                    result.Add(new RenderShaderBindingDescriptor(
                        id,
                        RenderShaderBindingKind.Uniform,
                        uniformType: binding.type == ShaderPropertyType.Matrix4x4
                            ? RenderUniformType.Matrix4x4
                            : RenderUniformType.Vector4,
                        count: binding.arrayCount));
                    break;
                case ShaderPropertyBindingKind.SampledTexture:
                    result.Add(new RenderShaderBindingDescriptor(
                        id,
                        RenderShaderBindingKind.Texture,
                        slot: textureSlot++));
                    break;
                case ShaderPropertyBindingKind.StorageTexture:
                    result.Add(new RenderShaderBindingDescriptor(
                        id,
                        RenderShaderBindingKind.StorageTexture,
                        slot: bufferSlot++,
                        storageAccess: binding.storageAccess));
                    break;
                case ShaderPropertyBindingKind.StorageBuffer:
                    result.Add(new RenderShaderBindingDescriptor(
                        id,
                        RenderShaderBindingKind.StorageBuffer,
                        slot: bufferSlot++,
                        storageAccess: binding.storageAccess));
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(binding));
            }
        }
        return result;
    }

    private void Publish(string code, string message, string? source)
        => m_diagnostics.Publish(new Diagnostic(
            code,
            message,
            DiagnosticSeverity.Error,
            source));

    private void DestroyProgram(ProgramEntry program)
    {
        if (program.graphicsPipeline.isValid)
            m_device.DestroyGraphicsPipeline(program.graphicsPipeline);
        if (program.computePipeline.isValid)
            m_device.DestroyComputePipeline(program.computePipeline);
    }

    private sealed class ProgramEntry
    {
        internal ProgramEntry(
            GraphicsPipelineHandle graphicsPipeline,
            ComputePipelineHandle computePipeline,
            ShaderInterface shaderInterface,
            long shaderContentVersion,
            ulong lastUsedFrame)
        {
            this.graphicsPipeline = graphicsPipeline;
            this.computePipeline = computePipeline;
            this.shaderInterface = shaderInterface;
            this.shaderContentVersion = shaderContentVersion;
            this.lastUsedFrame = lastUsedFrame;
        }

        internal GraphicsPipelineHandle graphicsPipeline { get; }
        internal ComputePipelineHandle computePipeline { get; }
        internal ShaderInterface shaderInterface { get; }
        internal long shaderContentVersion { get; }
        internal ulong lastUsedFrame { get; set; }
    }

    private readonly record struct ProgramKey(
        Guid shaderId,
        string targetKey,
        string variantKey,
        string passName,
        ShaderProgramKind kind,
        RenderVertexLayout? vertexLayout);
}
