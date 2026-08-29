using System;
using System.Collections.Generic;

namespace Inno.Rendering.Core;

internal static class RenderGraphCompiler
{
    public static RenderGraphCompileResult Compile(
        uint generation,
        GraphicsCapabilities capabilities,
        IReadOnlyList<RenderTextureRecord> textures,
        IReadOnlyList<RenderBufferRecord> buffers,
        IReadOnlyList<RenderPassRecord> passes,
        IReadOnlySet<RenderResourceKey> outputs)
    {
        List<RenderGraphDiagnostic> diagnostics = [];
        ValidateResources(capabilities, textures, buffers, diagnostics);
        ValidatePasses(capabilities, textures, buffers, passes, diagnostics);

        List<HashSet<int>> dependencies = CreateEdgeSets(passes.Count);
        List<HashSet<int>> dataDependencies = CreateEdgeSets(passes.Count);
        BuildResourceDependencies(
            textures,
            buffers,
            passes,
            outputs,
            dependencies,
            dataDependencies,
            diagnostics);
        BuildPhaseDependencies(passes, dependencies);

        if (ContainsErrors(diagnostics))
        {
            return new RenderGraphCompileResult(null, diagnostics);
        }

        HashSet<int> livePasses = FindLivePasses(textures, buffers, passes, outputs, dataDependencies);
        List<int>? schedule = TopologicalSort(passes.Count, livePasses, dependencies);
        if (schedule is null)
        {
            diagnostics.Add(new RenderGraphDiagnostic(
                "RENDER_GRAPH_CYCLE",
                "Pass ordering and resource dependencies contain a cycle.",
                RenderGraphDiagnosticSeverity.Error));
            return new RenderGraphCompileResult(null, diagnostics);
        }

        if (schedule.Count > capabilities.limits.maxViews)
        {
            diagnostics.Add(new RenderGraphDiagnostic(
                "RENDER_GRAPH_VIEW_LIMIT",
                $"Graph requires {schedule.Count} views but the device supports {capabilities.limits.maxViews}.",
                RenderGraphDiagnosticSeverity.Error));
            return new RenderGraphCompileResult(null, diagnostics);
        }

        Dictionary<int, int> schedulePositions = [];
        for (int index = 0; index < schedule.Count; index++)
        {
            schedulePositions[schedule[index]] = index;
        }

        int[] textureSlots = AllocateTextureSlots(textures, passes, schedulePositions);
        int[] bufferSlots = AllocateBufferSlots(buffers, passes, schedulePositions);
        return new RenderGraphCompileResult(
            BuildCompiledGraph(generation, textures, buffers, passes, schedule, textureSlots, bufferSlots),
            diagnostics,
            passes.Count - livePasses.Count);
    }

