using Inno.Assets;
using Inno.Core.Application;
using Inno.Core.Events;
using Inno.Core.Layers;
using Inno.Core.Logging;
using Inno.Graphics;
using Inno.Graphics.Renderer;
using Inno.Platform;
using Inno.Platform.Graphics;
using Inno.Platform.Window;

namespace Inno.Runtime.Core;

public abstract class EngineCore
{
    private readonly PlatformRuntime m_platform;
    private readonly Shell m_gameShell;
    private readonly LayerStack m_layerStack;
    private readonly FileLogSink m_fileSink;
    
    protected EngineCore()
    {
        // Initialize platforms
        m_platform = PlatformAPI.CreatePlatform
        (
            new WindowInfo
            {
                name = "Main Window",
                x = 0,
                y = 0,
                width = 2180,
                height = 1080,
                flags = WindowFlags.AllowHighDpi | WindowFlags.Resizable | WindowFlags.Decorated
            }, 
            PlatformBackend.Veldrid_Sdl2,
            GraphicsBackend.Metal
        );

        // Initialize lifecycle
        m_gameShell = new Shell();
        m_layerStack = new LayerStack();
        
        // Initialize Asset
        AssetManager.Initialize(
            assetDir: "Project/Assets",
            binDir: "Project/Library"
        );
        
        // Initialize Logging
        m_fileSink = new FileLogSink("Project/Logs", 5 * 1024 * 1024, 20);
        LogManager.SetMinimumLevel(LogLevel.Debug);
        LogManager.RegisterSink(m_fileSink);
        
        // Initialize Render
        RenderGraphics.Initialize(m_platform.graphicsDevice);
        
        // Initialization Callbacks
        m_gameShell.SetOnLoad(OnLoad);
        m_gameShell.SetOnSetup(OnSetup);
        m_gameShell.SetOnStep(OnStep);
        m_gameShell.SetOnEvent(OnEvent);
        m_gameShell.SetOnDraw(OnDraw);
        m_gameShell.SetOnClose(OnClose);
    }
    
    private void OnLoad()
    {
        // InnoAsset Initialization
        AssetManager.LoadAllFromAssetDirectory();
        
        // Graphics Resources
        Renderer2D.LoadResources();
        Renderer3D.LoadResources();
    }

    private void OnSetup()
    {
        Setup();
        RegisterLayers(m_layerStack);
    }

    private void OnStep()
    {
        // Layer Update
        m_layerStack.OnUpdate();
    }

    private void OnEvent(EventDispatcher dispatcher)
    {
        m_platform.windowSystem.mainWindow.PumpEvents(dispatcher);
        
        var shouldCloseWindow = false;
        dispatcher.Dispatch(e =>
        {
            m_layerStack.OnEvent(e);
            if (e.type == EventType.WindowClose) shouldCloseWindow = true;
        });
        if (shouldCloseWindow) End();
    }

    private void OnDraw()
    {
        // Layer Render
        m_layerStack.OnRender();
        
        // Swap Buffers
        m_platform.windowSystem.SwapWindowBuffers(m_platform.windowSystem.mainWindow);
        foreach (var extraWindow in m_platform.windowSystem.extraWindows)
        {
            m_platform.windowSystem.SwapWindowBuffers(extraWindow);
        }
    }

    private void OnClose()
    {
        // Clean Graphics Cache
        RenderGraphics.Clear();
        
        // Dispose Resources
        m_fileSink.Dispose();
        
        // Assets
        AssetManager.Shutdown();
    }
    
    /// <summary>
    /// Get the platform implemented for this engine core.
    /// </summary>
    public PlatformRuntime GetImplementedPlatform() => m_platform;
    
    /// <summary>
    /// Starts the main loop of the engine.
    /// </summary>
    public void Run()
    {
        m_gameShell.Run();
    }

    /// <summary>
    /// Ends the engine core loop.
    /// </summary>
    public void End()
    {
        m_gameShell.Terminate();
    }

    /// <summary>
    /// Sets up the engine core.
    /// </summary>
    protected abstract void Setup();

    /// <summary>
    /// Registers engine layers.
    /// </summary>
    protected abstract void RegisterLayers(LayerStack layerStack);
}
