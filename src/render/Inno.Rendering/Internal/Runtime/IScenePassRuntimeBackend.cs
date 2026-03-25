using Inno.Core.Mathematics;
using Inno.Graphics;

namespace Inno.Rendering;

internal interface IScenePassRuntimeBackend
{
    IGraphicsCommandList commandList { get; }

    bool shadowMapReady { get; }

    IGraphicsResourceSet? shadowResourceSet { get; }

    IGraphicsResourceSet fallbackShadowResourceSet { get; }

    void EnsureMainRenderPassStarted();

    void ApplyGlobalLightUniform(RenderScene scene);

    void ApplyCameraUniform(Camera camera);

    void ApplyShadowUniforms(RenderScene scene);

    void ApplyShadowReceiverUniform(Renderable renderable, Material material);

    bool TryResolveDrawable(Renderable renderable, out Mesh mesh, out Material material, out Transform transform);

    RuntimeGpuMesh GetOrCreateMesh(Mesh mesh);

    IGraphicsRenderPipeline GetOrCreatePipeline(Material material, IGraphicsInputLayout inputLayout, RenderItemFilter filter);

    IGraphicsResourceSet GetOrCreateResourceSet(Renderable renderable, Material material);

    void BindMaterialParameters(Renderable renderable, Material material);

    float GetAspectRatio(RenderView view, RenderTarget target);

    void WriteColumnMajor(Matrix matrix, Span<float> output);
}
