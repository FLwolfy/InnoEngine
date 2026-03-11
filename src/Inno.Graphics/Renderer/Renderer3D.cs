using System;
using System.Collections.Generic;

using Inno.Assets;
using Inno.Assets.AssetType;
using Inno.Core.Math;
using Inno.Graphics.Decoder;
using Inno.Graphics.Resources.CpuResources;
using Inno.Graphics.Resources.GpuResources.Bindings;
using Inno.Graphics.Resources.GpuResources.Compilers;
using Inno.Graphics.Targets;
using Inno.Platform.Graphics;

namespace Inno.Graphics.Renderer;

public static class Renderer3D
{
    private static IGraphicsDevice m_graphicsDevice = null!;

    // 每個 Mesh(Guid) 對應一個 RenderableGpuBinding
    // 注意：這個 binding 裡面包含 per-object uniform buffer，
    // 我們採「每次 DrawMesh 前更新」的方式（單 thread / 單 commandlist 下是 OK 的）。
    private static Dictionary<Guid, RenderableGpuBinding> m_unlitOpaqueCache = null!;

    private static (string name, Type type)[] s_perObjectUniforms = [
        ("MVP", typeof(Matrix)),
        ("Color", typeof(Color))
    ];

    public static void Initialize(IGraphicsDevice graphicsDevice)
    {
        m_graphicsDevice = graphicsDevice;
        m_unlitOpaqueCache = new Dictionary<Guid, RenderableGpuBinding>();
    }

    public static void LoadResources()
    {
        // Shaders（embedded）
        AssetManager.LoadEmbedded<ShaderAsset>("MeshUnlit.vert");
        AssetManager.LoadEmbedded<ShaderAsset>("MeshUnlit.frag");
    }

    public static void CleanResources()
    {
        if (m_unlitOpaqueCache != null)
        {
            foreach (var kv in m_unlitOpaqueCache)
                kv.Value.Dispose();
            m_unlitOpaqueCache.Clear();
        }
    }

    private static RenderableGpuBinding GetOrCreateUnlitOpaque(Mesh mesh)
    {
        if (m_unlitOpaqueCache.TryGetValue(mesh.guid, out var res))
            return res;

        // Material（最小化：Unlit + Opaque + DepthTest）
        var mat = new Material("MeshUnlitOpaque");
        mat.renderState = new MaterialRenderState
        {
            blendMode = BlendMode.Opaque,
            depthStencilState = DepthStencilState.DepthOnlyLessEqual
        };

        mat.shaders = new ShaderProgram();
        mat.shaders.Add(ResourceDecoder.DecodeBinaries<Shader, ShaderAsset>(
            AssetManager.GetEmbedded<ShaderAsset>("MeshUnlit.vert").Resolve()!
        ));
        mat.shaders.Add(ResourceDecoder.DecodeBinaries<Shader, ShaderAsset>(
            AssetManager.GetEmbedded<ShaderAsset>("MeshUnlit.frag").Resolve()!
        ));

        // Compile renderable（mesh GPU 會按 mesh.guid 共用）
        res = RenderableGpuCompiler.Compile(
            m_graphicsDevice,
            renderableGuid: mesh.guid,     // owner guid：用 mesh guid，方便 cache
            mesh: mesh,
            materials: [mat],
            perObjectUniforms: s_perObjectUniforms
        );

        m_unlitOpaqueCache[mesh.guid] = res;
        return res;
    }

    /// <summary>
    /// Draw a mesh with a simple unlit color shader.
    /// </summary>
    public static void DrawMesh(RenderContext ctx, Mesh mesh, Matrix model, Color color)
    {
        var res = GetOrCreateUnlitOpaque(mesh);
        var mvp = model * ctx.viewProjection;

        res.UpdatePerObject(ctx.commandList, "MVP", mvp);
        res.UpdatePerObject(ctx.commandList, "Color", color);

        res.DrawAll(ctx.commandList);
    }
}