    private static void ValidateResources(
        GraphicsCapabilities capabilities,
        IReadOnlyList<RenderTextureRecord> textures,
        IReadOnlyList<RenderBufferRecord> buffers,
        List<RenderGraphDiagnostic> diagnostics)
    {
        foreach (RenderTextureRecord texture in textures)
        {
            RenderTextureDescriptor descriptor = texture.descriptor;
            if (descriptor.width > capabilities.limits.maxTextureSize
                || descriptor.height > capabilities.limits.maxTextureSize
                || descriptor.depth > capabilities.limits.maxTextureSize)
            {
                diagnostics.Add(new RenderGraphDiagnostic(
                    "RENDER_GRAPH_TEXTURE_LIMIT",
                    $"Texture '{texture.name}' exceeds the device texture extent limit.",
                    RenderGraphDiagnosticSeverity.Error,
                    resourceName: texture.name));
            }

            if (descriptor.dimension == RenderTextureDimension.Texture2D
                && descriptor.arrayLayers > 1
                && !capabilities.Supports(GraphicsFeature.Texture2DArray))
            {
                diagnostics.Add(new RenderGraphDiagnostic(
                    "RENDER_GRAPH_TEXTURE_ARRAY_UNSUPPORTED",
                    $"Texture '{texture.name}' requires two-dimensional texture-array capability.",
                    RenderGraphDiagnosticSeverity.Error,
                    resourceName: texture.name));
            }

            if (descriptor.dimension == RenderTextureDimension.Texture3D
                && !capabilities.Supports(GraphicsFeature.Texture3D))
            {
                diagnostics.Add(new RenderGraphDiagnostic(
                    "RENDER_GRAPH_TEXTURE_3D_UNSUPPORTED",
                    $"Texture '{texture.name}' requires three-dimensional texture capability.",
                    RenderGraphDiagnosticSeverity.Error,
                    resourceName: texture.name));
            }

            if (descriptor.dimension == RenderTextureDimension.Cube
                && descriptor.arrayLayers > 1
                && !capabilities.Supports(GraphicsFeature.TextureCubeArray))
            {
                diagnostics.Add(new RenderGraphDiagnostic(
                    "RENDER_GRAPH_TEXTURE_CUBE_ARRAY_UNSUPPORTED",
                    $"Texture '{texture.name}' requires cubemap-array capability.",
                    RenderGraphDiagnosticSeverity.Error,
                    resourceName: texture.name));
            }

            bool attachmentUsage = (descriptor.usage
                & (RenderTextureUsage.ColorAttachment | RenderTextureUsage.DepthStencilAttachment)) != 0;
            if ((descriptor.usage & RenderTextureUsage.Sampled) != 0
                && !capabilities.SupportsSampled(descriptor.format, descriptor.dimension))
            {
                diagnostics.Add(new RenderGraphDiagnostic(
                    "RENDER_GRAPH_FORMAT_SAMPLED_UNSUPPORTED",
                    $"Texture format '{descriptor.format}' is not supported for sampling.",
                    RenderGraphDiagnosticSeverity.Error,
                    resourceName: texture.name));
            }

            if (attachmentUsage && !capabilities.SupportsRenderTarget(descriptor.format))
            {
                diagnostics.Add(new RenderGraphDiagnostic(
                    "RENDER_GRAPH_FORMAT_ATTACHMENT_UNSUPPORTED",
                    $"Texture format '{descriptor.format}' is not supported as an attachment.",
                    RenderGraphDiagnosticSeverity.Error,
                    resourceName: texture.name));
            }

            if (attachmentUsage
                && descriptor.sampleCount > 1
                && !capabilities.SupportsMultisampleRenderTarget(descriptor.format))
            {
                diagnostics.Add(new RenderGraphDiagnostic(
                    "RENDER_GRAPH_FORMAT_MSAA_UNSUPPORTED",
                    $"Texture format '{descriptor.format}' is not supported as a multisampled attachment.",
                    RenderGraphDiagnosticSeverity.Error,
                    resourceName: texture.name));
            }

            if ((descriptor.usage & RenderTextureUsage.Storage) != 0
                && (!capabilities.Supports(GraphicsFeature.Compute)
                    || !capabilities.Supports(GraphicsFeature.StorageTexture)
                    || (!capabilities.SupportsStorage(descriptor.format, RenderStorageAccess.Read)
                        && !capabilities.SupportsStorage(descriptor.format, RenderStorageAccess.Write))))
            {
                diagnostics.Add(new RenderGraphDiagnostic(
                    "RENDER_GRAPH_FORMAT_STORAGE_UNSUPPORTED",
                    $"Texture '{texture.name}' cannot be used for unordered shader access on this device.",
                    RenderGraphDiagnosticSeverity.Error,
                    resourceName: texture.name));
            }
        }

        foreach (RenderBufferRecord buffer in buffers)
        {
            if ((buffer.descriptor.usage & RenderBufferUsage.Storage) != 0
                && !capabilities.Supports(GraphicsFeature.StorageBuffer))
            {
                diagnostics.Add(new RenderGraphDiagnostic(
                    "RENDER_GRAPH_STORAGE_BUFFER_UNSUPPORTED",
                    $"Buffer '{buffer.name}' requires storage-buffer capability.",
                    RenderGraphDiagnosticSeverity.Error,
                    resourceName: buffer.name));
            }


            if ((buffer.descriptor.usage & RenderBufferUsage.Index) != 0
                && buffer.descriptor.elementStride == sizeof(uint)
                && !capabilities.Supports(GraphicsFeature.Index32))
            {
                diagnostics.Add(new RenderGraphDiagnostic(
                    "RENDER_GRAPH_INDEX32_UNSUPPORTED",
                    $"Buffer '{buffer.name}' requires unsigned 32-bit index capability.",
                    RenderGraphDiagnosticSeverity.Error,
                    resourceName: buffer.name));
            }
        }
    }

