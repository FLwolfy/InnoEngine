using System;
using System.Collections.Generic;
using Inno.Platform.Graphics;

namespace Inno.Engine.Graphics.Targets;

public sealed class RenderTargetPool
{
    private readonly Dictionary<string, RenderTarget> m_targets = new();
    private readonly IGraphicsDevice m_device;

    internal RenderTargetPool(IGraphicsDevice device)
    {
        m_device = device;
        m_targets["main"] = new RenderTarget(new RenderContext(device, device.swapchainFrameBuffer));
    }

    public RenderTarget? Get(string name) => m_targets.GetValueOrDefault(name);
    public RenderTarget GetMain() => m_targets["main"];

    public RenderTarget Create(string name, FrameBufferDescription desc)
    {
        if (m_targets.ContainsKey(name)) throw new InvalidOperationException($"Already exists a framebuffer named {name}!");
        var result = new RenderTarget(m_device, desc);
        m_targets[name] = result;
        return result;
    }

    public void Release(string name)
    {
        if (m_targets.TryGetValue(name, out var rt))
        {
            rt.Dispose();
            m_targets.Remove(name);
        }
    }

    public void Clear()
    {
        foreach (var rt in m_targets.Values) rt.Dispose();
        m_targets.Clear();
    }
}