using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Inno.Assets;
using Inno.Assets.Core;
using Inno.Assets.File;
using Inno.Core.Mathematics;
using Inno.Rendering.Assets;
using Inno.Rendering.Core;

namespace Inno.Rendering.Runtime;

internal sealed class RenderResourceService : IRenderResourceService, IDisposable
{
    private const ulong C_UNUSED_FRAME_LIMIT = 240;

    private readonly IRenderDevice m_device;
    private readonly IRenderDiagnosticSink m_diagnostics;
    private readonly ShaderCompiler? m_shaderCompiler;
    private readonly ShaderLastGoodStore m_shaderLastGood = new();
    private readonly ITextureTargetCompiler? m_textureCompiler;
    private readonly Dictionary<RenderPersistentResourceId, BufferEntry> m_buffers = [];
    private readonly Dictionary<RenderPersistentResourceId, TextureEntry> m_textures = [];
    private readonly Dictionary<RenderPersistentResourceId, GraphicsPipelineEntry> m_graphicsPipelines = [];
    private readonly Dictionary<RenderPersistentResourceId, ComputePipelineEntry> m_computePipelines = [];
    private readonly Dictionary<Guid, GeometryMetadata> m_geometryMetadata = [];
    private readonly Dictionary<ShaderArtifactKey, ShaderArtifactEntry> m_shaderArtifacts = [];
    private readonly Dictionary<ProgramKey, ProgramEntry> m_programs = [];
    private ulong m_frameIndex;
    private bool m_disposed;

    internal RenderResourceService(
        IRenderDevice device,
        IRenderDiagnosticSink diagnostics,
        ShaderCompiler? shaderCompiler,
        ITextureTargetCompiler? textureCompiler)
    {
        m_device = device ?? throw new ArgumentNullException(nameof(device));
        m_diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        m_shaderCompiler = shaderCompiler;
        m_textureCompiler = textureCompiler;
    }

    public GraphicsCapabilities capabilities => m_device.capabilities;

    public PersistentBufferHandle AcquireBuffer(
        RenderPersistentResourceId id,
        long revision,
        PersistentBufferDescriptor descriptor,
        ReadOnlyMemory<byte> initialData,
        string name)
    {
        ThrowIfDisposed();
        RequireId(id);
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (m_buffers.TryGetValue(id, out BufferEntry? current)
            && current.revision == revision
            && BufferDescriptorsEqual(current.descriptor, descriptor))
        {
            current.lastUsedFrame = m_frameIndex;
            return current.handle;
        }

        PersistentBufferHandle candidate = m_device.CreateBuffer(descriptor, initialData.Span, name);
        var replacement = new BufferEntry(candidate, descriptor, revision, m_frameIndex);
        if (current is not null)
            m_device.DestroyBuffer(current.handle);
        m_buffers[id] = replacement;
        return candidate;
    }

    public PersistentTextureHandle AcquireTexture(
        RenderPersistentResourceId id,
        long revision,
        RenderTextureDescriptor descriptor,
        IReadOnlyList<RenderTextureSubresourceData> subresources,
        string name)
    {
        ThrowIfDisposed();
        RequireId(id);
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(subresources);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (subresources.Count == 0)
            throw new ArgumentException("A raw texture requires at least one subresource upload.", nameof(subresources));
        if (m_textures.TryGetValue(id, out TextureEntry? current)
            && current.kind == TextureEntryKind.Raw
            && current.revision == revision
            && descriptor.Equals(current.descriptor))
        {
            current.lastUsedFrame = m_frameIndex;
            return current.handle;
        }

        PersistentTextureHandle candidate = m_device.CreateTexture(descriptor, name);
        try
        {
            foreach (RenderTextureSubresourceData subresource in subresources)
            {
                if (subresource.mipLevel >= descriptor.mipCount
                    || (subresource.mipLevel < descriptor.mipCount
                        && subresource.arrayLayer
                            >= descriptor.GetSubresourceLayerCount(subresource.mipLevel)))
                {
                    throw new ArgumentException(
                        "A texture upload addresses a mip or layer outside its descriptor.",
                        nameof(subresources));
                }
                m_device.UpdateTexture(
                    candidate,
                    subresource.data.Span,
                    subresource.mipLevel,
                    subresource.arrayLayer);
            }
        }
        catch
        {
            m_device.DestroyTexture(candidate);
            throw;
        }

        var replacement = new TextureEntry(
            candidate,
            TextureEntryKind.Raw,
            descriptor,
            revision,
            m_frameIndex);
        if (current is not null)
            m_device.DestroyTexture(current.handle);
        m_textures[id] = replacement;
        return candidate;
    }

