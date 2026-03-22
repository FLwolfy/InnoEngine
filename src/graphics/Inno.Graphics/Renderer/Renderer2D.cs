using System;
using System.Collections.Generic;

using Inno.Core.Mathematics;
using Inno.Engine.Graphics.Decoder;
using Inno.Engine.Graphics.Resources.CpuResources;
using Inno.Engine.Graphics.Resources.GpuResources.Bindings;
using Inno.Engine.Graphics.Resources.GpuResources.Compilers;
using Inno.Engine.Graphics.Targets;
using Inno.Platform.Graphics;

namespace Inno.Engine.Graphics.Renderer;

public static class Renderer2D
{
    // Graphics Device Backend
    private static IGraphicsDevice m_graphicsDevice = null!;

    // Quad Resources
    private static RenderableGpuBinding m_quadOpaque = null!;
    private static RenderableGpuBinding m_quadAlpha = null!;

    // Textured Resources
    private static Dictionary<Guid, RenderableGpuBinding> m_texturedOpaqueCache = null!;
    private static Dictionary<Guid, RenderableGpuBinding> m_texturedAlphaCache = null!;

    public static void Initialize(IGraphicsDevice graphicsDevice)
    {
        m_graphicsDevice = graphicsDevice;
        
        m_texturedOpaqueCache = new Dictionary<Guid, RenderableGpuBinding>();
        m_texturedAlphaCache = new Dictionary<Guid, RenderableGpuBinding>();
    }

    public static void LoadResources()
    {
        LoadEmbeddedResources();
        CreateSolidQuadResources();
    }

    private static void LoadEmbeddedResources()
    {
        AssetManager.LoadEmbedded<ShaderAsset>("SolidQuad.vert");
        AssetManager.LoadEmbedded<ShaderAsset>("SolidQuad.frag");
        AssetManager.LoadEmbedded<ShaderAsset>("TexturedQuad.vert");
        AssetManager.LoadEmbedded<ShaderAsset>("TexturedQuad.frag");
    }

    private static void CreateSolidQuadResources()
    {
        // Mesh
        // TODO: Use storage to cache meshes
        Mesh mesh = new("Quad");
        mesh.renderState = new MeshRenderState
        {
            topology = PrimitiveTopology.TriangleList
        };
        mesh.SetAttribute("Position", new Vector3[]
        {
            new(-1.0f,  1.0f, 0f),
            new( 1.0f,  1.0f, 0f),
            new(-1.0f, -1.0f, 0f),
            new( 1.0f, -1.0f, 0f)
        });
        mesh.SetIndices([
            0, 1, 2,
            2, 1, 3
        ]);
        
        // Opaque Material
        Material opaqueMat = new("QuadOpaque");
        opaqueMat.renderState = new MaterialRenderState
        {
            blendMode = BlendMode.Opaque,
            depthStencilState = DepthStencilState.DepthOnlyLessEqual
        };
        opaqueMat.shaders = new ShaderProgram();
        opaqueMat.shaders.Add(ResourceDecoder.DecodeBinaries<Shader, ShaderAsset>(
            AssetManager.GetEmbedded<ShaderAsset>("SolidQuad.vert").Resolve()!
        ));
        opaqueMat.shaders.Add(ResourceDecoder.DecodeBinaries<Shader, ShaderAsset>(
            AssetManager.GetEmbedded<ShaderAsset>("SolidQuad.frag").Resolve()!
        ));
        
        // Alpha Material
        Material alphaMat = new("QuadAlpha");
        alphaMat.renderState = new MaterialRenderState
        {
            blendMode = BlendMode.AlphaBlend,
            depthStencilState = DepthStencilState.DepthReadOnlyLessEqual
        };
        alphaMat.shaders = new ShaderProgram();
        alphaMat.shaders.Add(ResourceDecoder.DecodeBinaries<Shader, ShaderAsset>(
            AssetManager.GetEmbedded<ShaderAsset>("SolidQuad.vert").Resolve()!
        ));
        alphaMat.shaders.Add(ResourceDecoder.DecodeBinaries<Shader, ShaderAsset>(
            AssetManager.GetEmbedded<ShaderAsset>("SolidQuad.frag").Resolve()!
        ));
        
        // Per-Object Uniforms
        (string name, Type type)[] perObjectUniforms = [
            ("MVP", typeof(Matrix)),
            ("Color", typeof(Color))
        ];
        
        // Opaque Renderable
        m_quadOpaque = RenderableGpuCompiler.Compile(
            m_graphicsDevice,
            mesh,
            [opaqueMat],
            perObjectUniforms
        );

        // Alpha Renderable
        m_quadAlpha = RenderableGpuCompiler.Compile(
            m_graphicsDevice,
            mesh,
            [alphaMat],
            perObjectUniforms
        );
    }
    
