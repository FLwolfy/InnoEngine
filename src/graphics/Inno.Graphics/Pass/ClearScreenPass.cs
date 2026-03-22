using Inno.Core.Mathematics;
using Inno.Engine.Graphics.Renderer;
using Inno.Engine.Graphics.Targets;

namespace Inno.Engine.Graphics.Pass;

/// <summary>
/// Clears the screen before rendering.
/// </summary>
public class ClearScreenPass(Color? clearColor = null) : RenderPass
{
    private static readonly Color DEFAULT_CLEAR_COLOR = Color.CORNFLOWERBLUE;
    private readonly Color m_clearColor = clearColor ?? DEFAULT_CLEAR_COLOR;
    
    public override RenderPassTag orderTag => RenderPassTag.ClearScreen;

    public override void OnRender(RenderContext ctx)
    {
        Renderer2D.FillColor(ctx, m_clearColor);
    }
}