using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Inno.Core.Diagnostics;
using Inno.Core.Execution;
using Inno.Core.Mathematics;

namespace Inno.Rendering.Runtime;

internal sealed class RenderMaterialOwner : RenderResourceProvider, IDisposable
{
    internal delegate bool TextureResolver(RenderTextureArtifactReference texture, out PersistentTextureHandle handle);

    private readonly IRenderDevice m_device;
    private readonly IDiagnosticReporter m_diagnostics;
    private readonly IRenderTargetArtifactProvider? m_targetArtifacts;
    private readonly TextureResolver m_textureResolver;
    private readonly RenderResourceCache<PublicationKey, ProgramPublication> m_programs;
    private readonly Dictionary<LookupKey, ArtifactResult> m_frameArtifacts = [];
    private readonly HashSet<string> m_failedFrameCandidates = new(StringComparer.Ordinal);
    private readonly List<ProgramPublication> m_failedCandidates = [];
    private readonly List<Exception> m_failedCleanupFailures = [];
    private readonly int m_capacity;
    private ulong m_frameIndex;
    private bool m_stopping;
    private RenderRetirementQueue? m_retirement;

    internal RenderMaterialOwner(IRenderDevice device, IDiagnosticReporter diagnostics,
        IRenderTargetArtifactProvider? targetArtifacts, TextureResolver textureResolver, int capacity)
    {
        m_device = device;
        m_diagnostics = diagnostics;
        m_targetArtifacts = targetArtifacts;
        m_textureResolver = textureResolver;
        m_capacity = capacity;
        m_programs = new(DestroyPublication, capacity);
    }

    internal int count { get { int result = 0; foreach (var pair in m_programs) result += pair.Value.programs.Count; return result; } }
    internal int retiringCount => m_programs.pendingCount + m_failedCandidates.Count;
    internal long rejectedCount => m_programs.rejectedCount;

    internal void BeginFrame(ulong frameIndex)
    {
        Drain();
        m_frameIndex = frameIndex;
        m_frameArtifacts.Clear();
        m_failedFrameCandidates.Clear();
    }

    internal void Sweep(ulong oldest) => m_programs.Sweep(entry => entry.lastUsedFrame < oldest);
    internal void Drain()
    {
        ObjectDisposedException.ThrowIf(m_stopping, this);
        DrainFailed();
        m_programs.Drain();
    }

    /// <summary>Retires every program publication without releasing dependencies before pending native retirement completes.</summary>
    public void Dispose()
    {
        m_stopping = true;
        if (m_retirement is null)
        {
            m_retirement = new();
            m_retirement.Add(DrainFailed);
            m_retirement.Add(m_programs.Dispose);
        }
        m_retirement.Dispose();
    }

