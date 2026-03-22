using System;
using System.Linq;
using Inno.Engine.Graphics.Resources.CpuResources;
using Inno.Engine.Graphics.Resources.GpuResources.Bindings;
using Inno.Engine.Graphics.Resources.GpuResources.Cache;
using Inno.Platform.Graphics;

namespace Inno.Engine.Graphics.Resources.GpuResources.Compilers;

internal static class MaterialGpuCompiler
{
    public static MaterialGpuBinding Compile(
        IGraphicsDevice gd,
        Guid ownerGuid,
        Material material
    )
    {
        // ---- Material UniformBuffers (cached; typically per material instance)
        var uniforms = material.GetAllUniforms();
        var matUbHandles = new GpuCache.Handle<IUniformBuffer>[uniforms.Count];
        for (int i = 0; i < uniforms.Count; i++)
        {
            var name = uniforms[i].name;
            var type = uniforms[i].value.GetType();

            int ubVariant = GpuVariant.Build(v =>
            {
                v.Add(name);
                v.AddType(type);
            });

            matUbHandles[i] = RenderGraphics.gpuCache.Acquire(
                factory: () => gd.CreateUniformBuffer(name, type),
                ownerGuid,
                variantKey: ubVariant
            );
        }

        var matUBs = new IUniformBuffer[matUbHandles.Length];
        for (int i = 0; i < matUbHandles.Length; i++) matUBs[i] = matUbHandles[i].value;

        // ---- Textures/Samplers (shared via cache)
        var texEntries = material.GetAllTextures();
        var texHandles = new GpuCache.Handle<ITexture>[texEntries.Count];
        var smpHandles = new GpuCache.Handle<ISampler>[texEntries.Count];

        for (int i = 0; i < texEntries.Count; i++)
        {
            var src = texEntries[i].texture;

            int texVariant = GpuVariant.Build(v =>
            {
                v.Add(src.width);
                v.Add(src.height);
                v.Add(1);
                v.Add((int)src.format);
                v.Add((int)src.usage);
                v.Add((int)src.dimension);
            });

            texHandles[i] = RenderGraphics.gpuCache.Acquire(
                factory: () =>
                {
                    var gpuTex = gd.CreateTexture(new TextureDescription
                    {
                        width = src.width,
                        height = src.height,
                        mipLevelCount = 1,
                        format = src.format,
                        usage = src.usage,
                        dimension = src.dimension
                    });

                    var bytes = src.data;
                    gpuTex.Set(ref bytes);
                    return gpuTex;
                },
                src.guid,
                variantKey: texVariant
            );

            int smpVariant = GpuVariant.Build(v =>
            {
                v.Add((int)SamplerFilter.Linear);
                v.Add((int)SamplerAddressMode.Clamp);
                v.Add((int)SamplerAddressMode.Clamp);
            });

            smpHandles[i] = RenderGraphics.gpuCache.Acquire(
                factory: () => gd.CreateSampler(new SamplerDescription
                {
                    filter = SamplerFilter.Linear,
                    addressU = SamplerAddressMode.Clamp,
                    addressV = SamplerAddressMode.Clamp
                }),
                GpuCache.GLOBAL_DOMAIN,
                variantKey: smpVariant
            );
        }

        // ---- Shaders (shared via cache)
        var vsCpu = material.shaders.GetShadersByStage(ShaderStage.Vertex).Values.First();
        var fsCpu = material.shaders.GetShadersByStage(ShaderStage.Fragment).Values.First();

        var vsHandle = RenderGraphics.gpuCache.Acquire(
            factory: () => gd.CreateVertexFragmentShader(
                new ShaderDescription { stage = vsCpu.stage, sourceBytes = vsCpu.shaderBinaries },
                new ShaderDescription { stage = fsCpu.stage, sourceBytes = fsCpu.shaderBinaries }
            ).Item1,
            vsCpu.guid
        );

        var fsHandle = RenderGraphics.gpuCache.Acquire(
            factory: () => gd.CreateVertexFragmentShader(
                new ShaderDescription { stage = vsCpu.stage, sourceBytes = vsCpu.shaderBinaries },
                new ShaderDescription { stage = fsCpu.stage, sourceBytes = fsCpu.shaderBinaries }
            ).Item2,
            fsCpu.guid
        );

        // ---- ResourceSet (usually per material instance; keep cached per ownerGuid)
        // Build raw arrays
        var rawTextures = texHandles.Select(h => h.value).ToArray();
        var rawSamplers = smpHandles.Select(h => h.value).ToArray();

        var materialSetBinding = new ResourceSetBinding
        {
            shaderStages = ShaderStage.Vertex | ShaderStage.Fragment,
            uniformBuffers = matUBs,
            textures = rawTextures,
            samplers = rawSamplers
        };
        
        int rsVariant = GpuVariant.Build(v =>
        {
            v.Add((int)materialSetBinding.shaderStages);
            v.Add(matUBs.Length);
            v.Add(rawTextures.Length);
            v.Add(rawSamplers.Length);

            // TODO: 
            // Make resource set rebuild when bindings change (editor-friendly).
            // Layout should be separated later; for now we key on the bound GPU resource identities.
            for (int i = 0; i < matUBs.Length; i++) v.AddType(matUBs[i].GetType());
            for (int i = 0; i < texEntries.Count; i++) v.AddId(texEntries[i].texture.guid);
        });

        var resourceSetHandle = RenderGraphics.gpuCache.Acquire(
            factory: () => gd.CreateResourceSet(materialSetBinding),
            ownerGuid,
            variantKey: rsVariant
        );

        return new MaterialGpuBinding(
            ubHandles: matUbHandles,
            texHandles: texHandles,
            smpHandles: smpHandles,
            vsHandle: vsHandle,
            fsHandle: fsHandle,
            resourceSetHandle: resourceSetHandle
        );
    }
}