    private static void ValidatePasses(
        GraphicsCapabilities capabilities,
        IReadOnlyList<RenderTextureRecord> textures,
        IReadOnlyList<RenderBufferRecord> buffers,
        IReadOnlyList<RenderPassRecord> passes,
        List<RenderGraphDiagnostic> diagnostics)
    {
        foreach (RenderPassRecord pass in passes)
        {
            if (pass.kind == RenderPassKind.Compute && !capabilities.Supports(GraphicsFeature.Compute))
            {
                diagnostics.Add(new RenderGraphDiagnostic(
                    "RENDER_GRAPH_COMPUTE_UNSUPPORTED",
                    $"Compute pass '{pass.name}' requires compute capability.",
                    RenderGraphDiagnosticSeverity.Error,
                    pass.name));
            }

            ValidatePassResourceConflicts(pass, diagnostics);
            ValidatePassResourceUsage(capabilities, textures, buffers, pass, diagnostics);
            if (pass.kind != RenderPassKind.Raster && pass.attachments.Count != 0)
            {
                diagnostics.Add(new RenderGraphDiagnostic(
                    "RENDER_GRAPH_ATTACHMENT_DOMAIN",
                    $"Pass '{pass.name}' is not a raster pass and cannot own attachments.",
                    RenderGraphDiagnosticSeverity.Error,
                    pass.name));
            }

            if (pass.surface.isValid && pass.kind != RenderPassKind.Raster)
            {
                diagnostics.Add(new RenderGraphDiagnostic(
                    "RENDER_GRAPH_SURFACE_DOMAIN",
                    $"Pass '{pass.name}' is not a raster pass and cannot target a presentation surface.",
                    RenderGraphDiagnosticSeverity.Error,
                    pass.name));
            }

            if (pass.surface.isValid && pass.attachments.Count != 0)
            {
                diagnostics.Add(new RenderGraphDiagnostic(
                    "RENDER_GRAPH_SURFACE_ATTACHMENTS",
                    $"Raster pass '{pass.name}' cannot target a presentation surface and texture attachments together.",
                    RenderGraphDiagnosticSeverity.Error,
                    pass.name));
            }

            if (pass.clearsPresentationTarget && pass.attachments.Count != 0)
            {
                diagnostics.Add(new RenderGraphDiagnostic(
                    "RENDER_GRAPH_PRESENTATION_CLEAR_ATTACHMENTS",
                    $"Raster pass '{pass.name}' cannot combine a presentation clear with texture attachments.",
                    RenderGraphDiagnosticSeverity.Error,
                    pass.name));
            }

            if (pass.attachments.Count == 0)
            {
                continue;
            }

            HashSet<int> colorSlots = [];
            HashSet<int> attachedTextures = [];
            int colorCount = 0;
            int depthCount = 0;
            RenderTextureDescriptor? firstDescriptor = null;
            foreach (RenderAttachment attachment in pass.attachments)
            {
                RenderTextureRecord texture = textures[attachment.texture.index];
                RenderTextureDescriptor descriptor = texture.descriptor;
                if (!attachedTextures.Add(attachment.texture.index))
                {
                    diagnostics.Add(new RenderGraphDiagnostic(
                        "RENDER_GRAPH_DUPLICATE_ATTACHMENT_RESOURCE",
                        $"Raster pass '{pass.name}' attaches texture '{texture.name}' more than once.",
                        RenderGraphDiagnosticSeverity.Error,
                        pass.name,
                        texture.name));
                }

                if (attachment.mipLevel >= descriptor.mipCount
                    || (attachment.mipLevel < descriptor.mipCount
                        && attachment.arrayLayer >= descriptor.GetSubresourceLayerCount(attachment.mipLevel)))
                {
                    diagnostics.Add(new RenderGraphDiagnostic(
                        "RENDER_GRAPH_ATTACHMENT_SUBRESOURCE",
                        $"Raster pass '{pass.name}' attachment subresource is outside texture '{texture.name}'.",
                        RenderGraphDiagnosticSeverity.Error,
                        pass.name,
                        texture.name));
                }

                if (attachment.isDepth)
                {
                    depthCount++;
                    if ((descriptor.usage & RenderTextureUsage.DepthStencilAttachment) == 0
                        || !IsDepthFormat(descriptor.format))
                    {
                        AddAttachmentUsageError(pass, texture, "depth-stencil", diagnostics);
                    }
                }
                else
                {
                    colorCount++;
                    if (!colorSlots.Add(attachment.slot))
                    {
                        diagnostics.Add(new RenderGraphDiagnostic(
                            "RENDER_GRAPH_DUPLICATE_ATTACHMENT",
                            $"Raster pass '{pass.name}' uses color attachment slot {attachment.slot} more than once.",
                            RenderGraphDiagnosticSeverity.Error,
                            pass.name,
                            texture.name));
                    }

                    if ((descriptor.usage & RenderTextureUsage.ColorAttachment) == 0
                        || IsDepthFormat(descriptor.format))
                    {
                        AddAttachmentUsageError(pass, texture, "color", diagnostics);
                    }
                }

                firstDescriptor ??= descriptor;
                int attachmentWidth = MipExtent(descriptor.width, attachment.mipLevel);
                int attachmentHeight = MipExtent(descriptor.height, attachment.mipLevel);
                int firstWidth = MipExtent(firstDescriptor.width, pass.attachments[0].mipLevel);
                int firstHeight = MipExtent(firstDescriptor.height, pass.attachments[0].mipLevel);
                if (firstWidth != attachmentWidth
                    || firstHeight != attachmentHeight
                    || firstDescriptor.sampleCount != descriptor.sampleCount)
                {
                    diagnostics.Add(new RenderGraphDiagnostic(
                        "RENDER_GRAPH_ATTACHMENT_MISMATCH",
                        $"Raster pass '{pass.name}' attachments must have equal width, height and sample count.",
                        RenderGraphDiagnosticSeverity.Error,
                        pass.name,
                        texture.name));
                }
            }

            if (colorCount > capabilities.limits.maxColorAttachments || depthCount > 1)
            {
                diagnostics.Add(new RenderGraphDiagnostic(
                    "RENDER_GRAPH_ATTACHMENT_LIMIT",
                    $"Raster pass '{pass.name}' exceeds the device attachment limit.",
                    RenderGraphDiagnosticSeverity.Error,
                    pass.name));
            }
        }
    }