    internal bool TryResolveMaterial(MaterialAsset material, ShaderContractId contractId, ShaderPassRoleId passRoleId,
        ShaderProgramKind expectedKind, RenderVertexLayout? vertexLayout, MaterialPropertyBlock? overrides,
        out RenderMaterialPass? materialPass)
    {
        Drain();
        ArgumentNullException.ThrowIfNull(material);
        materialPass = null;
        ShaderAsset? shader = material.shader;
        if (shader is null || shader.identity.persistentId == Guid.Empty)
        {
            Publish("RENDER_SHADER_ID_MISSING", "Material requires a shader with a persistent asset identity.", material.assetPath.ToString());
            return false;
        }

        Guid shaderId = shader.identity.persistentId;
        RenderShaderVariant variant;
        try { variant = RenderShaderVariant.FromMaterial(material); }
        catch (Exception pending) when (RetirementPendingException.Find(pending) is not null) { throw; }
        catch (Exception failure)
        {
            Publish("RENDER_MATERIAL_VARIANT_INVALID", failure.Message, material.assetPath.ToString());
            return TryUseLastGood(material, shaderId, contractId, passRoleId, expectedKind, vertexLayout, overrides, out materialPass);
        }

        ProgramPublication? publication = FindPublication(shaderId, variant);
        ArtifactResult result = QueryArtifact(shader, variant);
        if (result.artifact is { } artifact && result.status == RenderTargetArtifactStatus.Ready
            && publication?.artifact.contentHash != artifact.contentHash
            && !m_failedFrameCandidates.Contains(artifact.contentHash))
        {
            ProgramPublication? candidate = null;
            bool published = false;
            try
            {
                ShaderDefinition definition = m_targetArtifacts!.ReadShaderDefinition(artifact);
                ValidatePublication(artifact, definition, variant);
                MaterialPassResolution? selected = MaterialPassResolver.Resolve(definition, material.techniqueId,
                    contractId, passRoleId, m_device.capabilities);
                if (selected is null) return Unavailable(material, contractId, passRoleId);
                if (selected.pass.programKind != expectedKind) return WrongKind(material, selected.pass, expectedKind);
                candidate = new(artifact, definition);
                var required = new HashSet<ProgramLayout>();
                if (publication is not null)
                    foreach (ProgramLayout layout in publication.programs.Keys)
                        if (artifact.passes.Any(pass => pass.name == layout.passName && pass.programKind == layout.kind))
                            required.Add(layout);
                required.Add(new(selected.pass.name, expectedKind, vertexLayout));
                if (count - (publication?.programs.Count ?? 0) + required.Count > m_capacity)
                    throw new InvalidOperationException("Shader program publication exceeds the configured program capacity.");
                var key = new PublicationKey(shaderId, artifact.targetKey, variant.value);
                m_programs.RequireCapacity(key);
                foreach (ProgramLayout layout in required)
                    candidate.programs.Add(layout, CreateProgram(artifact, layout));
                candidate.lastUsedFrame = m_frameIndex;
                m_programs.Replace(key, candidate);
                publication = candidate;
                candidate = null;
                published = true;
                m_programs.Drain();
                m_diagnostics.Resolve("RENDER_PROGRAM_CREATE_FAILED", shader.assetPath.ToString());
            }
            catch (Exception failure)
            {
                // Publication has already committed; a retirement failure must never be reported as a rollback.
                if (published) throw;
                if (candidate is not null)
                {
                    m_failedCandidates.Add(candidate);
                    try { DrainFailed(); }
                    catch (Exception cleanup) { throw new AggregateException(failure, cleanup); }
                }
                if (RetirementPendingException.Find(failure) is not null) throw;
                m_failedFrameCandidates.Add(artifact.contentHash);
                Publish("RENDER_PROGRAM_CREATE_FAILED", $"Shader kept its complete last-good publication: {failure.Message}", shader.assetPath.ToString());
            }
        }
        if (publication is null) return false;
        return TryUsePublication(material, publication, contractId, passRoleId, expectedKind, vertexLayout, overrides, out materialPass);
    }

    private ArtifactResult QueryArtifact(ShaderAsset shader, RenderShaderVariant variant)
    {
        var key = new LookupKey(shader.identity.persistentId, variant.value);
        if (m_frameArtifacts.TryGetValue(key, out ArtifactResult result)) return result;
        string source = shader.assetPath.ToString();
        try
        {
            RenderShaderArtifact? artifact = null;
            RenderTargetArtifactStatus status = m_targetArtifacts?.GetShaderArtifact(shader, variant, m_device.capabilities, out artifact)
                ?? RenderTargetArtifactStatus.Unavailable;
            if (status == RenderTargetArtifactStatus.Ready && artifact is null)
                throw new InvalidOperationException("The artifact provider returned Ready without a shader publication.");
            result = new(status, artifact);
            if (status == RenderTargetArtifactStatus.Unavailable)
                Publish("RENDER_SHADER_TARGET_UNAVAILABLE", "No target shader artifact is available for this device and variant.", source);
            else m_diagnostics.Resolve("RENDER_SHADER_TARGET_UNAVAILABLE", source);
            m_diagnostics.Resolve("RENDER_SHADER_TARGET_INVALID", source);
        }
        catch (Exception pending) when (RetirementPendingException.Find(pending) is not null) { throw; }
        catch (Exception failure)
        {
            Publish("RENDER_SHADER_TARGET_INVALID", failure.Message, source);
            result = new(RenderTargetArtifactStatus.Unavailable, null);
        }
        m_frameArtifacts.Add(key, result);
        return result;
    }

