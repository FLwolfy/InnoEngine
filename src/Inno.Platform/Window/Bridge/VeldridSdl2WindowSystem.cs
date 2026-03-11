using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Inno.Core.Math;
using Inno.Platform.Graphics;
using Inno.Platform.Graphics.Bridge;

using InnoGraphicsBackend = Inno.Platform.Graphics.GraphicsBackend;

using Veldrid;
using Veldrid.StartupUtilities;

namespace Inno.Platform.Window.Bridge;

internal class VeldridSdl2WindowSystem : IWindowSystem
{
    // Graphics
    private readonly Dictionary<IWindow, Swapchain> m_windowSwapchains;
    private readonly IGraphicsDevice m_graphicsDevice; // This should be disposed outside.

    // Window Properties
    public IWindow mainWindow { get; }
    public IEnumerable<IWindow> extraWindows => m_windowSwapchains.Keys.Where(w => w != mainWindow);

    internal VeldridSdl2WindowSystem(
        in WindowInfo mainWindowInfo, 
        in InnoGraphicsBackend graphicsBackend,
        out VeldridGraphicsDevice graphicsDevice)
    {
        var mwInner = new VeldridSdl2Window(mainWindowInfo);
        graphicsDevice = new VeldridGraphicsDevice(mwInner, graphicsBackend);
        mwInner.frameBuffer = graphicsDevice.swapchainFrameBuffer;

        m_graphicsDevice = graphicsDevice;
        mainWindow = mwInner;
        m_windowSwapchains = new Dictionary<IWindow, Swapchain>
        {
            [mainWindow] = graphicsDevice.inner.MainSwapchain
        };
    }
    
    public IWindow CreateWindow(in WindowInfo info)
    {
        var window = new VeldridSdl2Window(info);
        var vgd = (m_graphicsDevice as VeldridGraphicsDevice)!.inner;
        var size = VeldridSdl2HiDpi.GetFramebufferSize(window.inner);
        
        SwapchainSource scSource = VeldridStartup.GetSwapchainSource(window.inner);
        SwapchainDescription scDesc = new SwapchainDescription(
            scSource, 
            (uint)size.x, 
            (uint)size.y, 
            vgd.SwapchainFramebuffer.OutputDescription.DepthAttachment?.Format,
            true, 
            false
        );
        
        var swapchain = vgd.ResourceFactory.CreateSwapchain(scDesc);
        swapchain.Resize((uint)size.x, (uint)size.y);
        window.Resized += () =>
        {
            var s = VeldridSdl2HiDpi.GetFramebufferSize(window.inner);
            swapchain.Resize((uint)s.x, (uint)s.y);
        };
        m_windowSwapchains[window] = swapchain;
        window.frameBuffer = new VeldridFrameBuffer(((VeldridGraphicsDevice)m_graphicsDevice).inner, swapchain.Framebuffer);
        
        return window;
    }

    public void DestroyWindow(IWindow window)
    {
        var swapchain = m_windowSwapchains[window];
        swapchain.Dispose();
        m_windowSwapchains.Remove(window);
        window.Dispose();
    }
    
    public void SwapWindowBuffers(IWindow window)
    {
        if (window == mainWindow)
        {
            (m_graphicsDevice as VeldridGraphicsDevice)!.inner.SwapBuffers();
        }
        else
        {
            SwapExtraWindowBuffers(window);
        }
    }

    private void SwapExtraWindowBuffers(IWindow window)
    {
        var vgd = (m_graphicsDevice as VeldridGraphicsDevice)!.inner;
        
        // Check occlusion state
        var focusWindows = m_windowSwapchains.Keys.Where(w => w.focused).ToArray();
        if (!focusWindows.Contains(window))
        {
            foreach (var focus in focusWindows)
            {
                if (focus.bounds.Contains(window.bounds)) return;
            }
        }
        
        vgd.SwapBuffers(m_windowSwapchains[window]);
    }
    
    public void Dispose()
    {
        mainWindow.Dispose();

        foreach (var windowSwapchain in m_windowSwapchains.Values)
        {
            windowSwapchain.Dispose();
        }
        m_windowSwapchains.Clear();
    }

}