    private static int MipExtent(int extent, int mipLevel)
        => mipLevel >= 31 ? 1 : Math.Max(1, extent >> mipLevel);

    private static void ValidatePassResourceConflicts(
        RenderPassRecord pass,
        List<RenderGraphDiagnostic> diagnostics)
    {
        Dictionary<RenderResourceKey, RenderResourceAccess> accessByResource = [];
        foreach (RenderResourceUse use in pass.resources)
        {
            if (!accessByResource.TryGetValue(use.key, out RenderResourceAccess previous))
            {
                accessByResource.Add(use.key, use.access);
                continue;
            }

            if (previous == use.access && use.access != RenderResourceAccess.ReadWrite)
            {
                continue;
            }

            diagnostics.Add(new RenderGraphDiagnostic(
                "RENDER_GRAPH_PASS_HAZARD",
                $"Pass '{pass.name}' declares conflicting access for one resource. Use an explicit ReadWrite declaration.",
                RenderGraphDiagnosticSeverity.Error,
                pass.name));
        }
    }

    private static void ValidatePassResourceUsage(
        GraphicsCapabilities capabilities,
        IReadOnlyList<RenderTextureRecord> textures,
        IReadOnlyList<RenderBufferRecord> buffers,
        RenderPassRecord pass,
        List<RenderGraphDiagnostic> diagnostics)
    {
        foreach (RenderResourceUse use in pass.resources)
        {
            if (use.key.isTexture)
            {
                RenderTextureRecord texture = textures[use.key.index];
                RenderTextureUsage required = use.kind switch
                {
                    RenderResourceUseKind.GenericRead => RenderTextureUsage.Sampled,
                    RenderResourceUseKind.StorageRead
                        or RenderResourceUseKind.StorageWrite
                        or RenderResourceUseKind.StorageReadWrite
                        => RenderTextureUsage.Storage,
                    RenderResourceUseKind.CopySource => RenderTextureUsage.CopySource,
                    RenderResourceUseKind.CopyDestination => RenderTextureUsage.CopyDestination,
                    RenderResourceUseKind.ColorAttachment => RenderTextureUsage.ColorAttachment,
                    RenderResourceUseKind.DepthStencilAttachment
                        => RenderTextureUsage.DepthStencilAttachment,
                    _ => 0
                };
                if ((texture.descriptor.usage & required) != required)
                {
                    AddResourceUsageError(pass, texture.name, required.ToString(), diagnostics);
                }

                if (use.kind is RenderResourceUseKind.CopySource
                    or RenderResourceUseKind.CopyDestination
                    && !capabilities.Supports(GraphicsFeature.TextureBlit))
                {
                    AddCapabilityError(
                        pass,
                        texture.name,
                        "texture-copy",
                        GraphicsFeature.TextureBlit,
                        diagnostics);
                }

                if (use.kind is RenderResourceUseKind.StorageRead
                    or RenderResourceUseKind.StorageWrite
                    or RenderResourceUseKind.StorageReadWrite)
                {
                    RenderStorageAccess access = use.kind switch
                    {
                        RenderResourceUseKind.StorageRead => RenderStorageAccess.Read,
                        RenderResourceUseKind.StorageWrite => RenderStorageAccess.Write,
                        RenderResourceUseKind.StorageReadWrite => RenderStorageAccess.ReadWrite,
                        _ => throw new InvalidOperationException("Unexpected storage texture access.")
                    };
                    if (!capabilities.Supports(GraphicsFeature.StorageTexture)
                        || !capabilities.SupportsStorage(texture.descriptor.format, access))
                    {
                        diagnostics.Add(new RenderGraphDiagnostic(
                            "RENDER_GRAPH_STORAGE_TEXTURE_ACCESS_UNSUPPORTED",
                            $"Texture '{texture.name}' does not support {access} storage access required by pass '{pass.name}'.",
                            RenderGraphDiagnosticSeverity.Error,
                            pass.name,
                            texture.name));
                    }
                }

                continue;
            }

            RenderBufferUsage requiredBufferUsage = use.kind switch
            {
                RenderResourceUseKind.StorageRead
                    or RenderResourceUseKind.StorageWrite
                    or RenderResourceUseKind.StorageReadWrite
                    => RenderBufferUsage.Storage,
                RenderResourceUseKind.CopySource => RenderBufferUsage.CopySource,
                RenderResourceUseKind.CopyDestination => RenderBufferUsage.CopyDestination,
                _ => 0
            };
            if (requiredBufferUsage != 0)
            {
                // Buffer names and descriptors are validated by the overload below.
                ValidateBufferUse(
                    capabilities,
                    buffers,
                    pass,
                    use,
                    requiredBufferUsage,
                    diagnostics);
            }
        }
    }

