using System;
using System.Collections.Generic;
using Inno.Engine.Graphics.Resources.GpuResources.Bindings;
using Inno.Engine.Graphics.Resources.GpuResources.Cache;
using Inno.Platform.Graphics;

namespace Inno.Engine.Graphics.Resources.GpuResources.Compilers;

internal static class PerObjectGpuCompiler
{
    public static PerObjectGpuBinding Compile(
        IGraphicsDevice gd,
        Guid ownerGuid,
        IReadOnlyList<(string name, Type type)> uniforms,
        ShaderStage stages)
    {
        var index = new Dictionary<string, int>(StringComparer.Ordinal);
        var ubHandles = new GpuCache.Handle<IUniformBuffer>[uniforms.Count];

        for (int i = 0; i < uniforms.Count; i++)
        {
            var (name, type) = uniforms[i];
            if (!index.TryAdd(name, i))
            {
                throw new InvalidOperationException($"Duplicate per-object uniform '{name}'.");
            }

            int ubVariant = GpuVariant.Build(v =>
            {
                v.Add(name);
                v.AddType(type);
            });

            ubHandles[i] = RenderGraphics.gpuCache.Acquire(
                factory: () => gd.CreateUniformBuffer(name, type),
                ownerGuid,
                variantKey: ubVariant
            );
        }

        // ResourceSet is per-owner (usually per renderable instance)
        int rsVariant = GpuVariant.Build(v =>
        {
            v.Add((int)stages);
            v.Add(uniforms.Count);
            for (int i = 0; i < uniforms.Count; i++)
            {
                v.Add(uniforms[i].name);
                v.AddType(uniforms[i].type);
            }
        });

        var rsHandle = RenderGraphics.gpuCache.Acquire(
            factory: () =>
            {
                var rawUBs = new IUniformBuffer[ubHandles.Length];
                for (int i = 0; i < ubHandles.Length; i++) rawUBs[i] = ubHandles[i].value;

                return gd.CreateResourceSet(new ResourceSetBinding
                {
                    shaderStages = stages,
                    uniformBuffers = rawUBs
                });
            },
            ownerGuid,
            variantKey: rsVariant
        );

        return new PerObjectGpuBinding(index, ubHandles, rsHandle);
    }
}
