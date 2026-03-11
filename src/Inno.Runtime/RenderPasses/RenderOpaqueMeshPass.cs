using System.Linq;

using Inno.Core.ECS;
using Inno.Core.Math;
using Inno.Graphics.Pass;
using Inno.Graphics.Renderer;
using Inno.Graphics.Targets;
using Inno.Runtime.Component;

namespace Inno.Runtime.RenderPasses;

/// <summary>
/// Render all MeshRenderer (opaque) using Renderer3D (unlit).
/// </summary>
public class RenderOpaqueMeshPass : RenderPass
{
    public override RenderPassTag orderTag => RenderPassTag.Geometry;

    public override void OnRender(RenderContext ctx)
    {
        var scene = SceneManager.GetActiveScene();
        if (scene == null) return;

        foreach (var mr in scene.GetAllComponents<MeshRenderer>()
                     .Where(mr => mr.isActive)
                     .Where(mr => mr.mesh != null))
        {
            // Model matrix (TRS)
            // 你 Transform 是 3D 的：worldPosition / worldRotation / worldScale 都是 Vector3/Quaternion
            var t =
                Matrix.CreateScale(mr.transform.worldScale) *
                Matrix.CreateFromQuaternion(mr.transform.worldRotation) *
                Matrix.CreateTranslation(mr.transform.worldPosition);

            Renderer3D.DrawMesh(ctx, mr.mesh!, t, mr.color);
        }
    }
}