    private static void ValidateBufferUse(
        GraphicsCapabilities capabilities,
        IReadOnlyList<RenderBufferRecord> buffers,
        RenderPassRecord pass,
        RenderResourceUse use,
        RenderBufferUsage requiredUsage,
        List<RenderGraphDiagnostic> diagnostics)
    {
        RenderBufferRecord buffer = buffers[use.key.index];
        if ((buffer.descriptor.usage & requiredUsage) != requiredUsage)
        {
            AddResourceUsageError(pass, buffer.name, requiredUsage.ToString(), diagnostics);
        }

        if (use.kind is RenderResourceUseKind.CopySource or RenderResourceUseKind.CopyDestination
            && !capabilities.Supports(GraphicsFeature.BufferCopy))
        {
            AddCapabilityError(
                pass,
                buffer.name,
                "buffer-copy",
                GraphicsFeature.BufferCopy,
                diagnostics);
        }
    }

    private static void BuildResourceDependencies(
        IReadOnlyList<RenderTextureRecord> textures,
        IReadOnlyList<RenderBufferRecord> buffers,
        IReadOnlyList<RenderPassRecord> passes,
        IReadOnlySet<RenderResourceKey> outputs,
        IReadOnlyList<HashSet<int>> dependencies,
        IReadOnlyList<HashSet<int>> dataDependencies,
        List<RenderGraphDiagnostic> diagnostics)
    {
        Dictionary<RenderResourceKey, int> lastWriters = [];
        Dictionary<RenderResourceKey, List<int>> readers = [];
        HashSet<RenderResourceKey> initialized = [];
        for (int textureIndex = 0; textureIndex < textures.Count; textureIndex++)
        {
            if (textures[textureIndex].imported)
            {
                initialized.Add(new RenderResourceKey(true, textureIndex));
            }
        }

        for (int bufferIndex = 0; bufferIndex < buffers.Count; bufferIndex++)
        {
            if (buffers[bufferIndex].imported)
            {
                initialized.Add(new RenderResourceKey(false, bufferIndex));
            }
        }

        for (int passIndex = 0; passIndex < passes.Count; passIndex++)
        {
            RenderPassRecord pass = passes[passIndex];
            IReadOnlyList<RenderResourceUse> uniqueUses = [.. UniqueUses(pass.resources)];
            foreach (RenderResourceUse use in uniqueUses)
            {
                bool reads = use.access is RenderResourceAccess.Read or RenderResourceAccess.ReadWrite;
                bool writes = use.access is RenderResourceAccess.Write or RenderResourceAccess.ReadWrite;

                if (reads && !initialized.Contains(use.key))
                {
                    diagnostics.Add(new RenderGraphDiagnostic(
                        "RENDER_GRAPH_UNINITIALIZED_READ",
                        $"Pass '{passes[passIndex].name}' reads '{GetResourceName(use.key, textures, buffers)}' before it is initialized.",
                        RenderGraphDiagnosticSeverity.Error,
                        passes[passIndex].name,
                        GetResourceName(use.key, textures, buffers)));
                }

                if (lastWriters.TryGetValue(use.key, out int writer))
                {
                    AddDependency(writer, passIndex, dependencies, dataDependencies);
                }

                if (writes)
                {
                    if (readers.TryGetValue(use.key, out List<int>? previousReaders))
                    {
                        foreach (int reader in previousReaders)
                        {
                            AddDependency(reader, passIndex, dependencies, dataDependencies);
                        }

                        previousReaders.Clear();
                    }

                    lastWriters[use.key] = passIndex;
                }
                else
                {
                    if (!readers.TryGetValue(use.key, out List<int>? resourceReaders))
                    {
                        resourceReaders = [];
                        readers.Add(use.key, resourceReaders);
                    }

                    resourceReaders.Add(passIndex);
                }
            }

            foreach (RenderResourceUse use in uniqueUses)
            {
                bool writes = use.access is RenderResourceAccess.Write or RenderResourceAccess.ReadWrite;
                if (!writes)
                {
                    continue;
                }

                if (StoresResult(pass, use.key))
                {
                    initialized.Add(use.key);
                }
                else
                {
                    initialized.Remove(use.key);
                }
            }
        }

        foreach (RenderResourceKey output in outputs)
        {
            if (!initialized.Contains(output))
            {
                string resourceName = GetResourceName(output, textures, buffers);
                diagnostics.Add(new RenderGraphDiagnostic(
                    "RENDER_GRAPH_OUTPUT_UNINITIALIZED",
                    $"Graph output '{resourceName}' has no stored contents at the end of the graph.",
                    RenderGraphDiagnosticSeverity.Error,
                    resourceName: resourceName));
            }
        }
    }