    public PersistentTextureHandle AcquireKtxTexture(
        RenderPersistentResourceId id,
        long revision,
        ReadOnlyMemory<byte> containerData,
        bool sRgb,
        string name)
    {
        ThrowIfDisposed();
        RequireId(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (containerData.IsEmpty)
            throw new ArgumentException("A KTX resource cannot be empty.", nameof(containerData));
        if (m_textures.TryGetValue(id, out TextureEntry? current)
            && current.kind == TextureEntryKind.Ktx
            && current.revision == revision
            && current.sRgb == sRgb)
        {
            current.lastUsedFrame = m_frameIndex;
            return current.handle;
        }

        PersistentTextureHandle candidate = m_device.CreateTexture(
            RenderTextureContainer.Ktx,
            containerData.Span,
            sRgb,
            name);
        var replacement = new TextureEntry(
            candidate,
            TextureEntryKind.Ktx,
            descriptor: null,
            revision,
            m_frameIndex,
            sRgb);
        if (current is not null)
            m_device.DestroyTexture(current.handle);
        m_textures[id] = replacement;
        return candidate;
    }

    public bool TryResolveGraphicsMaterial(
        MaterialAsset material,
        ShaderContractId contractId,
        ShaderPassRoleId passRoleId,
        RenderVertexLayout? vertexLayout,
        MaterialPropertyBlock? overrides,
        out RenderMaterialPass? materialPass)
        => TryResolveMaterial(
            material,
            contractId,
            passRoleId,
            ShaderProgramKind.Raster,
            vertexLayout,
            overrides,
            out materialPass);

    public bool TryResolveComputeMaterial(
        MaterialAsset material,
        ShaderContractId contractId,
        ShaderPassRoleId passRoleId,
        MaterialPropertyBlock? overrides,
        out RenderMaterialPass? materialPass)
        => TryResolveMaterial(
            material,
            contractId,
            passRoleId,
            ShaderProgramKind.Compute,
            vertexLayout: null,
            overrides,
            out materialPass);

    public bool TryResolveGeometry(GeometryAsset geometry, out RenderGeometry? resolvedGeometry)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(geometry);
        resolvedGeometry = null;
        Guid id = geometry.identity.persistentId;
        if (id == Guid.Empty)
        {
            Publish("RENDER_GEOMETRY_ID_MISSING", "Geometry must have a persistent asset identity.", geometry.assetPath.ToString());
            return false;
        }

        var vertexResource = new RenderPersistentResourceId($"asset:{id:D}:geometry:vertices");
        var indexResource = new RenderPersistentResourceId($"asset:{id:D}:geometry:indices");
        try
        {
            GeometryData data = GeometryAssetRuntime.GetGeometryData(geometry);
            RenderVertexLayout layout = CanonicalGeometryLayout();
            byte[] vertices = PackVertices(data.vertices);
            uint[] indexValues = data.indices.ToArray();
            byte[] indices = MemoryMarshal.AsBytes(indexValues.AsSpan()).ToArray();
            PersistentBufferHandle vertexBuffer = AcquireBuffer(
                vertexResource,
                geometry.contentVersion,
                new PersistentBufferDescriptor(
                    new RenderBufferDescriptor(data.vertices.Count, layout.stride, RenderBufferUsage.Vertex),
                    layout),
                vertices,
                $"{geometry.name}/Vertices");
            PersistentBufferHandle indexBuffer = AcquireBuffer(
                indexResource,
                geometry.contentVersion,
                new PersistentBufferDescriptor(
                    new RenderBufferDescriptor(data.indices.Count, sizeof(uint), RenderBufferUsage.Index),
                    indexFormat: RenderIndexFormat.UInt32),
                indices,
                $"{geometry.name}/Indices");
            RenderGeometrySection[] sections = data.sections.Select(static section =>
                new RenderGeometrySection(section.firstIndex, section.indexCount)).ToArray();
            m_geometryMetadata[id] = new GeometryMetadata(
                layout,
                data.vertices.Count,
                data.indices.Count,
                sections);
            resolvedGeometry = new RenderGeometry(
                vertexBuffer,
                indexBuffer,
                layout,
                data.vertices.Count,
                data.indices.Count,
                sections);
            return true;
        }
        catch (Exception exception)
        {
            Publish(
                "RENDER_GEOMETRY_RESOLVE_FAILED",
                $"Geometry '{geometry.assetPath.ToString()}' kept any last-good GPU buffers: {exception.Message}",
                geometry.assetPath.ToString());
            if (m_buffers.TryGetValue(vertexResource, out BufferEntry? vertices)
                && m_buffers.TryGetValue(indexResource, out BufferEntry? indices))
            {
                vertices.lastUsedFrame = m_frameIndex;
                indices.lastUsedFrame = m_frameIndex;
                GeometryMetadata metadata = m_geometryMetadata.TryGetValue(id, out GeometryMetadata? lastGood)
                    ? lastGood
                    : new GeometryMetadata(
                        vertices.descriptor.vertexLayout ?? CanonicalGeometryLayout(),
                        geometry.vertexCount,
                        geometry.indexCount,
                        geometry.sectionCount > 0
                            ? [new RenderGeometrySection(0, Math.Max(1, geometry.indexCount))]
                            : []);
                resolvedGeometry = new RenderGeometry(
                    vertices.handle,
                    indices.handle,
                    metadata.layout,
                    metadata.vertexCount,
                    metadata.indexCount,
                    metadata.sections);
                return true;
            }
            return false;
        }
    }