    private ProgramPublication? FindPublication(Guid shaderId, RenderShaderVariant variant)
    {
        foreach ((PublicationKey key, ProgramPublication publication) in m_programs)
            if (key.shaderId == shaderId && key.variantKey == variant.value) return publication;
        return null;
    }

    private bool TryUseLastGood(MaterialAsset material, Guid shaderId, ShaderContractId contract, ShaderPassRoleId role,
        ShaderProgramKind kind, RenderVertexLayout? layout, MaterialPropertyBlock? overrides, out RenderMaterialPass? result)
    {
        foreach ((PublicationKey key, ProgramPublication publication) in m_programs)
        {
            if (key.shaderId != shaderId) continue;
            RenderShaderVariant variant;
            try { variant = RenderShaderVariant.FromMaterial(material, publication.definition); }
            catch (InvalidOperationException) { continue; }
            if (variant.value == key.variantKey)
                return TryUsePublication(material, publication, contract, role, kind, layout, overrides, out result);
        }
        result = null;
        return false;
    }

    private bool TryUsePublication(MaterialAsset material, ProgramPublication publication, ShaderContractId contract,
        ShaderPassRoleId role, ShaderProgramKind kind, RenderVertexLayout? layout, MaterialPropertyBlock? overrides,
        out RenderMaterialPass? result)
    {
        result = null;
        MaterialPassResolution? resolution = MaterialPassResolver.Resolve(publication.definition, material.techniqueId,
            contract, role, m_device.capabilities);
        if (resolution is null) return Unavailable(material, contract, role);
        if (resolution.pass.programKind != kind) return WrongKind(material, resolution.pass, kind);
        var key = new ProgramLayout(resolution.pass.name, kind, layout);
        if (!publication.programs.TryGetValue(key, out ProgramEntry? program))
        {
            try
            {
                if (count >= m_capacity) throw new InvalidOperationException("Shader program capacity is exhausted.");
                program = CreateProgram(publication.artifact, key);
                publication.programs.Add(key, program);
            }
            catch (Exception pending) when (RetirementPendingException.Find(pending) is not null) { throw; }
            catch (Exception failure) { Publish("RENDER_PROGRAM_CREATE_FAILED", failure.Message, material.assetPath.ToString()); return false; }
        }
        publication.lastUsedFrame = m_frameIndex;
        if (!TryBuildBindings(material, overrides, publication, program.shaderInterface, out MaterialBinding[] bindings)) return false;
        result = CreateMaterialPass(resolution.pass, program.graphicsPipeline, program.computePipeline, bindings);
        m_diagnostics.Resolve("RENDER_MATERIAL_PASS_UNAVAILABLE", material.assetPath.ToString());
        m_diagnostics.Resolve("RENDER_MATERIAL_PASS_KIND_MISMATCH", material.assetPath.ToString());
        return true;
    }

    private bool Unavailable(MaterialAsset material, ShaderContractId contract, ShaderPassRoleId role)
    {
        Publish("RENDER_MATERIAL_PASS_UNAVAILABLE", $"Material does not implement contract '{contract}' role '{role}' in its published shader.", material.assetPath.ToString());
        return false;
    }

    private bool WrongKind(MaterialAsset material, ShaderPassDefinition pass, ShaderProgramKind kind)
    {
        Publish("RENDER_MATERIAL_PASS_KIND_MISMATCH", $"Published pass '{pass.name}' is {pass.programKind}, not {kind}.", material.assetPath.ToString());
        return false;
    }