    private static bool StoresResult(RenderPassRecord pass, RenderResourceKey key)
    {
        foreach (RenderAttachment attachment in pass.attachments)
        {
            if (key.isTexture
                && attachment.texture.index == key.index
                && attachment.storeAction == RenderStoreAction.Discard)
            {
                return false;
            }
        }

        return true;
    }

    private static void BuildPhaseDependencies(
        IReadOnlyList<RenderPassRecord> passes,
        IReadOnlyList<HashSet<int>> dependencies)
    {
        for (int passIndex = 0; passIndex < passes.Count; passIndex++)
        {
            RenderPassRecord pass = passes[passIndex];
            for (int targetIndex = 0; targetIndex < passes.Count; targetIndex++)
            {
                if (passIndex == targetIndex)
                {
                    continue;
                }

                RenderPassRecord target = passes[targetIndex];
                if (pass.before.Contains(target.phase))
                {
                    dependencies[targetIndex].Add(passIndex);
                }

                if (pass.after.Contains(target.phase))
                {
                    dependencies[passIndex].Add(targetIndex);
                }
            }
        }
    }

    private static HashSet<int> FindLivePasses(
        IReadOnlyList<RenderTextureRecord> textures,
        IReadOnlyList<RenderBufferRecord> buffers,
        IReadOnlyList<RenderPassRecord> passes,
        IReadOnlySet<RenderResourceKey> outputs,
        IReadOnlyList<HashSet<int>> dataDependencies)
    {
        Stack<int> pending = [];
        HashSet<int> live = [];
        for (int passIndex = 0; passIndex < passes.Count; passIndex++)
        {
            RenderPassRecord pass = passes[passIndex];
            bool root = pass.hasSideEffect;
            foreach (RenderResourceUse use in pass.resources)
            {
                bool writes = use.access is RenderResourceAccess.Write or RenderResourceAccess.ReadWrite;
                if (writes && (outputs.Contains(use.key) || IsImported(use.key, textures, buffers)))
                {
                    root = true;
                    break;
                }
            }

            if (root)
            {
                pending.Push(passIndex);
            }
        }

        while (pending.TryPop(out int passIndex))
        {
            if (!live.Add(passIndex))
            {
                continue;
            }

            foreach (int dependency in dataDependencies[passIndex])
            {
                pending.Push(dependency);
            }
        }

        return live;
    }

