using Inno.Core.Mathematics;
using Inno.Graphics;

namespace Inno.Rendering;

internal interface IShadowPassRuntimeBackend
{
    IGraphicsCommandList commandList { get; }

    IGraphicsRenderTarget? shadowRenderTarget { get; }

    bool hasShadowSamplingResource { get; }

    int shadowCascadeCount { get; }

    ShadowCascadeData GetShadowCascade(int cascadeIndex);

    void MarkShadowMapReady();

    LightShadowSettings ResolveDirectionalShadowSettings(RenderScene scene);

    void EnsureShadowResources(int requestedSize);

    bool TryBuildDirectionalShadowCascades(
        RenderRequest request,
        RenderScene scene,
        IReadOnlyList<RenderItem> casterItems,
        LightShadowSettings shadowSettings);

    bool TryResolveDrawable(Renderable renderable, out Mesh mesh, out Material material, out Transform transform);

    RuntimeGpuMesh GetOrCreateMesh(Mesh mesh);

    IGraphicsRenderPipeline GetOrCreatePipeline(Material material, IGraphicsInputLayout inputLayout, RenderItemFilter filter);

    void WriteColumnMajor(Matrix matrix, Span<float> output);

    void SetMatrixRows(string uniformPrefix, Matrix matrix);
}