    private ProgramEntry CreateProgram(RenderShaderArtifact artifact, ProgramLayout layout)
    {
        RenderShaderPassArtifact pass = artifact.passes.Single(value => value.name == layout.passName && value.programKind == layout.kind);
        IReadOnlyList<RenderShaderBindingDescriptor> bindings = BuildBindingDescriptors(pass.shaderInterface);
        if (pass.programKind == ShaderProgramKind.Raster)
        {
            GraphicsPipelineHandle handle = m_device.CreateGraphicsPipeline(new GraphicsPipelineDescriptor(
                pass.stages.Single(value => value.stage == ShaderStage.Vertex).bytes.Span,
                pass.stages.Single(value => value.stage == ShaderStage.Fragment).bytes.Span,
                bindings, layout.vertexLayout, pass.rasterState), artifact.shaderName + "/" + pass.name);
            if (!handle.isValid) throw new InvalidOperationException("The rendering device returned an invalid graphics pipeline.");
            return new(handle, default, pass.shaderInterface);
        }
        ComputePipelineHandle compute = m_device.CreateComputePipeline(new ComputePipelineDescriptor(
            pass.stages.Single(value => value.stage == ShaderStage.Compute).bytes.Span, bindings), artifact.shaderName + "/" + pass.name);
        if (!compute.isValid) throw new InvalidOperationException("The rendering device returned an invalid compute pipeline.");
        return new(default, compute, pass.shaderInterface);
    }

    private void ValidatePublication(RenderShaderArtifact artifact, ShaderDefinition definition, RenderShaderVariant variant)
    {
        if (artifact.variant != variant || definition.name != artifact.shaderName)
            throw new InvalidOperationException("The shader program publication does not match its requested variant and captured definition.");
        foreach (ShaderDiagnostic diagnostic in ShaderDefinitionValidator.Validate(definition, m_device.capabilities))
            if (diagnostic.severity == DiagnosticSeverity.Error) throw new InvalidOperationException(diagnostic.message);
        var properties = definition.properties.ToDictionary(static property => property.id);
        foreach (RenderShaderPassArtifact pass in artifact.passes)
        {
            ShaderPassDefinition declared = definition.passes.SingleOrDefault(value => value.name == pass.name);
            if (declared.name != pass.name || declared.programKind != pass.programKind)
                throw new InvalidOperationException($"Program '{pass.name}' has no matching captured pass contract.");
            foreach (ShaderInterfaceBinding binding in pass.shaderInterface.bindings)
                if (!properties.TryGetValue(binding.id, out ShaderPropertyDefinition property) || property.type != binding.type
                    || property.bindingKind != binding.bindingKind || property.storageAccess != binding.storageAccess
                    || (binding.stages & ~property.stages) != 0)
                    throw new InvalidOperationException($"Program binding '{binding.id}' does not match its captured material contract.");
        }
    }

    private void DrainFailed()
    {
        while (m_failedCandidates.Count != 0)
        {
            try { DestroyPublication(m_failedCandidates[0]); }
            catch (Exception pending) when (RetirementPendingException.Find(pending) is not null) { throw; }
            catch (Exception failure) { m_failedCleanupFailures.Add(failure); }
            m_failedCandidates.RemoveAt(0);
        }
        if (m_failedCleanupFailures.Count != 0)
        {
            var failure = new AggregateException("Rejected shader publications retired with completed failures.", m_failedCleanupFailures);
            m_failedCleanupFailures.Clear();
            throw failure;
        }
    }

