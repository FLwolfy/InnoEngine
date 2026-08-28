using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using Inno.Core.Mathematics;
using Inno.Rendering.Assets;
using Inno.Rendering.Core;

namespace Inno.Rendering.Pipelines;

/// <summary>
/// Resolves compiled artifacts into device resources and records built-in pipeline operations.
/// </summary>
public sealed class DefaultRenderPipelineExecutor : IRenderPipelineExecutor, IDisposable
{
    private const int C_MAX_LOCAL_LIGHTS = 8;

    private static readonly RenderBindingId S_CAMERA_POSITION = new("inno_camera_position");
    private static readonly RenderBindingId S_MAIN_LIGHT_DIRECTION = new("inno_main_light_direction");
    private static readonly RenderBindingId S_MAIN_LIGHT_COLOR = new("inno_main_light_color");
    private static readonly RenderBindingId S_LIGHT_COUNT = new("inno_light_count");
    private static readonly RenderBindingId S_VIEW_PARAMETERS = new("inno_view_parameters");
    private static readonly RenderBindingId S_EXPOSURE = new("inno_exposure");
    private static readonly RenderBindingId S_OBJECT_ID = new("inno_object_id");
    private static readonly RenderBindingId S_SHADOW_CASCADE_SPLITS = new("inno_shadow_cascade_splits");
    private static readonly RenderBindingId S_SHADOW_PARAMETERS = new("inno_shadow_parameters");
    private static readonly RenderBindingId[] S_SHADOW_MATRICES = CreateIndexedBindings("inno_shadow_matrix");
    private static readonly RenderBindingId[] S_LOCAL_LIGHT_POSITION_RANGE = CreateIndexedBindings(
        "inno_local_light_position_range");
    private static readonly RenderBindingId[] S_LOCAL_LIGHT_DIRECTION_OUTER = CreateIndexedBindings(
        "inno_local_light_direction_outer");
    private static readonly RenderBindingId[] S_LOCAL_LIGHT_COLOR_INNER = CreateIndexedBindings(
        "inno_local_light_color_inner");
    private static readonly RenderVertexLayout S_MESH_LAYOUT = new(
    [
        new RenderVertexAttribute(RenderVertexSemantic.Position, RenderVertexFormat.Float3),
        new RenderVertexAttribute(RenderVertexSemantic.Normal, RenderVertexFormat.Float3),
        new RenderVertexAttribute(RenderVertexSemantic.Tangent, RenderVertexFormat.Float4),
        new RenderVertexAttribute(RenderVertexSemantic.TextureCoordinate0, RenderVertexFormat.Float2)
    ]);

    private readonly IRenderDevice m_device;
    private readonly RenderPipelineArtifactRegistry m_artifacts;
    private readonly IRenderDiagnosticSink m_diagnostics;
    private readonly Dictionary<Guid, MeshEntry> m_meshes = [];
    private readonly Dictionary<string, PipelineEntry> m_pipelines = new(StringComparer.Ordinal);
    private readonly Dictionary<RenderTexture, TargetEntry> m_targets =
        new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<RenderTexture> m_pendingTargetReleases =
        new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<string> m_publishedDiagnostics = new(StringComparer.Ordinal);
    private bool m_disposed;