    private static List<int>? TopologicalSort(
        int passCount,
        IReadOnlySet<int> livePasses,
        IReadOnlyList<HashSet<int>> dependencies)
    {
        int[] inDegrees = new int[passCount];
        List<List<int>> dependants = new(passCount);
        for (int i = 0; i < passCount; i++)
        {
            dependants.Add([]);
        }

        foreach (int passIndex in livePasses)
        {
            foreach (int dependency in dependencies[passIndex])
            {
                if (!livePasses.Contains(dependency))
                {
                    continue;
                }

                inDegrees[passIndex]++;
                dependants[dependency].Add(passIndex);
            }
        }

        SortedSet<int> ready = [];
        foreach (int passIndex in livePasses)
        {
            if (inDegrees[passIndex] == 0)
            {
                ready.Add(passIndex);
            }
        }

        List<int> result = [];
        while (ready.Count != 0)
        {
            int passIndex = ready.Min;
            ready.Remove(passIndex);
            result.Add(passIndex);
            foreach (int dependant in dependants[passIndex])
            {
                inDegrees[dependant]--;
                if (inDegrees[dependant] == 0)
                {
                    ready.Add(dependant);
                }
            }
        }

        return result.Count == livePasses.Count ? result : null;
    }

    private static int[] AllocateTextureSlots(
        IReadOnlyList<RenderTextureRecord> textures,
        IReadOnlyList<RenderPassRecord> passes,
        IReadOnlyDictionary<int, int> schedulePositions)
        => AllocateSlots(
            textures.Count,
            index => textures[index].imported,
            (left, right) => textures[left].descriptor.Equals(textures[right].descriptor),
            new RenderResourceKey(true, 0),
            passes,
            schedulePositions);

    private static int[] AllocateBufferSlots(
        IReadOnlyList<RenderBufferRecord> buffers,
        IReadOnlyList<RenderPassRecord> passes,
        IReadOnlyDictionary<int, int> schedulePositions)
        => AllocateSlots(
            buffers.Count,
            index => buffers[index].imported,
            (left, right) => buffers[left].descriptor.Equals(buffers[right].descriptor),
            new RenderResourceKey(false, 0),
            passes,
            schedulePositions);

    private static int[] AllocateSlots(
        int resourceCount,
        Func<int, bool> isImported,
        Func<int, int, bool> descriptorsEqual,
        RenderResourceKey keyTemplate,
        IReadOnlyList<RenderPassRecord> passes,
        IReadOnlyDictionary<int, int> schedulePositions)
    {
        int[] firstUses = new int[resourceCount];
        int[] lastUses = new int[resourceCount];
        int[] slots = new int[resourceCount];
        Array.Fill(firstUses, int.MaxValue);
        Array.Fill(lastUses, -1);
        Array.Fill(slots, -1);

        foreach ((int passIndex, int position) in schedulePositions)
        {
            foreach (RenderResourceUse use in passes[passIndex].resources)
            {
                if (use.key.isTexture != keyTemplate.isTexture)
                {
                    continue;
                }

                firstUses[use.key.index] = Math.Min(firstUses[use.key.index], position);
                lastUses[use.key.index] = Math.Max(lastUses[use.key.index], position);
            }
        }

        List<int> resources = [];
        for (int index = 0; index < resourceCount; index++)
        {
            if (!isImported(index) && lastUses[index] >= 0)
            {
                resources.Add(index);
            }
        }

        resources.Sort((left, right) => firstUses[left].CompareTo(firstUses[right]));
        List<(int representative, int lastUse)> allocations = [];
        foreach (int resource in resources)
        {
            int selected = -1;
            for (int slot = 0; slot < allocations.Count; slot++)
            {
                (int representative, int lastUse) allocation = allocations[slot];
                if (allocation.lastUse < firstUses[resource]
                    && descriptorsEqual(allocation.representative, resource))
                {
                    selected = slot;
                    allocations[slot] = (allocation.representative, lastUses[resource]);
                    break;
                }
            }

            if (selected < 0)
            {
                selected = allocations.Count;
                allocations.Add((resource, lastUses[resource]));
            }

            slots[resource] = selected;
        }

        return slots;
    }

