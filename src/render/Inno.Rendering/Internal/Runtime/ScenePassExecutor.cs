using Inno.Core.Mathematics;
using Inno.Graphics;

namespace Inno.Rendering;

internal sealed class ScenePassExecutor
{
    public void Execute(IScenePassRuntimeBackend runtime, RenderPipelineContext context, RenderList renderList, RenderItemFilter filter)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(renderList);

        runtime.EnsureMainRenderPassStarted();

        var view = context.request.view;
        var target = context.request.target;
        var overlayPass = filter is RenderItemFilter.Ui or RenderItemFilter.Gizmo or RenderItemFilter.PostProcess;
        var viewMatrix = overlayPass ? Matrix.identity : view.camera.GetViewMatrix();
        var projectionMatrix = overlayPass
            ? Matrix.identity
            : view.camera.GetProjectionMatrix(runtime.GetAspectRatio(view, target));

        Span<float> viewRaw = stackalloc float[16];
        Span<float> projRaw = stackalloc float[16];
        Span<float> modelRaw = stackalloc float[16];
        runtime.WriteColumnMajor(viewMatrix, viewRaw);
        runtime.WriteColumnMajor(projectionMatrix, projRaw);
        runtime.commandList.SetViewProjection(viewRaw, projRaw);

        runtime.ApplyGlobalLightUniform(context.request.scene);
        runtime.ApplyCameraUniform(view.camera);
        runtime.ApplyShadowUniforms(context.request.scene);

        Span<float> ambientRaw = stackalloc float[4];
        var ambient = context.request.scene.environment.ambientColor;
        ambientRaw[0] = ambient.r;
        ambientRaw[1] = ambient.g;
        ambientRaw[2] = ambient.b;
        ambientRaw[3] = context.request.scene.environment.ambientIntensity;
        runtime.commandList.SetGlobalVector4("u_ambient", ambientRaw);

        foreach (var item in renderList.items)
        {
            if (!runtime.TryResolveDrawable(item.renderable, out var mesh, out var material, out var transform))
            {
                continue;
            }

            var gpuMesh = runtime.GetOrCreateMesh(mesh);
            var pipeline = runtime.GetOrCreatePipeline(material, gpuMesh.inputLayout, filter);
            runtime.BindMaterialParameters(item.renderable, material);

            runtime.commandList.SetPipeline(pipeline);
            runtime.commandList.SetVertexBuffer(gpuMesh.vertexBuffer);
            var baseResourceSet = runtime.GetOrCreateResourceSet(item.renderable, material);
            runtime.commandList.SetResourceSet(0, baseResourceSet);
            if (runtime.shadowMapReady && material.receiveShadows && runtime.shadowResourceSet is not null)
            {
                runtime.commandList.SetResourceSet(1, runtime.shadowResourceSet);
            }
            else
            {
                runtime.commandList.SetResourceSet(1, runtime.fallbackShadowResourceSet);
            }

            runtime.ApplyShadowReceiverUniform(item.renderable, material);

            if (filter == RenderItemFilter.Skybox)
            {
                transform = new Transform
                {
                    position = view.camera.transform.position,
                    rotation = Quaternion.identity,
                    scale = new Vector3(50.0f, 50.0f, 50.0f)
                };
            }

            runtime.WriteColumnMajor(transform.ToMatrix(), modelRaw);
            runtime.commandList.SetModelTransform(modelRaw);

            if (gpuMesh.indexCount > 0)
            {
                runtime.commandList.SetIndexBuffer(gpuMesh.indexBuffer!);
                runtime.commandList.DrawIndexed(new DrawIndexedArguments(gpuMesh.indexCount));
                context.frame.statistics.drawCalls++;
            }
            else if (gpuMesh.vertexCount > 0)
            {
                runtime.commandList.Draw(gpuMesh.vertexCount);
                context.frame.statistics.drawCalls++;
            }
        }
    }
}