    private bool TryBuildBindings(
        MaterialAsset material,
        MaterialPropertyBlock? overrides,
        ProgramPublication publication,
        ShaderInterface shaderInterface,
        out MaterialBinding[] bindings)
    {
        var result = new List<MaterialBinding>();
        Dictionary<ShaderPropertyId, ShaderPropertyDefinition> definitions = publication.properties;
        foreach (ShaderInterfaceBinding binding in shaderInterface.bindings)
        {
            if (!definitions.TryGetValue(binding.id, out ShaderPropertyDefinition property))
                continue;
            if (property.bindingOwner == ShaderPropertyBindingOwner.RenderPass)
                continue;
            bool supplied = overrides is not null && overrides.TryGet(binding.id, out _);
            MaterialValue value;
            if (supplied) { overrides!.TryGet(binding.id, out value); }
            else if (material.TryGet(binding.id, out value)) supplied = true;
            else value = property.defaultValue;
            if (binding.bindingKind == ShaderPropertyBindingKind.SampledTexture)
            {
                RenderTextureArtifactReference reference;
                if (supplied && value.kind == MaterialValueKind.Texture && value.texture is not null)
                    reference = value.texture.GetTextureArtifactReference();
                else if (supplied || !publication.textureDefaults.TryGetValue(binding.id, out reference)) continue;
                if (!m_textureResolver(reference, out PersistentTextureHandle texture))
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
        var textureSlots = new HashSet<int>();
        var storageSlots = new HashSet<int>();
        foreach (ShaderInterfaceBinding binding in shaderInterface.bindings)
        {
            if (!binding.location.HasValue || binding.bindingKind == ShaderPropertyBindingKind.Uniform) continue;
            HashSet<int> used = binding.bindingKind == ShaderPropertyBindingKind.SampledTexture ? textureSlots : storageSlots;
            if (!used.Add(binding.location.Value)) throw new InvalidOperationException($"Shader resource slot '{binding.location}' is assigned more than once in its binding domain.");
        }
        static int Next(HashSet<int> used)
        {
            int slot = 0;
            while (!used.Add(slot)) slot++;
            return slot;
        }
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
                        count: binding.arrayCount,
                        nativeName: binding.nativeName));
                    break;
                case ShaderPropertyBindingKind.SampledTexture:
                    result.Add(new RenderShaderBindingDescriptor(
                        id,
                        RenderShaderBindingKind.Texture,
                        slot: binding.location ?? Next(textureSlots),
                        nativeName: binding.nativeName));
                    break;
                case ShaderPropertyBindingKind.StorageTexture:
                    result.Add(new RenderShaderBindingDescriptor(
                        id,
                        RenderShaderBindingKind.StorageTexture,
                        slot: binding.location ?? Next(storageSlots),
                        storageAccess: binding.storageAccess,
                        nativeName: binding.nativeName));
                    break;
                case ShaderPropertyBindingKind.StorageBuffer:
                    result.Add(new RenderShaderBindingDescriptor(
                        id,
                        RenderShaderBindingKind.StorageBuffer,
                        slot: binding.location ?? Next(storageSlots),
                        storageAccess: binding.storageAccess,
                        nativeName: binding.nativeName));
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

    private void DestroyPublication(ProgramPublication publication)
    {
        if (publication.retirement is null)
        {
            publication.retirement = new();
            foreach (ProgramEntry program in publication.programs.Values)
            {
                if (program.graphicsPipeline.isValid) publication.retirement.Add(() => m_device.DestroyGraphicsPipeline(program.graphicsPipeline));
                if (program.computePipeline.isValid) publication.retirement.Add(() => m_device.DestroyComputePipeline(program.computePipeline));
            }
            publication.retirement.Add(publication.programs.Clear);
        }
        publication.retirement.Dispose();
    }

    private sealed class ProgramPublication
    {
        internal ProgramPublication(RenderShaderArtifact artifact, ShaderDefinition definition)
        {
            this.artifact = artifact;
            this.definition = definition;
            for (int index = 0; index < definition.properties.Length; index++)
            {
                ShaderPropertyDefinition property = definition.properties[index];
                MaterialValue value = property.defaultValue;
                if (value.texture is not null) textureDefaults.Add(property.id, value.texture.GetTextureArtifactReference());
                value.texture = null;
                property.defaultValue = value;
                definition.properties[index] = property;
                properties.Add(property.id, property);
            }
        }
        internal RenderShaderArtifact artifact { get; }
        internal ShaderDefinition definition { get; }
        internal Dictionary<ShaderPropertyId, ShaderPropertyDefinition> properties { get; } = [];
        internal Dictionary<ShaderPropertyId, RenderTextureArtifactReference> textureDefaults { get; } = [];
        internal Dictionary<ProgramLayout, ProgramEntry> programs { get; } = [];
        internal ulong lastUsedFrame { get; set; }
        internal RenderRetirementQueue? retirement { get; set; }
    }

    private sealed record ProgramEntry(GraphicsPipelineHandle graphicsPipeline, ComputePipelineHandle computePipeline,
        ShaderInterface shaderInterface);
    private readonly record struct ProgramLayout(string passName, ShaderProgramKind kind, RenderVertexLayout? vertexLayout);
    private readonly record struct PublicationKey(Guid shaderId, string targetKey, string variantKey);
    private readonly record struct LookupKey(Guid shaderId, string variantKey);
    private readonly record struct ArtifactResult(RenderTargetArtifactStatus status, RenderShaderArtifact? artifact);
}
