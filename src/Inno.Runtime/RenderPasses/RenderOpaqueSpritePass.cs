using System.Linq;

using Inno.Core.ECS;
using Inno.Core.Math;
using Inno.Graphics.Pass;
using Inno.Graphics.Renderer;
using Inno.Graphics.Targets;
using Inno.Runtime.Component;

namespace Inno.Runtime.RenderPasses;

public class RenderOpaqueSpritePass() : RenderPass
{
    public override RenderPassTag orderTag => RenderPassTag.Geometry;

    public override void OnRender(RenderContext ctx)
    {
        var scene = SceneManager.GetActiveScene();
        if (scene == null) return;
        var camera = scene.GetMainCamera();
        if (camera == null) return;

        foreach (var sr in scene.GetAllComponents<SpriteRenderer>()
                     .Where(sr => sr.isActive)
                     .Where(sr => sr.opacity >= 1f && sr.color.a >= 1f)
                     .Where(sr => sr.sprite.texture == null)
                     .OrderByDescending(sr => sr.layerDepth))
        {
            var t = Matrix.CreateScale(new Vector3(sr.sprite.size.x * sr.transform.worldScale.x, sr.sprite.size.y * sr.transform.worldScale.y, 1)) *
                    Matrix.CreateFromQuaternion(sr.transform.worldRotation) *
                    Matrix.CreateTranslation(sr.transform.worldPosition);

            // TODO: Add Opaque Sprite Check
            Renderer2D.DrawQuad(ctx, t, sr.color);
        }
    }
}