    /// <summary>
    /// Creates a backend-neutral production operation executor.
    /// </summary>
    /// <param name="device">Active render device used only through neutral resource and command contracts.</param>
    /// <param name="artifacts">Target artifact and uploaded texture registry.</param>
    /// <param name="diagnostics">Structured rendering diagnostic sink.</param>
    public DefaultRenderPipelineExecutor(
        IRenderDevice device,
        RenderPipelineArtifactRegistry artifacts,
        IRenderDiagnosticSink diagnostics)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(artifacts);
        ArgumentNullException.ThrowIfNull(diagnostics);
        m_device = device;
        m_artifacts = artifacts;
        m_diagnostics = diagnostics;
    }

    /// <inheritdoc />
    public void PrepareFrame(ulong frameIndex)
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
        _ = frameIndex;
        foreach (RenderTexture target in m_pendingTargetReleases)
        {
            if (m_targets.Remove(target, out TargetEntry? entry))
            {
                m_device.DestroyTexture(entry.texture);
            }
        }

        m_pendingTargetReleases.Clear();
    }

    /// <inheritdoc />
    public RenderTextureHandle ImportTarget(RenderGraphBuilder graph, RenderTexture target)
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(target);
        m_pendingTargetReleases.Remove(target);
        RenderTextureDescriptor descriptor = target.descriptor;
        if ((descriptor.usage & RenderTextureUsage.ColorAttachment) == 0
            || (descriptor.usage & RenderTextureUsage.Sampled) == 0)
        {
            throw new ArgumentException(
                "A camera RenderTexture must support ColorAttachment and Sampled usage.",
                nameof(target));
        }

        if (!m_targets.TryGetValue(target, out TargetEntry? entry)
            || entry.revision != target.contentRevision
            || !entry.descriptor.Equals(descriptor))
        {
            PersistentTextureHandle candidate = m_device.CreateTexture(descriptor, target.name);
            TargetEntry replacement = new(target.contentRevision, descriptor, candidate);
            m_targets[target] = replacement;
            if (entry is not null)
            {
                m_device.DestroyTexture(entry.texture);
            }

            entry = replacement;
        }

        return graph.ImportTexture(target.name, entry.texture, entry.descriptor);
    }

    /// <inheritdoc />
    public bool TryGetTargetTexture(RenderTexture target, out PersistentTextureHandle texture)
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
        ArgumentNullException.ThrowIfNull(target);
        if (!m_pendingTargetReleases.Contains(target)
            && m_targets.TryGetValue(target, out TargetEntry? entry)
            && entry.revision == target.contentRevision
            && entry.descriptor.Equals(target.descriptor))
        {
            texture = entry.texture;
            return true;
        }

        texture = default;
        return false;
    }

    /// <inheritdoc />
    public void ReleaseTarget(RenderTexture target)
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
        ArgumentNullException.ThrowIfNull(target);
        if (m_targets.ContainsKey(target))
        {
            m_pendingTargetReleases.Add(target);
        }
    }

    /// <inheritdoc />
    public void Prepare(RenderPipelineOperation operation)
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
        ArgumentNullException.ThrowIfNull(operation);
        switch (operation.kind)
        {
            case RenderPipelineOperationKind.Scene:
                PrepareScene(operation);
                break;
            case RenderPipelineOperationKind.Fullscreen:
            case RenderPipelineOperationKind.Compute:
                PrepareOperation(operation);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(operation));
        }
    }

    /// <inheritdoc />
    public void Execute(RenderPipelineOperation operation, RenderPassContext context)
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(context);
        switch (operation.kind)
        {
            case RenderPipelineOperationKind.Scene:
                ExecuteScene(operation, context.commands);
                break;
            case RenderPipelineOperationKind.Fullscreen:
                ExecuteFullscreen(operation, context.commands);
                break;
            case RenderPipelineOperationKind.Compute:
                ExecuteCompute(operation, context.commands);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(operation));
        }
    }

    /// <summary>
    /// Releases cached programs and buffers through the device delayed-destruction path.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when called outside a device frame safety point.</exception>
    public void Dispose()
    {
        if (m_disposed)
        {
            return;
        }

        foreach (PipelineEntry pipeline in m_pipelines.Values)
        {
            DestroyPipeline(pipeline);
        }

        foreach (MeshEntry mesh in m_meshes.Values)
        {
            m_device.DestroyBuffer(mesh.vertexBuffer);
            m_device.DestroyBuffer(mesh.indexBuffer);
        }

        foreach (TargetEntry target in m_targets.Values)
        {
            m_device.DestroyTexture(target.texture);
        }

        m_pipelines.Clear();
        m_meshes.Clear();
        m_targets.Clear();
        m_pendingTargetReleases.Clear();
        m_disposed = true;
    }

    private void PrepareScene(RenderPipelineOperation operation)
    {
        foreach (RenderObjectData renderObject in operation.objects)
        {
            PrepareMesh(renderObject.mesh);
            foreach (MaterialAsset material in renderObject.materials)
            {
                if (material.shader is not ShaderAsset shader)
                {
                    PublishOnce(
                        "RENDER_MATERIAL_SHADER_MISSING",
                        $"Material '{material.name}' has no shader and will be skipped.",
                        material.identity.persistentId.ToString());
                    continue;
                }

                if (!m_artifacts.TryGetShader(shader, material.keywords, out RenderPipelineArtifactRegistry.ShaderCandidate candidate))
                {
                    PublishOnce(
                        "RENDER_SHADER_ARTIFACT_MISSING",
                        $"Shader '{shader.name}' has no last-good artifact for the material keyword selection.",
                        shader.identity.persistentId.ToString());
                    continue;
                }

                CompiledShaderPass? pass = FindScenePass(candidate.artifact, operation.shaderPassTag!);
                if (pass is null)
                {
                    PublishOnce(
                        "RENDER_SHADER_PASS_MISSING",
                        $"Shader '{shader.name}' has no pass tagged '{operation.shaderPassTag}'.",
                        shader.identity.persistentId.ToString());
                    continue;
                }

                EnsurePipeline(
                    ScenePipelineKey(shader, material, operation.shaderPassTag!),
                    candidate.artifact,
                    pass,
                    proceduralVertices: false,
                    shader.definition);
            }
        }
    }

    private void PrepareOperation(RenderPipelineOperation operation)
    {
        if (!m_artifacts.TryGetOperation(operation.id, out RenderPipelineArtifactRegistry.OperationCandidate candidate))
        {
            PublishOnce(
                "RENDER_OPERATION_ARTIFACT_MISSING",
                $"Pipeline operation '{operation.id}' has no installed last-good shader artifact.",
                operation.id);
            return;
        }

        CompiledShaderPass pass = candidate.artifact.passes.First(value =>
            string.Equals(value.definition.name, candidate.passName, StringComparison.Ordinal));
        bool computePass = pass.stages.Any(static stage => stage.stage == ShaderStage.Compute);
        if (computePass != (operation.kind == RenderPipelineOperationKind.Compute))
        {
            PublishOnce(
                "RENDER_OPERATION_STAGE_MISMATCH",
                $"Pipeline operation '{operation.id}' selected an incompatible shader pass.",
                operation.id);
            return;
        }

        EnsurePipeline(
            OperationPipelineKey(operation.id),
            candidate.artifact,
            pass,
            proceduralVertices: operation.kind == RenderPipelineOperationKind.Fullscreen,
            definition: null);
    }

    private static CompiledShaderPass? FindScenePass(
        CompiledShaderArtifact artifact,
        string requestedTag)
    {
        CompiledShaderPass? pass = artifact.passes.FirstOrDefault(value =>
            string.Equals(value.definition.tag, requestedTag, StringComparison.Ordinal));
        if (pass is null
            && string.Equals(
                requestedTag,
                BuiltinShaderPassTags.ForwardLitClustered,
                StringComparison.Ordinal))
        {
            pass = artifact.passes.FirstOrDefault(value => string.Equals(
                value.definition.tag,
                BuiltinShaderPassTags.ForwardLit,
                StringComparison.Ordinal));
        }

        return pass;
    }

    private void PrepareMesh(MeshAsset mesh)
    {
        Guid persistentId = mesh.identity.persistentId;
        if (persistentId == Guid.Empty)
        {
            PublishOnce(
                "RENDER_MESH_IDENTITY_MISSING",
                $"Mesh '{mesh.name}' has no persistent identity and cannot enter the GPU cache.",
                mesh.name);
            return;
        }

        if (m_meshes.TryGetValue(persistentId, out MeshEntry? active)
            && active.contentVersion == mesh.contentVersion)
        {
            return;
        }

        PersistentBufferHandle vertexBuffer = default;
        PersistentBufferHandle indexBuffer = default;
        try
        {
            MeshData data = MeshAssetRuntime.GetMeshData(mesh);
            byte[] vertices = EncodeVertices(data.vertices);
            byte[] indices = EncodeIndices(data.indices);
            vertexBuffer = m_device.CreateBuffer(
                new PersistentBufferDescriptor(
                    new RenderBufferDescriptor(data.vertices.Count, S_MESH_LAYOUT.stride, RenderBufferUsage.Vertex),
                    S_MESH_LAYOUT),
                vertices,
                $"{mesh.name}.Vertices");
            indexBuffer = m_device.CreateBuffer(
                new PersistentBufferDescriptor(
                    new RenderBufferDescriptor(data.indices.Count, sizeof(uint), RenderBufferUsage.Index),
                    indexFormat: RenderIndexFormat.UInt32),
                indices,
                $"{mesh.name}.Indices");

            MeshEntry candidate = new(
                mesh.contentVersion,
                vertexBuffer,
                indexBuffer,
                data.subMeshes.ToArray());
            m_meshes[persistentId] = candidate;
            if (active is not null)
            {
                m_device.DestroyBuffer(active.vertexBuffer);
                m_device.DestroyBuffer(active.indexBuffer);
            }
        }
        catch (Exception exception)
        {
            if (vertexBuffer.isValid)
            {
                m_device.DestroyBuffer(vertexBuffer);
            }

            if (indexBuffer.isValid)
            {
                m_device.DestroyBuffer(indexBuffer);
            }

            PublishOnce(
                "RENDER_MESH_UPLOAD_FAILED",
                $"Mesh '{mesh.name}' kept its previous GPU resource because upload failed: {exception.Message}",
                persistentId.ToString());
        }
    }

    private void EnsurePipeline(
        string key,
        CompiledShaderArtifact artifact,
        CompiledShaderPass pass,
        bool proceduralVertices,
        ShaderDefinition? definition)
    {
        m_pipelines.TryGetValue(key, out PipelineEntry? active);
        if (active is not null
            && ReferenceEquals(active.artifact, artifact)
            && string.Equals(active.pass.definition.name, pass.definition.name, StringComparison.Ordinal))
        {
            return;
        }

        try
        {
            PipelineEntry candidate = CreatePipeline(artifact, pass, proceduralVertices, definition);
            m_pipelines[key] = candidate;
            if (active is not null)
            {
                DestroyPipeline(active);
            }
        }
        catch (Exception exception)
        {
            PublishOnce(
                "RENDER_PIPELINE_CREATE_FAILED",
                $"Shader '{artifact.shaderName}' pass '{pass.definition.name}' kept its last-good program: "
                    + exception.Message,
                key);
        }
    }

    private PipelineEntry CreatePipeline(
        CompiledShaderArtifact artifact,
        CompiledShaderPass pass,
        bool proceduralVertices,
        ShaderDefinition? definition)
    {
        ShaderStage stageMask = pass.stages.Aggregate(
            ShaderStage.None,
            static (current, stage) => current | stage.stage);
        ShaderInterfaceBinding[] sourceBindings = pass.shaderInterface.bindings
            .Where(binding => (binding.stages & stageMask) != 0)
            .ToArray();
        bool compute = (stageMask & ShaderStage.Compute) != 0;
        RenderShaderBindingDescriptor[] bindings = CreateBindings(sourceBindings, compute);
        if (compute)
        {
            ShaderStageArtifact stage = RequireStage(pass, ShaderStage.Compute);
            ComputePipelineHandle pipeline = m_device.CreateComputePipeline(
                new ComputePipelineDescriptor(stage.bytes.Span, bindings),
                $"{artifact.shaderName}/{pass.definition.name}");
            return PipelineEntry.ForCompute(
                artifact,
                pass,
                pipeline,
                bindings,
                sourceBindings,
                definition);
        }

        ShaderStageArtifact vertex = RequireStage(pass, ShaderStage.Vertex);
        ShaderStageArtifact fragment = RequireStage(pass, ShaderStage.Fragment);
        GraphicsPipelineHandle graphics = m_device.CreateGraphicsPipeline(
            new GraphicsPipelineDescriptor(
                vertex.bytes.Span,
                fragment.bytes.Span,
                bindings,
                proceduralVertices ? null : S_MESH_LAYOUT,
                ToRasterState(pass.definition.renderState)),
            $"{artifact.shaderName}/{pass.definition.name}");
        return PipelineEntry.ForGraphics(
            artifact,
            pass,
            graphics,
            bindings,
            sourceBindings,
            definition);
    }

    private void ExecuteScene(RenderPipelineOperation operation, RenderCommandEncoder commands)
    {
        foreach (RenderObjectData renderObject in operation.objects)
        {
            if (!m_meshes.TryGetValue(renderObject.mesh.identity.persistentId, out MeshEntry? mesh))
            {
                continue;
            }

            for (int subMeshIndex = 0; subMeshIndex < mesh.subMeshes.Count; subMeshIndex++)
            {
                MaterialAsset material = renderObject.materials[Math.Min(subMeshIndex, renderObject.materials.Count - 1)];
                if (material.shader is not ShaderAsset shader
                    || !m_pipelines.TryGetValue(
                        ScenePipelineKey(shader, material, operation.shaderPassTag!),
                        out PipelineEntry? pipeline)
                    || !pipeline.graphics.isValid)
                {
                    continue;
                }

                try
                {
                    commands.BindGraphicsPipeline(pipeline.graphics);
                    BindOperationResources(operation, pipeline, commands);
                    BindBuiltins(operation, pipeline, commands);
                    BindMaterial(material, renderObject.propertyBlock, pipeline, commands);
                    BindObjectId(renderObject.persistentId, pipeline, commands);
                    commands.SetTransform(renderObject.localToWorld.ToColumnMajorArray());
                    commands.BindVertexBuffer(mesh.vertexBuffer);
                    MeshSubMesh subMesh = mesh.subMeshes[subMeshIndex];
                    commands.BindIndexBuffer(mesh.indexBuffer, subMesh.firstIndex);
                    commands.DrawIndexed(subMesh.indexCount);
                }
                catch (Exception exception)
                {
                    PublishOnce(
                        "RENDER_DRAW_FAILED",
                        $"Renderer '{renderObject.persistentId}' skipped one submesh draw: {exception.Message}",
                        renderObject.persistentId.ToString());
                }
            }
        }
    }

    private void ExecuteFullscreen(RenderPipelineOperation operation, RenderCommandEncoder commands)
    {
        if (!m_pipelines.TryGetValue(OperationPipelineKey(operation.id), out PipelineEntry? pipeline)
            || !pipeline.graphics.isValid)
        {
            return;
        }

        commands.BindGraphicsPipeline(pipeline.graphics);
        BindOperationResources(operation, pipeline, commands);
        BindBuiltins(operation, pipeline, commands);
        commands.Draw(3);
    }

    private void ExecuteCompute(RenderPipelineOperation operation, RenderCommandEncoder commands)
    {
        if (!m_pipelines.TryGetValue(OperationPipelineKey(operation.id), out PipelineEntry? pipeline)
            || !pipeline.compute.isValid)
        {
            return;
        }

        commands.BindComputePipeline(pipeline.compute);
        BindOperationResources(operation, pipeline, commands);
        BindBuiltins(operation, pipeline, commands);
        commands.Dispatch(operation.dispatchX, operation.dispatchY, operation.dispatchZ);
    }

    private void BindOperationResources(
        RenderPipelineOperation operation,
        PipelineEntry pipeline,
        RenderCommandEncoder commands)
    {
        foreach (RenderTextureBinding binding in operation.textures)
        {
            if (pipeline.bindings.TryGetValue(binding.binding.value, out RenderShaderBindingDescriptor? descriptor)
                && descriptor.kind == RenderShaderBindingKind.Texture)
            {
                commands.BindTexture(binding.binding, binding.texture);
            }
        }

        foreach (RenderBufferBinding binding in operation.buffers)
        {
            if (pipeline.bindings.TryGetValue(binding.binding.value, out RenderShaderBindingDescriptor? descriptor)
                && descriptor.kind == RenderShaderBindingKind.StorageBuffer)
            {
                commands.BindBuffer(binding.binding, binding.buffer);
            }
        }

        foreach (RenderUniformBinding binding in operation.uniforms)
        {
            if (pipeline.bindings.TryGetValue(binding.binding.value, out RenderShaderBindingDescriptor? descriptor)
                && descriptor.kind == RenderShaderBindingKind.Uniform)
            {
                commands.SetUniform(binding.binding, MemoryMarshal.AsBytes(binding.values.Span));
            }
        }
    }

    private void BindBuiltins(
        RenderPipelineOperation operation,
        PipelineEntry pipeline,
        RenderCommandEncoder commands)
    {
        SetVectorIfDeclared(
            pipeline,
            commands,
            S_CAMERA_POSITION,
            operation.view.worldPosition.x,
            operation.view.worldPosition.y,
            operation.view.worldPosition.z,
            1f);
        SetVectorIfDeclared(
            pipeline,
            commands,
            S_VIEW_PARAMETERS,
            operation.view.pixelWidth,
            operation.view.pixelHeight,
            m_device.capabilities.homogeneousDepth ? 1f : 0f,
            m_device.capabilities.originBottomLeft ? 1f : 0f);
        SetVectorIfDeclared(pipeline, commands, S_EXPOSURE, operation.scalarParameter, 0f, 0f, 0f);
        RenderLightData? mainLight = operation.lights.FirstOrDefault(
            static light => light.kind == RenderLightKind.Directional);
        SetVectorIfDeclared(
            pipeline,
            commands,
            S_MAIN_LIGHT_DIRECTION,
            mainLight?.direction.x ?? 0f,
            mainLight?.direction.y ?? 0f,
            mainLight?.direction.z ?? 0f,
            0f);
        SetVectorIfDeclared(
            pipeline,
            commands,
            S_MAIN_LIGHT_COLOR,
            (mainLight?.color.r ?? 0f) * (mainLight?.intensity ?? 0f),
            (mainLight?.color.g ?? 0f) * (mainLight?.intensity ?? 0f),
            (mainLight?.color.b ?? 0f) * (mainLight?.intensity ?? 0f),
            mainLight?.shadowStrength ?? 0f);

        RenderLightData[] localLights = operation.lights
            .Where(static light => light.kind != RenderLightKind.Directional)
            .Take(C_MAX_LOCAL_LIGHTS)
            .ToArray();
        SetVectorIfDeclared(
            pipeline,
            commands,
            S_LIGHT_COUNT,
            localLights.Length,
            mainLight is null ? 0f : 1f,
            operation.lights.Count,
            C_MAX_LOCAL_LIGHTS);
        for (int index = 0; index < C_MAX_LOCAL_LIGHTS; index++)
        {
            RenderLightData? localLight = index < localLights.Length ? localLights[index] : null;
            SetVectorIfDeclared(
                pipeline,
                commands,
                S_LOCAL_LIGHT_POSITION_RANGE[index],
                localLight?.position.x ?? 0f,
                localLight?.position.y ?? 0f,
                localLight?.position.z ?? 0f,
                localLight?.range ?? 0f);
            SetVectorIfDeclared(
                pipeline,
                commands,
                S_LOCAL_LIGHT_DIRECTION_OUTER[index],
                localLight?.direction.x ?? 0f,
                localLight?.direction.y ?? 0f,
                localLight?.direction.z ?? 0f,
                localLight?.outerConeCosine ?? 1f);
            SetVectorIfDeclared(
                pipeline,
                commands,
                S_LOCAL_LIGHT_COLOR_INNER[index],
                (localLight?.color.r ?? 0f) * (localLight?.intensity ?? 0f),
                (localLight?.color.g ?? 0f) * (localLight?.intensity ?? 0f),
                (localLight?.color.b ?? 0f) * (localLight?.intensity ?? 0f),
                localLight?.kind == RenderLightKind.Spot
                    ? localLight.innerConeCosine
                    : -1f);
        }

        DirectionalShadowData? shadow = operation.directionalShadow;
        float finalSplit = shadow?.cascadeSplits[^1] ?? 0f;
        SetVectorIfDeclared(
            pipeline,
            commands,
            S_SHADOW_CASCADE_SPLITS,
            shadow?.cascadeSplits.ElementAtOrDefault(0) ?? finalSplit,
            shadow?.cascadeSplits.ElementAtOrDefault(1) ?? finalSplit,
            shadow?.cascadeSplits.ElementAtOrDefault(2) ?? finalSplit,
            shadow?.cascadeSplits.ElementAtOrDefault(3) ?? finalSplit);
        SetVectorIfDeclared(
            pipeline,
            commands,
            S_SHADOW_PARAMETERS,
            shadow?.cascadeCount ?? 0f,
            shadow?.strength ?? 0f,
            shadow?.depthBias ?? 0f,
            shadow?.texelSize ?? 0f);
        for (int index = 0; index < S_SHADOW_MATRICES.Length; index++)
        {
            Matrix matrix = shadow is not null && index < shadow.cascadeCount
                ? shadow.worldToShadowMatrices[index]
                : Matrix.identity;
            SetMatrixIfDeclared(pipeline, commands, S_SHADOW_MATRICES[index], matrix);
        }

    }

    private static RenderBindingId[] CreateIndexedBindings(string prefix)
    {
        int count = string.Equals(prefix, "inno_shadow_matrix", StringComparison.Ordinal)
            ? 4
            : C_MAX_LOCAL_LIGHTS;
        var bindings = new RenderBindingId[count];
        for (int index = 0; index < bindings.Length; index++)
        {
            bindings[index] = new RenderBindingId($"{prefix}_{index}");
        }

        return bindings;
    }

    private static void SetMatrixIfDeclared(
        PipelineEntry pipeline,
        RenderCommandEncoder commands,
        RenderBindingId binding,
        Matrix matrix)
    {
        if (!pipeline.bindings.TryGetValue(binding.value, out RenderShaderBindingDescriptor? descriptor)
            || descriptor.kind != RenderShaderBindingKind.Uniform
            || descriptor.uniformType != RenderUniformType.Matrix4x4
            || descriptor.count != 1)
        {
            return;
        }

        float[] values = matrix.ToColumnMajorArray();
        commands.SetUniform(binding, MemoryMarshal.AsBytes(values.AsSpan()));
    }

    private void BindMaterial(
        MaterialAsset material,
        MaterialPropertyBlock? overrides,
        PipelineEntry pipeline,
        RenderCommandEncoder commands)
    {
        foreach (ShaderInterfaceBinding sourceBinding in pipeline.sourceBindings)
        {
            if (sourceBinding.id.value.StartsWith("inno_", StringComparison.Ordinal))
            {
                continue;
            }

            if (!pipeline.bindings.TryGetValue(
                    sourceBinding.id.value,
                    out RenderShaderBindingDescriptor? descriptor))
            {
                continue;
            }

            MaterialValue value;
            bool hasValue = overrides?.TryGet(sourceBinding.id, out value) == true
                || material.TryGet(sourceBinding.id, out value)
                || TryGetDefault(pipeline.definition, sourceBinding.id, out value);
            if (!hasValue)
            {
                continue;
            }

            if (descriptor.kind == RenderShaderBindingKind.Uniform)
            {
                SetMaterialUniform(commands, sourceBinding.id, value);
            }
            else if (descriptor.kind == RenderShaderBindingKind.Texture
                && value.texture is TextureAsset texture)
            {
                if (m_artifacts.TryGetTexture(texture, out RenderPipelineArtifactRegistry.TextureCandidate candidate))
                {
                    commands.BindTexture(new RenderBindingId(sourceBinding.id.value), candidate.handle);
                }
                else
                {
                    PublishOnce(
                        "RENDER_TEXTURE_NOT_RESIDENT",
                        $"Texture '{texture.name}' has no uploaded target resource; the draw keeps its last-good bindings.",
                        texture.identity.persistentId.ToString());
                }
            }
        }
    }

    private static void BindObjectId(
        Guid persistentId,
        PipelineEntry pipeline,
        RenderCommandEncoder commands)
    {
        Vector4 encoded = RenderPicking.EncodeObjectId(persistentId);
        SetVectorIfDeclared(
            pipeline,
            commands,
            S_OBJECT_ID,
            encoded.x,
            encoded.y,
            encoded.z,
            encoded.w);
    }

    private static void SetMaterialUniform(
        RenderCommandEncoder commands,
        ShaderPropertyId property,
        MaterialValue value)
    {
        RenderBindingId binding = new(property.value);
        if (value.kind == MaterialValueKind.Matrix)
        {
            float[] matrix = value.matrix.ToColumnMajorArray();
            commands.SetUniform(binding, MemoryMarshal.AsBytes(matrix.AsSpan()));
            return;
        }

        Span<float> vector = stackalloc float[4]
        {
            value.vector.x,
            value.vector.y,
            value.vector.z,
            value.vector.w
        };
        commands.SetUniform(binding, MemoryMarshal.AsBytes(vector));
    }

    private static void SetVectorIfDeclared(
        PipelineEntry pipeline,
        RenderCommandEncoder commands,
        RenderBindingId binding,
        float x,
        float y,
        float z,
        float w)
    {
        if (!pipeline.bindings.TryGetValue(binding.value, out RenderShaderBindingDescriptor? descriptor)
            || descriptor.kind != RenderShaderBindingKind.Uniform
            || descriptor.uniformType != RenderUniformType.Vector4
            || descriptor.count != 1)
        {
            return;
        }

        Span<float> value = stackalloc float[4] { x, y, z, w };
        commands.SetUniform(binding, MemoryMarshal.AsBytes(value));
    }

    private static RenderShaderBindingDescriptor[] CreateBindings(
        IReadOnlyList<ShaderInterfaceBinding> sourceBindings,
        bool compute)
    {
        List<RenderShaderBindingDescriptor> result = [];
        int resourceSlot = 0;
        foreach (ShaderInterfaceBinding binding in sourceBindings)
        {
            RenderBindingId id = new(binding.id.value);
            switch (binding.type)
            {
                case ShaderPropertyType.Texture2D:
                case ShaderPropertyType.Texture2DArray:
                case ShaderPropertyType.TextureCube:
                case ShaderPropertyType.Sampler:
                    result.Add(new RenderShaderBindingDescriptor(
                        id,
                        RenderShaderBindingKind.Texture,
                        resourceSlot++,
                        count: binding.arrayCount));
                    break;
                case ShaderPropertyType.Buffer:
                    result.Add(new RenderShaderBindingDescriptor(
                        id,
                        RenderShaderBindingKind.StorageBuffer,
                        resourceSlot++,
                        count: binding.arrayCount,
                        bufferAccess: compute
                            ? RenderBufferBindingAccess.ReadWrite
                            : RenderBufferBindingAccess.Read));
                    break;
                case ShaderPropertyType.Matrix4x4:
                    result.Add(new RenderShaderBindingDescriptor(
                        id,
                        RenderShaderBindingKind.Uniform,
                        uniformType: RenderUniformType.Matrix4x4,
                        count: binding.arrayCount));
                    break;
                default:
                    result.Add(new RenderShaderBindingDescriptor(
                        id,
                        RenderShaderBindingKind.Uniform,
                        uniformType: RenderUniformType.Vector4,
                        count: binding.arrayCount));
                    break;
            }
        }

        return result.ToArray();
    }

    private static RenderRasterState ToRasterState(ShaderRenderState state)
        => new()
        {
            cull = state.cull switch
            {
                ShaderCullMode.None => RenderCullMode.None,
                ShaderCullMode.Front => RenderCullMode.Front,
                ShaderCullMode.Back => RenderCullMode.Back,
                _ => throw new ArgumentOutOfRangeException(nameof(state))
            },
            depthCompare = state.depthCompare switch
            {
                ShaderCompareFunction.Never => RenderDepthCompare.Never,
                ShaderCompareFunction.Less => RenderDepthCompare.Less,
                ShaderCompareFunction.Equal => RenderDepthCompare.Equal,
                ShaderCompareFunction.LessEqual => RenderDepthCompare.LessEqual,
                ShaderCompareFunction.Greater => RenderDepthCompare.Greater,
                ShaderCompareFunction.NotEqual => RenderDepthCompare.NotEqual,
                ShaderCompareFunction.GreaterEqual => RenderDepthCompare.GreaterEqual,
                ShaderCompareFunction.Always => RenderDepthCompare.Always,
                _ => throw new ArgumentOutOfRangeException(nameof(state))
            },
            depthWrite = state.depthWrite,
            blend = state.blend switch
            {
                ShaderBlendMode.Opaque => RenderBlendMode.Opaque,
                ShaderBlendMode.Alpha => RenderBlendMode.Alpha,
                ShaderBlendMode.Additive => RenderBlendMode.Additive,
                ShaderBlendMode.Premultiplied => RenderBlendMode.Premultiplied,
                _ => throw new ArgumentOutOfRangeException(nameof(state))
            },
            colorWriteMask = state.colorWriteMask
        };

    private static ShaderStageArtifact RequireStage(CompiledShaderPass pass, ShaderStage stage)
        => pass.stages.SingleOrDefault(value => value.stage == stage)
            ?? throw new InvalidOperationException(
                $"Pass '{pass.definition.name}' requires exactly one '{stage}' stage.");

    private static bool TryGetDefault(
        ShaderDefinition? definition,
        ShaderPropertyId id,
        out MaterialValue value)
    {
        ShaderPropertyDefinition? property = definition?.properties.FirstOrDefault(candidate => candidate.id == id);
        if (property is null || property.type is ShaderPropertyType.Texture2D
            or ShaderPropertyType.Texture2DArray
            or ShaderPropertyType.TextureCube
            or ShaderPropertyType.Sampler
            or ShaderPropertyType.Buffer)
        {
            value = default;
            return false;
        }

        using JsonDocument document = JsonDocument.Parse(property.defaultValueJson);
        JsonElement root = document.RootElement;
        if (property.type == ShaderPropertyType.Float)
        {
            value = MaterialValue.FromFloat(root.GetSingle());
            return true;
        }

        float[] components = root.EnumerateArray().Select(static element => element.GetSingle()).ToArray();
        if (property.type == ShaderPropertyType.Matrix4x4 && components.Length == 16)
        {
            value = MaterialValue.FromMatrix(new Matrix(
                components[0], components[1], components[2], components[3],
                components[4], components[5], components[6], components[7],
                components[8], components[9], components[10], components[11],
                components[12], components[13], components[14], components[15]));
            return true;
        }

        if (components.Length is < 2 or > 4)
        {
            value = default;
            return false;
        }

        Vector4 vector = new(
            components[0],
            components[1],
            components.Length > 2 ? components[2] : 0f,
            components.Length > 3 ? components[3] : 0f);
        value = property.type == ShaderPropertyType.Color
            ? MaterialValue.FromColor(new Color(vector.x, vector.y, vector.z, vector.w))
            : MaterialValue.FromVector(vector);
        return true;
    }

    private static byte[] EncodeVertices(IReadOnlyList<MeshVertex> vertices)
    {
        float[] values = new float[checked(vertices.Count * 12)];
        for (int index = 0; index < vertices.Count; index++)
        {
            MeshVertex vertex = vertices[index];
            int offset = index * 12;
            values[offset] = vertex.position.x;
            values[offset + 1] = vertex.position.y;
            values[offset + 2] = vertex.position.z;
            values[offset + 3] = vertex.normal.x;
            values[offset + 4] = vertex.normal.y;
            values[offset + 5] = vertex.normal.z;
            values[offset + 6] = vertex.tangent.x;
            values[offset + 7] = vertex.tangent.y;
            values[offset + 8] = vertex.tangent.z;
            values[offset + 9] = vertex.tangent.w;
            values[offset + 10] = vertex.textureCoordinate.x;
            values[offset + 11] = vertex.textureCoordinate.y;
        }

        return MemoryMarshal.AsBytes(values.AsSpan()).ToArray();
    }

    private static byte[] EncodeIndices(IReadOnlyList<uint> indices)
    {
        uint[] values = indices.ToArray();
        return MemoryMarshal.AsBytes(values.AsSpan()).ToArray();
    }

    private void DestroyPipeline(PipelineEntry pipeline)
    {
        if (pipeline.graphics.isValid)
        {
            m_device.DestroyGraphicsPipeline(pipeline.graphics);
        }

        if (pipeline.compute.isValid)
        {
            m_device.DestroyComputePipeline(pipeline.compute);
        }
    }

    private void PublishOnce(string code, string message, string sourceId)
    {
        string key = $"{code}:{sourceId}:{message}";
        if (m_publishedDiagnostics.Add(key))
        {
            m_diagnostics.Publish(new RenderDiagnostic(
                code,
                message,
                RenderDiagnosticSeverity.Error,
                sourceId));
        }
    }

    private static string ScenePipelineKey(
        ShaderAsset shader,
        MaterialAsset material,
        string passTag)
        => $"scene:{shader.identity.persistentId:N}:{KeywordKey(material.keywords)}:{passTag}";

    private static string OperationPipelineKey(string operationId) => $"operation:{operationId}";

    private static string KeywordKey(IReadOnlySet<string> keywords)
        => string.Join(";", keywords.OrderBy(static value => value, StringComparer.Ordinal));

    private sealed record MeshEntry(
        long contentVersion,
        PersistentBufferHandle vertexBuffer,
        PersistentBufferHandle indexBuffer,
        IReadOnlyList<MeshSubMesh> subMeshes);

    private sealed record TargetEntry(
        long revision,
        RenderTextureDescriptor descriptor,
        PersistentTextureHandle texture);

    private sealed class PipelineEntry
    {
        private PipelineEntry(
            CompiledShaderArtifact artifact,
            CompiledShaderPass pass,
            GraphicsPipelineHandle graphics,
            ComputePipelineHandle compute,
            IReadOnlyList<RenderShaderBindingDescriptor> bindings,
            IReadOnlyList<ShaderInterfaceBinding> sourceBindings,
            ShaderDefinition? definition)
        {
            this.artifact = artifact;
            this.pass = pass;
            this.graphics = graphics;
            this.compute = compute;
            this.bindings = bindings.ToDictionary(static value => value.id.value, StringComparer.Ordinal);
            this.sourceBindings = sourceBindings;
            this.definition = definition;
        }

        public CompiledShaderArtifact artifact { get; }
        public CompiledShaderPass pass { get; }
        public GraphicsPipelineHandle graphics { get; }
        public ComputePipelineHandle compute { get; }
        public IReadOnlyDictionary<string, RenderShaderBindingDescriptor> bindings { get; }
        public IReadOnlyList<ShaderInterfaceBinding> sourceBindings { get; }
        public ShaderDefinition? definition { get; }

        public static PipelineEntry ForGraphics(
            CompiledShaderArtifact artifact,
            CompiledShaderPass pass,
            GraphicsPipelineHandle graphics,
            IReadOnlyList<RenderShaderBindingDescriptor> bindings,
            IReadOnlyList<ShaderInterfaceBinding> sourceBindings,
            ShaderDefinition? definition)
            => new(artifact, pass, graphics, default, bindings, sourceBindings, definition);

        public static PipelineEntry ForCompute(
            CompiledShaderArtifact artifact,
            CompiledShaderPass pass,
            ComputePipelineHandle compute,
            IReadOnlyList<RenderShaderBindingDescriptor> bindings,
            IReadOnlyList<ShaderInterfaceBinding> sourceBindings,
            ShaderDefinition? definition)
            => new(artifact, pass, default, compute, bindings, sourceBindings, definition);
    }
}