    private static CompiledRenderGraph BuildCompiledGraph(
        uint generation,
        IReadOnlyList<RenderTextureRecord> textures,
        IReadOnlyList<RenderBufferRecord> buffers,
        IReadOnlyList<RenderPassRecord> passes,
        IReadOnlyList<int> schedule,
        IReadOnlyList<int> textureSlots,
        IReadOnlyList<int> bufferSlots)
    {
        List<CompiledRenderPass> compiledPasses = [];
        for (int viewIndex = 0; viewIndex < schedule.Count; viewIndex++)
        {
            RenderPassRecord pass = passes[schedule[viewIndex]];
            List<CompiledRenderAttachment> attachments = [];
            foreach (RenderAttachment attachment in pass.attachments)
            {
                attachments.Add(new CompiledRenderAttachment(attachment));
            }

            compiledPasses.Add(new CompiledRenderPass(
                pass.name,
                pass.phase,
                pass.kind,
                viewIndex,
                attachments,
                pass.surface,
                pass.clearsPresentationTarget,
                pass.presentationClearColor,
                pass.viewTransform,
                pass.execute));
        }

        List<CompiledRenderTexture> compiledTextures = [];
        for (int index = 0; index < textures.Count; index++)
        {
            RenderTextureRecord texture = textures[index];
            compiledTextures.Add(new CompiledRenderTexture(
                new RenderTextureHandle(index, generation),
                texture.name,
                texture.descriptor,
                texture.imported,
                texture.persistentHandle,
                textureSlots[index]));
        }

        List<CompiledRenderBuffer> compiledBuffers = [];
        for (int index = 0; index < buffers.Count; index++)
        {
            RenderBufferRecord buffer = buffers[index];
            compiledBuffers.Add(new CompiledRenderBuffer(
                new RenderBufferHandle(index, generation),
                buffer.name,
                buffer.descriptor,
                buffer.imported,
                buffer.persistentHandle,
                bufferSlots[index]));
        }

        return new CompiledRenderGraph(generation, compiledPasses, compiledTextures, compiledBuffers);
    }

    private static IEnumerable<RenderResourceUse> UniqueUses(IReadOnlyList<RenderResourceUse> uses)
    {
        HashSet<RenderResourceKey> emitted = [];
        foreach (RenderResourceUse use in uses)
        {
            if (emitted.Add(use.key))
            {
                yield return use;
            }
        }
    }

    private static bool IsImported(
        RenderResourceKey key,
        IReadOnlyList<RenderTextureRecord> textures,
        IReadOnlyList<RenderBufferRecord> buffers)
        => key.isTexture ? textures[key.index].imported : buffers[key.index].imported;

    private static string GetResourceName(
        RenderResourceKey key,
        IReadOnlyList<RenderTextureRecord> textures,
        IReadOnlyList<RenderBufferRecord> buffers)
        => key.isTexture ? textures[key.index].name : buffers[key.index].name;

    private static bool IsDepthFormat(RenderTextureFormat format)
        => format is RenderTextureFormat.Depth24Stencil8 or RenderTextureFormat.Depth32Float;

    private static bool ContainsErrors(IReadOnlyList<RenderGraphDiagnostic> diagnostics)
    {
        foreach (RenderGraphDiagnostic diagnostic in diagnostics)
        {
            if (diagnostic.severity == RenderGraphDiagnosticSeverity.Error)
            {
                return true;
            }
        }

        return false;
    }

    private static List<HashSet<int>> CreateEdgeSets(int count)
    {
        List<HashSet<int>> result = new(count);
        for (int i = 0; i < count; i++)
        {
            result.Add([]);
        }

        return result;
    }

    private static void AddDependency(
        int dependency,
        int dependant,
        IReadOnlyList<HashSet<int>> dependencies,
        IReadOnlyList<HashSet<int>> dataDependencies)
    {
        if (dependency == dependant)
        {
            return;
        }

        dependencies[dependant].Add(dependency);
        dataDependencies[dependant].Add(dependency);
    }

    private static void AddAttachmentUsageError(
        RenderPassRecord pass,
        RenderTextureRecord texture,
        string role,
        List<RenderGraphDiagnostic> diagnostics)
    {
        diagnostics.Add(new RenderGraphDiagnostic(
            "RENDER_GRAPH_ATTACHMENT_USAGE",
            $"Texture '{texture.name}' is not declared for {role} attachment usage.",
            RenderGraphDiagnosticSeverity.Error,
            pass.name,
            texture.name));
    }

    private static void AddResourceUsageError(
        RenderPassRecord pass,
        string resourceName,
        string requiredUsage,
        List<RenderGraphDiagnostic> diagnostics)
    {
        diagnostics.Add(new RenderGraphDiagnostic(
            "RENDER_GRAPH_RESOURCE_USAGE",
            $"Resource '{resourceName}' used by pass '{pass.name}' requires '{requiredUsage}' usage.",
            RenderGraphDiagnosticSeverity.Error,
            pass.name,
            resourceName));
    }

    private static void AddCapabilityError(
        RenderPassRecord pass,
        string resourceName,
        string operation,
        GraphicsFeature requiredFeature,
        List<RenderGraphDiagnostic> diagnostics)
    {
        diagnostics.Add(new RenderGraphDiagnostic(
            "RENDER_GRAPH_CAPABILITY_UNSUPPORTED",
            $"Pass '{pass.name}' requires '{requiredFeature}' for {operation} operations.",
            RenderGraphDiagnosticSeverity.Error,
            pass.name,
            resourceName));
    }
}
