using Inno.Core.Mathematics;
using Inno.Graphics;

namespace Inno.Rendering;

internal sealed class ShadowPassExecutor
{
    public void Execute(IShadowPassRuntimeBackend runtime, RenderPipelineContext context, RenderList renderList)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(renderList);

        if (!context.request.scene.settings.enableShadows)
        {
            return;
        }

        var shadowSettings = runtime.ResolveDirectionalShadowSettings(context.request.scene);
        if (!shadowSettings.enabled)
        {
            return;
        }

        runtime.EnsureShadowResources(shadowSettings.resolution);
        var shadowRenderTarget = runtime.shadowRenderTarget;
        if (shadowRenderTarget is null || !runtime.hasShadowSamplingResource)
        {
            return;
        }

        if (!runtime.TryBuildDirectionalShadowCascades(context.request, context.request.scene, renderList.items, shadowSettings))
        {
            return;
        }

        var commandList = runtime.commandList;
        var clearShadow = new ClearValue(1.0f, 1.0f, 1.0f, 1.0f, 1.0f, 0);
        commandList.BeginRenderPass(shadowRenderTarget, clearShadow);
        Span<float> modelRaw = stackalloc float[16];
        Span<float> viewRaw = stackalloc float[16];
        Span<float> projRaw = stackalloc float[16];
        var tileWidth = Math.Max(1, shadowRenderTarget.width / runtime.shadowCascadeCount);
        for (var cascadeIndex = 0; cascadeIndex < runtime.shadowCascadeCount; cascadeIndex++)
        {
            var cascade = runtime.GetShadowCascade(cascadeIndex);
            commandList.SetViewport(new GraphicsViewport(tileWidth * cascadeIndex, 0, tileWidth, shadowRenderTarget.height));
            runtime.SetMatrixRows("u_shadowViewProj", cascade.viewProjection);

            runtime.WriteColumnMajor(cascade.view, viewRaw);
            runtime.WriteColumnMajor(cascade.projection, projRaw);
            commandList.SetViewProjection(viewRaw, projRaw);

            foreach (var item in renderList.items)
            {
                if (!runtime.TryResolveDrawable(item.renderable, out var mesh, out var material, out var transform))
                {
                    continue;
                }

                if (item.renderable is not MeshRenderable)
                {
                    continue;
                }

                var gpuMesh = runtime.GetOrCreateMesh(mesh);
                var pipeline = runtime.GetOrCreatePipeline(material, gpuMesh.inputLayout, RenderItemFilter.ShadowCasters);
                commandList.SetPipeline(pipeline);
                commandList.SetVertexBuffer(gpuMesh.vertexBuffer);

                runtime.WriteColumnMajor(transform.ToMatrix(), modelRaw);
                commandList.SetModelTransform(modelRaw);

                if (gpuMesh.indexCount > 0)
                {
                    commandList.SetIndexBuffer(gpuMesh.indexBuffer!);
                    commandList.DrawIndexed(new DrawIndexedArguments(gpuMesh.indexCount));
                    context.frame.statistics.drawCalls++;
                }
                else if (gpuMesh.vertexCount > 0)
                {
                    commandList.Draw(gpuMesh.vertexCount);
                    context.frame.statistics.drawCalls++;
                }
            }
        }

        commandList.EndRenderPass();
        runtime.MarkShadowMapReady();
    }
}
