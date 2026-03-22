using System;
using System.Linq;
using Inno.Engine.Graphics.Resources.CpuResources;
using Inno.Engine.Graphics.Resources.GpuResources.Bindings;
using Inno.Engine.Graphics.Resources.GpuResources.Cache;
using Inno.Platform.Graphics;

namespace Inno.Engine.Graphics.Resources.GpuResources.Compilers;

internal static class RenderableGpuCompiler
{
    public static RenderableGpuBinding Compile(
        IGraphicsDevice gd,
        Guid renderableGuid,
        Mesh mesh,
        Material[] materials,
        (string name, Type type)[] perObjectUniforms)
    {
        // Mesh GPU (shared by mesh.guid)
        var meshGpu = MeshGpuCompiler.Compile(gd, mesh);

        // Per-object binding (owned by this renderable instance)
        var perObj = PerObjectGpuCompiler.Compile(
            gd,
            ownerGuid: renderableGuid,
            uniforms: perObjectUniforms,
            stages: ShaderStage.Vertex | ShaderStage.Fragment);

        var vertexLayoutTypes = mesh.GetAllAttributes().Select(a => a.elementType).ToList();
        var matsGpu = new MaterialGpuBinding[materials.Length];
        var psoHandles = new GpuCache.Handle<IPipelineState>[materials.Length];

        for (int i = 0; i < materials.Length; i++)
        {
            var mat = materials[i];

            // Material GPU (currently treated as per-renderable instance)
            matsGpu[i] = MaterialGpuCompiler.Compile(
                gd,
                ownerGuid: renderableGuid,
                material: mat);

            var matGpu = matsGpu[i];

            // --- Build PSO variant key (global/shared)
            int psoVariant = GpuVariant.Build(v =>
            {
                // Shaders identity (use CPU shader GUIDs; stable and editor-friendly)
                var vsGuid = mat.shaders.GetShadersByStage(ShaderStage.Vertex).Values.First().guid;
                var fsGuid = mat.shaders.GetShadersByStage(ShaderStage.Fragment).Values.First().guid;
                v.AddId(vsGuid);
                v.AddId(fsGuid);

                // Mesh vertex layout signature
                foreach (var t in vertexLayoutTypes)
                    v.AddType(t);

                // Fixed states
                v.Add((int)mat.renderState.blendMode);
                v.Add((int)mat.renderState.depthStencilState);
                v.Add((int)mesh.renderState.topology);

                // Resource layout signature (per-object set)
                v.Add(perObj.uniformBuffers.Length);
                foreach (var t in perObj.uniformBuffers)
                    v.AddType(t.GetType());

                // Resource layout signature (material set) - counts for now
                v.Add(matGpu.uniformBuffers.Length);
                v.Add(matGpu.textures.Length);
                v.Add(matGpu.samplers.Length);
            });

            // --- Acquire PSO from global domain
            psoHandles[i] = RenderGraphics.gpuCache.Acquire(
                factory: () =>
                {
                    var perObjectSet = new ResourceSetBinding
                    {
                        shaderStages = ShaderStage.Vertex | ShaderStage.Fragment,
                        uniformBuffers = perObj.uniformBuffers
                    };

                    var materialSet = new ResourceSetBinding
                    {
                        shaderStages = ShaderStage.Vertex | ShaderStage.Fragment,
                        uniformBuffers = matGpu.uniformBuffers,
                        textures = matGpu.textures,
                        samplers = matGpu.samplers
                    };

                    var desc = new PipelineStateDescription
                    {
                        vertexShader = matGpu.vertexShader,
                        fragmentShader = matGpu.fragmentShader,
                        vertexLayoutTypes = vertexLayoutTypes,
                        blendMode = mat.renderState.blendMode,
                        depthStencilState = mat.renderState.depthStencilState,
                        primitiveTopology = mesh.renderState.topology,

                        // Slot order must match RenderableGpuBinding.C_*_SET_INDEX
                        resourceLayoutSpecifiers = [perObjectSet, materialSet]
                    };

                    return gd.CreatePipelineState(desc);
                },
                guid: GpuCache.GLOBAL_DOMAIN,
                variantKey: psoVariant
            );
        }

        return new RenderableGpuBinding(psoHandles, meshGpu, matsGpu, perObj);
    }

    // Backward-compatible overload (creates unique per-object owner guid)
    public static RenderableGpuBinding Compile(
        IGraphicsDevice gd,
        Mesh mesh,
        Material[] materials,
        (string name, Type type)[] perObjectUniforms)
    {
        return Compile(gd, Guid.NewGuid(), mesh, materials, perObjectUniforms);
    }
}