    private static RenderableGpuBinding GetOrCreateTexturedQuadResource(Texture texture, bool opaque)
    {
        var cache = opaque ? m_texturedOpaqueCache : m_texturedAlphaCache;
        if (cache.TryGetValue(texture.guid, out var res))
            return res;

        // Mesh
        // TODO: Use storage to cache meshes
        var mesh = new Mesh("TexturedQuad");
        mesh.renderState = new MeshRenderState { topology = PrimitiveTopology.TriangleList };
        mesh.SetAttribute("Position", new Vector3[]
        {
            new(-1.0f,  1.0f, 0f),
            new( 1.0f,  1.0f, 0f),
            new(-1.0f, -1.0f, 0f),
            new( 1.0f, -1.0f, 0f)
        });
        mesh.SetAttribute("TexCoord0", new Vector2[]
        {
            new(0f, 0f),
            new(1f, 0f),
            new(0f, 1f),
            new(1f, 1f)
        });
        mesh.SetIndices([0, 1, 2, 2, 1, 3]);

        // Materials
        var mat = new Material(opaque ? "TexturedQuadOpaque" : "TexturedQuadAlpha");
        mat.renderState = new MaterialRenderState
        {
            blendMode = opaque ? BlendMode.Opaque : BlendMode.AlphaBlend,
            depthStencilState = opaque ? DepthStencilState.DepthOnlyLessEqual : DepthStencilState.DepthReadOnlyLessEqual
        };

        mat.shaders = new ShaderProgram();
        mat.shaders.Add(ResourceDecoder.DecodeBinaries<Shader, ShaderAsset>(
            AssetManager.GetEmbedded<ShaderAsset>("TexturedQuad.vert").Resolve()!
        ));
        mat.shaders.Add(ResourceDecoder.DecodeBinaries<Shader, ShaderAsset>(
            AssetManager.GetEmbedded<ShaderAsset>("TexturedQuad.frag").Resolve()!
        ));

        mat.SetTexture("MainTex", texture);

        // Per-Object Uniforms
        (string name, Type type)[] perObjectUniforms = [
            ("MVP", typeof(Matrix)),
            ("Color", typeof(Color)),
            ("UVRect", typeof(Vector4))
        ];
        
        res = RenderableGpuCompiler.Compile(
            m_graphicsDevice,
            mesh,
            [mat],
            perObjectUniforms
        );

        cache[texture.guid] = res;
        return res;
    }
    
    public static void DrawQuad(RenderContext ctx, Matrix transform, Color color)
    {
        var mvp = transform * ctx.viewProjection;

        if (MathHelper.AlmostEquals(color.a, 1.0f))
        {
            m_quadOpaque.UpdatePerObject(ctx.commandList, "MVP", mvp);
            m_quadOpaque.UpdatePerObject(ctx.commandList, "Color", color);
            m_quadOpaque.DrawAll(ctx.commandList);
        }
        else
        {
            m_quadAlpha.UpdatePerObject(ctx.commandList, "MVP", mvp);
            m_quadAlpha.UpdatePerObject(ctx.commandList, "Color", color);
            m_quadAlpha.DrawAll(ctx.commandList);
        }
    }
    
    public static void DrawTexturedQuad(RenderContext ctx, Matrix transform, Texture? texture, Vector4 uv, Color color)
    {
        if (texture == null)
        {
            DrawQuad(ctx, transform, color);
            return;
        }
        
        // TODO: Use Texture Asset to detect
        bool opaque = false; 
        
        var res = GetOrCreateTexturedQuadResource(texture, opaque);
        var mvp = transform * ctx.viewProjection;

        res.UpdatePerObject(ctx.commandList, "MVP", mvp);
        res.UpdatePerObject(ctx.commandList, "Color", color);
        res.UpdatePerObject(ctx.commandList, "UVRect", uv);

        res.DrawAll(ctx.commandList);
    }
    
    public static void FillColor(RenderContext ctx, Color color)
    {
        var mvp = Matrix.identity;
        
        m_quadAlpha.UpdatePerObject(ctx.commandList, "MVP", mvp);
        m_quadAlpha.UpdatePerObject(ctx.commandList, "Color", color);
        m_quadAlpha.DrawAll(ctx.commandList);
    }


    public static void CleanResources()
    {
        m_quadOpaque.Dispose();
        m_quadAlpha.Dispose();

        foreach (var kv in m_texturedOpaqueCache) kv.Value.Dispose();
        foreach (var kv in m_texturedAlphaCache) kv.Value.Dispose();

        m_texturedOpaqueCache.Clear();
        m_texturedAlphaCache.Clear();
    }
}
