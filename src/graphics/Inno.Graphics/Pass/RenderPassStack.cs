using System;
using System.Collections.Generic;
using Inno.Engine.Graphics.Targets;

namespace Inno.Engine.Graphics.Pass;

/// <summary>
/// System to update and render all Renderer components in scene.
/// </summary>
public class RenderPassStack
{
    private readonly Dictionary<RenderPassTag, List<RenderPass>> m_passes = new();

    /// <summary>
    /// Push a render pass onto the stack for the given tag.
    /// </summary>
    public void PushPass(RenderPass renderPass)
    {
        if (!m_passes.TryGetValue(renderPass.orderTag, out var list))
        {
            list = new List<RenderPass>();
            m_passes[renderPass.orderTag] = list;
        }

        list.Add(renderPass);
    }

    /// <summary>
    /// Pop the last render pass from the stack for the given tag.
    /// Returns true if a pass was removed, false if stack was empty.
    /// </summary>
    public bool PopPass(RenderPassTag tag)
    {
        if (m_passes.TryGetValue(tag, out var list) && list.Count > 0)
        {
            list.RemoveAt(list.Count - 1);
            if (list.Count == 0)
                m_passes.Remove(tag);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Render all passes in order of RenderPassTag enum.
    /// </summary>
    public void OnRender(RenderContext ctx)
    {
        foreach (RenderPassTag tag in Enum.GetValues(typeof(RenderPassTag)))
        {
            if (m_passes.TryGetValue(tag, out var list))
            {
                foreach (var pass in list)
                {
                    pass.OnRender(ctx);
                }
            }
        }
    }
}