    public bool TryResolveTexture(TextureAsset texture, out PersistentTextureHandle resolvedTexture)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(texture);
        resolvedTexture = default;
        Guid id = texture.identity.persistentId;
        if (id == Guid.Empty)
        {
            Publish("RENDER_TEXTURE_ID_MISSING", "Texture must have a persistent asset identity.", texture.assetPath.ToString());
            return false;
        }

        var resourceId = new RenderPersistentResourceId($"asset:{id:D}:texture");
        bool sRgb = texture.colorSpace == TextureColorSpace.Srgb;
        if (m_textures.TryGetValue(resourceId, out TextureEntry? current)
            && current.kind == TextureEntryKind.Ktx
            && current.revision == texture.contentVersion
            && current.sRgb == sRgb)
        {
            current.lastUsedFrame = m_frameIndex;
            resolvedTexture = current.handle;
            return true;
        }
        try
        {
            if (m_textureCompiler is null)
            {
                throw new InvalidOperationException(
                    "No texture target compiler is configured for this render runtime.");
            }
            string sourcePath = ResolvePhysicalSource(texture.assetPath);
            byte[] ktx = m_textureCompiler.CompileKtx(sourcePath, texture.colorSpace);
            resolvedTexture = AcquireKtxTexture(
                resourceId,
                texture.contentVersion,
                ktx,
                sRgb,
                texture.name);
            return true;
        }
        catch (Exception exception)
        {
            Publish(
                "RENDER_TEXTURE_RESOLVE_FAILED",
                $"Texture '{texture.assetPath.ToString()}' kept its last-good GPU resource: {exception.Message}",
                texture.assetPath.ToString());
            if (m_textures.TryGetValue(resourceId, out TextureEntry? lastGood))
            {
                lastGood.lastUsedFrame = m_frameIndex;
                resolvedTexture = lastGood.handle;
                return true;
            }
            return false;
        }
    }

    public GraphicsPipelineHandle AcquireGraphicsPipeline(
        RenderPersistentResourceId id,
        long revision,
        GraphicsPipelineDescriptor descriptor,
        string name)
    {
        ThrowIfDisposed();
        RequireId(id);
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (m_graphicsPipelines.TryGetValue(id, out GraphicsPipelineEntry? current)
            && current.revision == revision)
        {
            current.lastUsedFrame = m_frameIndex;
            return current.handle;
        }

        GraphicsPipelineHandle candidate = m_device.CreateGraphicsPipeline(descriptor, name);
        var replacement = new GraphicsPipelineEntry(candidate, revision, m_frameIndex);
        m_graphicsPipelines[id] = replacement;
        if (current is not null)
            m_device.DestroyGraphicsPipeline(current.handle);
        return candidate;
    }

    public ComputePipelineHandle AcquireComputePipeline(
        RenderPersistentResourceId id,
        long revision,
        ComputePipelineDescriptor descriptor,
        string name)
    {
        ThrowIfDisposed();
        RequireId(id);
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (m_computePipelines.TryGetValue(id, out ComputePipelineEntry? current)
            && current.revision == revision)
        {
            current.lastUsedFrame = m_frameIndex;
            return current.handle;
        }

        ComputePipelineHandle candidate = m_device.CreateComputePipeline(descriptor, name);
        var replacement = new ComputePipelineEntry(candidate, revision, m_frameIndex);
        m_computePipelines[id] = replacement;
        if (current is not null)
            m_device.DestroyComputePipeline(current.handle);
        return candidate;
    }

    public void Release(RenderPersistentResourceId id)
    {
        ThrowIfDisposed();
        RequireId(id);
        if (m_buffers.Remove(id, out BufferEntry? buffer))
            m_device.DestroyBuffer(buffer.handle);
        if (m_textures.Remove(id, out TextureEntry? texture))
            m_device.DestroyTexture(texture.handle);
        if (m_graphicsPipelines.Remove(id, out GraphicsPipelineEntry? graphicsPipeline))
            m_device.DestroyGraphicsPipeline(graphicsPipeline.handle);
        if (m_computePipelines.Remove(id, out ComputePipelineEntry? computePipeline))
            m_device.DestroyComputePipeline(computePipeline.handle);
    }

    public void Dispose()
    {
        if (m_disposed)
            return;
        m_disposed = true;
        foreach (ProgramEntry program in m_programs.Values)
            DestroyProgram(program);
        foreach (BufferEntry buffer in m_buffers.Values)
            m_device.DestroyBuffer(buffer.handle);
        foreach (TextureEntry texture in m_textures.Values)
            m_device.DestroyTexture(texture.handle);
        foreach (GraphicsPipelineEntry pipeline in m_graphicsPipelines.Values)
            m_device.DestroyGraphicsPipeline(pipeline.handle);
        foreach (ComputePipelineEntry pipeline in m_computePipelines.Values)
            m_device.DestroyComputePipeline(pipeline.handle);
        m_programs.Clear();
        m_shaderArtifacts.Clear();
        m_geometryMetadata.Clear();
        m_buffers.Clear();
        m_textures.Clear();
        m_graphicsPipelines.Clear();
        m_computePipelines.Clear();
    }

    internal void BeginFrame(ulong frameIndex)
    {
        ThrowIfDisposed();
        m_frameIndex = frameIndex;
    }

    internal void SweepUnused()
    {
        ThrowIfDisposed();
        if (m_frameIndex < C_UNUSED_FRAME_LIMIT)
            return;
        ulong oldest = m_frameIndex - C_UNUSED_FRAME_LIMIT;
        foreach (RenderPersistentResourceId id in m_buffers
                     .Where(pair => pair.Value.lastUsedFrame < oldest)
                     .Select(static pair => pair.Key)
                     .ToArray())
        {
            if (m_buffers.Remove(id, out BufferEntry? entry))
                m_device.DestroyBuffer(entry.handle);
        }
        foreach (RenderPersistentResourceId id in m_textures
                     .Where(pair => pair.Value.lastUsedFrame < oldest)
                     .Select(static pair => pair.Key)
                     .ToArray())
        {
            if (m_textures.Remove(id, out TextureEntry? entry))
                m_device.DestroyTexture(entry.handle);
        }
        foreach (ProgramKey key in m_programs
                     .Where(pair => pair.Value.lastUsedFrame < oldest)
                     .Select(static pair => pair.Key)
                     .ToArray())
        {
            if (m_programs.Remove(key, out ProgramEntry? entry))
                DestroyProgram(entry);
        }
        foreach (RenderPersistentResourceId id in m_graphicsPipelines
                     .Where(pair => pair.Value.lastUsedFrame < oldest)
                     .Select(static pair => pair.Key)
                     .ToArray())
        {
            if (m_graphicsPipelines.Remove(id, out GraphicsPipelineEntry? entry))
                m_device.DestroyGraphicsPipeline(entry.handle);
        }
        foreach (RenderPersistentResourceId id in m_computePipelines
                     .Where(pair => pair.Value.lastUsedFrame < oldest)
                     .Select(static pair => pair.Key)
                     .ToArray())
        {
            if (m_computePipelines.Remove(id, out ComputePipelineEntry? entry))
                m_device.DestroyComputePipeline(entry.handle);
        }
    }

    private bool TryResolveMaterial(
        MaterialAsset material,
        ShaderContractId contractId,
        ShaderPassRoleId passRoleId,
        ShaderProgramKind expectedKind,
        RenderVertexLayout? vertexLayout,
        MaterialPropertyBlock? overrides,
        out RenderMaterialPass? materialPass)
    {
        ThrowIfDisposed();
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
        ShaderVariantKey variant;
        try
        {
            variant = BuildVariant(shader, material);
        }
        catch (Exception exception)
        {
            Publish("RENDER_MATERIAL_VARIANT_INVALID", exception.Message, material.assetPath.ToString());
            return false;
        }

        if (!TryResolveProgram(shader, resolution.pass, variant, vertexLayout, out ProgramEntry? program))
            return false;
        ProgramEntry activeProgram = program!;
        if (!TryBuildBindings(material, overrides, activeProgram.shaderInterface, out RenderMaterialBinding[] bindings))
            return false;
        materialPass = new RenderMaterialPass(
            resolution.pass,
            activeProgram.graphicsPipeline,
            activeProgram.computePipeline,
            bindings);
        return true;
    }

    private bool TryResolveProgram(
        ShaderAsset shader,
        ShaderPassDefinition pass,
        ShaderVariantKey variant,
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

        ShaderCompileTarget target;
        try
        {
            if (m_shaderCompiler is null)
            {
                throw new InvalidOperationException(
                    "No shader target compiler is configured for this render runtime.");
            }
            target = m_shaderCompiler.CreateTarget(
                m_device.capabilities,
                optimize: true,
                debugInformation: false);
        }
        catch (Exception exception)
        {
            Publish("RENDER_SHADER_TARGET_UNAVAILABLE", exception.Message, shader.assetPath.ToString());
            return false;
        }

        var key = new ProgramKey(
            shaderId,
            target.key,
            variant.value,
            pass.name,
            pass.programKind,
            vertexLayout);
        m_programs.TryGetValue(key, out ProgramEntry? current);
        ShaderArtifactSelection selection = CompileShader(shader, target, variant);
        if (!selection.candidateSucceeded && current is not null)
        {
            current.lastUsedFrame = m_frameIndex;
            program = current;
            return true;
        }
        if (selection.artifact is null)
            return false;
        if (current is not null
            && current.shaderContentVersion == shader.contentVersion
            && selection.candidateSucceeded)
        {
            current.lastUsedFrame = m_frameIndex;
            program = current;
            return true;
        }

        CompiledShaderPass? compiledPass = selection.artifact.passes.FirstOrDefault(candidate =>
            string.Equals(candidate.definition.name, pass.name, StringComparison.Ordinal));
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

        try
        {
            ProgramEntry candidate = CreateProgram(
                shader,
                compiledPass,
                vertexLayout,
                shader.contentVersion);
            candidate.lastUsedFrame = m_frameIndex;
            if (current is not null)
                DestroyProgram(current);
            m_programs[key] = candidate;
            program = candidate;
            return true;
        }
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
    }

    private ShaderArtifactSelection CompileShader(
        ShaderAsset shader,
        ShaderCompileTarget target,
        ShaderVariantKey variant)
    {
        Guid shaderId = shader.identity.persistentId;
        var key = new ShaderArtifactKey(shaderId, target.key, variant.value);
        if (m_shaderArtifacts.TryGetValue(key, out ShaderArtifactEntry? cached)
            && cached.attemptedContentVersion == shader.contentVersion)
        {
            return cached.selection;
        }

        ShaderCompilationResult result;
        try
        {
            ShaderIRModule module = ShaderAssetRuntime.GetModule(shader);
            result = m_shaderCompiler!.CompileAsync(
                    module,
                    target,
                    variant,
                    ResolveSourceRoot(shader.assetPath))
                .AsTask()
                .GetAwaiter()
                .GetResult();
        }
        catch (Exception exception)
        {
            result = new ShaderCompilationResult(
                null,
                [new ShaderDiagnostic(
                    "SHADER_COMPILE_EXCEPTION",
                    ShaderDiagnosticSeverity.Error,
                    exception.Message,
                    new ShaderSourceLocation(
                        shader.assetPath.ToString(),
                        "Shader",
                        ShaderStage.None))]);
        }

        foreach (ShaderDiagnostic diagnostic in result.diagnostics)
        {
            string diagnosticSource = diagnostic.location?.assetPath ?? shader.assetPath.ToString();
            m_diagnostics.Publish(new RenderDiagnostic(
                diagnostic.code,
                diagnostic.message,
                diagnostic.severity == ShaderDiagnosticSeverity.Error
                    ? RenderDiagnosticSeverity.Error
                    : diagnostic.severity == ShaderDiagnosticSeverity.Warning
                        ? RenderDiagnosticSeverity.Warning
                        : RenderDiagnosticSeverity.Info,
                string.IsNullOrWhiteSpace(diagnosticSource)
                    ? shader.assetPath.ToString()
                    : diagnosticSource));
        }
        ShaderArtifactSelection selection = m_shaderLastGood.Select(shaderId, target.key, variant, result);
        if (selection.usingLastGood)
        {
            m_diagnostics.Publish(new RenderDiagnostic(
                "RENDER_SHADER_USING_LAST_GOOD",
                $"Shader '{shader.assetPath.ToString()}' is using its last-good compiled artifact.",
                RenderDiagnosticSeverity.Warning,
                shader.assetPath.ToString()));
        }
        m_shaderArtifacts[key] = new ShaderArtifactEntry(shader.contentVersion, selection);
        return selection;
    }

    private ProgramEntry CreateProgram(
        ShaderAsset shader,
        CompiledShaderPass pass,
        RenderVertexLayout? vertexLayout,
        long shaderContentVersion)
    {
        IReadOnlyList<RenderShaderBindingDescriptor> bindings = BuildBindingDescriptors(pass.shaderInterface);
        if (pass.definition.programKind == ShaderProgramKind.Raster)
        {
            ReadOnlyMemory<byte> vertex = pass.stages.Single(stage => stage.stage == ShaderStage.Vertex).bytes;
            ReadOnlyMemory<byte> fragment = pass.stages.Single(stage => stage.stage == ShaderStage.Fragment).bytes;
            GraphicsPipelineHandle pipeline = m_device.CreateGraphicsPipeline(
                new GraphicsPipelineDescriptor(
                    vertex.Span,
                    fragment.Span,
                    bindings,
                    vertexLayout,
                    ConvertRasterState(pass.definition.renderState)),
                $"{shader.name}/{pass.definition.name}");
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
            $"{shader.name}/{pass.definition.name}");
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
        out RenderMaterialBinding[] bindings)
    {
        var result = new List<RenderMaterialBinding>();
        ShaderDefinition definition = material.shader!.definition!;
        Dictionary<ShaderPropertyId, ShaderPropertyDefinition> definitions = definition.properties
            .ToDictionary(static property => property.id);
        foreach (ShaderInterfaceBinding binding in shaderInterface.bindings)
        {
            if (!definitions.TryGetValue(binding.id, out ShaderPropertyDefinition property))
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
                if (!TryResolveTexture(value.texture, out PersistentTextureHandle texture))
                {
                    bindings = [];
                    return false;
                }
                result.Add(new RenderMaterialBinding(
                    RenderMaterialBindingKind.Texture,
                    new RenderBindingId(binding.id.value),
                    null,
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
            result.Add(new RenderMaterialBinding(
                RenderMaterialBindingKind.Uniform,
                new RenderBindingId(binding.id.value),
                bytes,
                default,
                default));
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

    private static ShaderVariantKey BuildVariant(ShaderAsset shader, MaterialAsset material)
    {
        ShaderDefinition definition = shader.definition
            ?? throw new InvalidOperationException($"Shader '{shader.assetPath.ToString()}' has no committed definition.");
        HashSet<string> enabled = material.keywords.ToHashSet(StringComparer.Ordinal);
        var selections = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (ShaderKeywordDefinition keyword in definition.keywords)
        {
            string[] selected = keyword.options.Where(enabled.Contains).ToArray();
            if (selected.Length > 1)
            {
                throw new InvalidOperationException(
                    $"Material '{material.assetPath.ToString()}' selects multiple options for keyword '{keyword.id}'.");
            }
            if (selected.Length == 1)
                selections.Add(keyword.id, selected[0]);
        }
        string? unknown = enabled.FirstOrDefault(option => !definition.keywords.Any(keyword =>
            keyword.options.Contains(option, StringComparer.Ordinal)));
        if (unknown is not null)
            throw new InvalidOperationException($"Material '{material.assetPath.ToString()}' enables unknown option '{unknown}'.");
        return new ShaderVariantKey(selections);
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

    private static RenderRasterState ConvertRasterState(ShaderRenderState source)
        => new()
        {
            topology = source.topology,
            cull = source.cull switch
            {
                ShaderCullMode.None => RenderCullMode.None,
                ShaderCullMode.Front => RenderCullMode.Front,
                ShaderCullMode.Back => RenderCullMode.Back,
                _ => throw new ArgumentOutOfRangeException(nameof(source))
            },
            frontFace = source.frontFace,
            depthCompare = source.depthCompare switch
            {
                ShaderCompareFunction.Never => RenderDepthCompare.Never,
                ShaderCompareFunction.Less => RenderDepthCompare.Less,
                ShaderCompareFunction.Equal => RenderDepthCompare.Equal,
                ShaderCompareFunction.LessEqual => RenderDepthCompare.LessEqual,
                ShaderCompareFunction.Greater => RenderDepthCompare.Greater,
                ShaderCompareFunction.NotEqual => RenderDepthCompare.NotEqual,
                ShaderCompareFunction.GreaterEqual => RenderDepthCompare.GreaterEqual,
                ShaderCompareFunction.Always => RenderDepthCompare.Always,
                _ => throw new ArgumentOutOfRangeException(nameof(source))
            },
            depthWrite = source.depthWrite,
            blend = source.blend,
            colorWriteMask = source.colorWriteMask,
            multisampling = source.multisampling
        };

    private static RenderVertexLayout CanonicalGeometryLayout()
        => new([
            new RenderVertexAttribute(RenderVertexSemantic.Position, RenderVertexFormat.Float3),
            new RenderVertexAttribute(RenderVertexSemantic.Normal, RenderVertexFormat.Float3),
            new RenderVertexAttribute(RenderVertexSemantic.Tangent, RenderVertexFormat.Float4),
            new RenderVertexAttribute(RenderVertexSemantic.TextureCoordinate0, RenderVertexFormat.Float2)
        ]);

    private static byte[] PackVertices(IReadOnlyList<GeometryVertex> vertices)
    {
        var values = new float[checked(vertices.Count * 12)];
        int offset = 0;
        foreach (GeometryVertex vertex in vertices)
        {
            values[offset++] = vertex.position.x;
            values[offset++] = vertex.position.y;
            values[offset++] = vertex.position.z;
            values[offset++] = vertex.normal.x;
            values[offset++] = vertex.normal.y;
            values[offset++] = vertex.normal.z;
            values[offset++] = vertex.tangent.x;
            values[offset++] = vertex.tangent.y;
            values[offset++] = vertex.tangent.z;
            values[offset++] = vertex.tangent.w;
            values[offset++] = vertex.textureCoordinate.x;
            values[offset++] = vertex.textureCoordinate.y;
        }
        return MemoryMarshal.AsBytes(values.AsSpan()).ToArray();
    }

    private static bool BufferDescriptorsEqual(PersistentBufferDescriptor left, PersistentBufferDescriptor right)
        => left.buffer.Equals(right.buffer)
            && Equals(left.vertexLayout, right.vertexLayout)
            && left.indexFormat == right.indexFormat;

    private static string ResolveSourceRoot(AssetPath assetPath)
        => GetMount(assetPath.source).rootPath;

    private static string ResolvePhysicalSource(AssetPath assetPath)
        => GetMount(assetPath.source).Resolve(assetPath.localPath);

    private static AssetSourceMount GetMount(AssetSourceId source)
        => AssetManager.sourceMounts.FirstOrDefault(mount => mount.id == source)
            ?? throw new InvalidOperationException($"Asset source mount '{source}' is not active.");

    private static void RequireId(RenderPersistentResourceId id)
    {
        if (!id.isValid)
            throw new ArgumentException("A persistent render resource ID must be valid.", nameof(id));
    }

    private void Publish(string code, string message, string? source)
        => m_diagnostics.Publish(new RenderDiagnostic(
            code,
            message,
            RenderDiagnosticSeverity.Error,
            source));

    private void DestroyProgram(ProgramEntry program)
    {
        if (program.graphicsPipeline.isValid)
            m_device.DestroyGraphicsPipeline(program.graphicsPipeline);
        if (program.computePipeline.isValid)
            m_device.DestroyComputePipeline(program.computePipeline);
    }

    private void ThrowIfDisposed()
        => ObjectDisposedException.ThrowIf(m_disposed, this);

    private enum TextureEntryKind
    {
        Raw,
        Ktx
    }

    private sealed class BufferEntry
    {
        internal BufferEntry(
            PersistentBufferHandle handle,
            PersistentBufferDescriptor descriptor,
            long revision,
            ulong lastUsedFrame)
        {
            this.handle = handle;
            this.descriptor = descriptor;
            this.revision = revision;
            this.lastUsedFrame = lastUsedFrame;
        }

        internal PersistentBufferHandle handle { get; }
        internal PersistentBufferDescriptor descriptor { get; }
        internal long revision { get; }
        internal ulong lastUsedFrame { get; set; }
    }

    private sealed class TextureEntry
    {
        internal TextureEntry(
            PersistentTextureHandle handle,
            TextureEntryKind kind,
            RenderTextureDescriptor? descriptor,
            long revision,
            ulong lastUsedFrame,
            bool sRgb = false)
        {
            this.handle = handle;
            this.kind = kind;
            this.descriptor = descriptor;
            this.revision = revision;
            this.lastUsedFrame = lastUsedFrame;
            this.sRgb = sRgb;
        }

        internal PersistentTextureHandle handle { get; }
        internal TextureEntryKind kind { get; }
        internal RenderTextureDescriptor? descriptor { get; }
        internal long revision { get; }
        internal bool sRgb { get; }
        internal ulong lastUsedFrame { get; set; }
    }

    private sealed class GraphicsPipelineEntry
    {
        internal GraphicsPipelineEntry(
            GraphicsPipelineHandle handle,
            long revision,
            ulong lastUsedFrame)
        {
            this.handle = handle;
            this.revision = revision;
            this.lastUsedFrame = lastUsedFrame;
        }

        internal GraphicsPipelineHandle handle { get; }
        internal long revision { get; }
        internal ulong lastUsedFrame { get; set; }
    }

    private sealed class ComputePipelineEntry
    {
        internal ComputePipelineEntry(
            ComputePipelineHandle handle,
            long revision,
            ulong lastUsedFrame)
        {
            this.handle = handle;
            this.revision = revision;
            this.lastUsedFrame = lastUsedFrame;
        }

        internal ComputePipelineHandle handle { get; }
        internal long revision { get; }
        internal ulong lastUsedFrame { get; set; }
    }

    private sealed class ShaderArtifactEntry
    {
        internal ShaderArtifactEntry(long attemptedContentVersion, ShaderArtifactSelection selection)
        {
            this.attemptedContentVersion = attemptedContentVersion;
            this.selection = selection;
        }

        internal long attemptedContentVersion { get; }
        internal ShaderArtifactSelection selection { get; }
    }

    private sealed record GeometryMetadata(
        RenderVertexLayout layout,
        int vertexCount,
        int indexCount,
        IReadOnlyList<RenderGeometrySection> sections);

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

    private readonly record struct ShaderArtifactKey(Guid shaderId, string targetKey, string variantKey);

    private readonly record struct ProgramKey(
        Guid shaderId,
        string targetKey,
        string variantKey,
        string passName,
        ShaderProgramKind kind,
        RenderVertexLayout? vertexLayout);
}
