using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Inno.Engine.Graphics.Resources.CpuResources;
using Inno.Engine.Graphics.Resources.GpuResources.Bindings;
using Inno.Engine.Graphics.Resources.GpuResources.Cache;
using Inno.Platform.Graphics;

namespace Inno.Engine.Graphics.Resources.GpuResources.Compilers;

internal static class MeshGpuCompiler
{
    public static MeshGpuBinding Compile(
        IGraphicsDevice gd, 
        Mesh mesh)
    {
        // Ensure CPU mesh has segments for draw traversal, but DO NOT mutate it.
        var cpuSegs = mesh.segmentCount == 0
            ? new List<MeshSegment> { new MeshSegment("whole", 0, mesh.indexCount, 0) }
            : new List<MeshSegment>(mesh.GetSegments());

        // Vertex buffer (shared by mesh guid + layout variant)
        var stride = GenerateVertexStride(mesh);
        int vbVariant = GpuVariant.Build(v =>
        {
            v.Add(stride);
            v.Add(mesh.vertexCount);
            foreach (var attr in mesh.GetAllAttributes())
            {
                v.Add(attr.name);
                v.AddType(attr.elementType);
            }
        });

        var vbHandle = RenderGraphics.gpuCache.Acquire(
            factory: () =>
            {
                var vb = gd.CreateVertexBuffer((uint)(mesh.vertexCount * stride));
                vb.Set(GenerateVertexArray(mesh));
                return vb;
            },
            mesh.guid,
            variantKey: vbVariant
        );

        // Index buffers (usually per segment). We keep them cached per mesh guid + segment signature.
        var ibHandles = new GpuCache.Handle<IIndexBuffer>[cpuSegs.Count];
        var gpuSegs = cpuSegs.ToArray();
        var indices = mesh.GetIndices();

        for (int i = 0; i < cpuSegs.Count; i++)
        {
            var s = cpuSegs[i];
            var segmentId = i;

            int ibVariant = GpuVariant.Build(v =>
            {
                v.Add(segmentId); // segment index
                v.Add(s.indexStart);
                v.Add(s.indexCount);
            });

            ibHandles[i] = RenderGraphics.gpuCache.Acquire(
                factory: () =>
                {
                    var ib = gd.CreateIndexBuffer((uint)(s.indexCount * sizeof(uint)));
                    uint[] sub = new uint[s.indexCount];
                    Array.Copy(indices, s.indexStart, sub, 0, s.indexCount);
                    ib.Set(sub);
                    return ib;
                },
                mesh.guid,
                variantKey: ibVariant
            );
        }

        return new MeshGpuBinding(
            gpuSegs,
            vbHandle,
            ibHandles
        );
    }

    private static int GenerateVertexStride(Mesh mesh)
    {
        int stride = 0;
        foreach (var attr in mesh.GetAllAttributes())
            stride += Marshal.SizeOf(attr.elementType);
        return stride;
    }

    private static byte[] GenerateVertexArray(Mesh mesh)
    {
        var attrs = mesh.GetAllAttributes();
        if (attrs.Count == 0) return [];

        int vCount = mesh.vertexCount;
        int stride = GenerateVertexStride(mesh);
        byte[] data = new byte[vCount * stride];

        int offset = 0;
        var offsets = new Dictionary<string, int>();
        foreach (var a in attrs)
        {
            offsets[a.name] = offset;
            offset += Marshal.SizeOf(a.elementType);
        }

        for (int i = 0; i < vCount; i++)
        {
            foreach (var a in attrs)
            {
                int elemSize = Marshal.SizeOf(a.elementType);
                int dst = i * stride + offsets[a.name];

                var handle = GCHandle.Alloc(a.data, GCHandleType.Pinned);
                try
                {
                    IntPtr ptr = handle.AddrOfPinnedObject() + i * elemSize;
                    Marshal.Copy(ptr, data, dst, elemSize);
                }
                finally
                {
                    handle.Free();
                }
            }
        }

        return data;
    }
}
