using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using Inno.Core.Events;
using Inno.Core.Math;
using Inno.Platform.Graphics;
using Inno.Platform.Window;

using ImGuiNET;
using Inno.Platform.Display;
using Color = Inno.Core.Math.Color;

namespace Inno.ImGui.Backend;

/// <summary>
/// ImGui.NET Bridge Renderer for Veldrid.
/// This is modified by the original ImGuiRenderer from Veldrid.ImGui.
/// </summary>
internal sealed class ImGuiNETController : IDisposable
{
    private readonly IWindowSystem m_windowSystem;
    private readonly IDisplaySystem m_displaySystem;
    private readonly IGraphicsDevice m_graphicsDevice;
    private readonly Assembly m_assembly;
    private readonly ImGuiColorSpaceHandling m_colorSpaceHandling;
    private bool m_frameBegun;
    
    // Window Info
    private readonly ImGuiNETWindow m_mainImGuiWindow;
    
    // Window Holder
    private readonly Dictionary<uint, ImGuiNETWindow> m_windowHolders = new();
    
    // Window Delegate
    private Platform_CreateWindow m_createWindow = null!;
    private Platform_DestroyWindow m_destroyWindow = null!;
    private Platform_GetWindowPos m_getWindowPos = null!;
    private Platform_ShowWindow m_showWindow = null!;
    private Platform_SetWindowPos m_setWindowPos = null!;
    private Platform_GetWindowSize m_getWindowSize = null!;
    private Platform_SetWindowSize m_setWindowSize = null!;
    private Platform_GetWindowFocus m_getWindowFocus = null!;
    private Platform_SetWindowFocus m_setWindowFocus = null!;
    private Platform_GetWindowMinimized m_getWindowMinimized = null!;
    private Platform_SetWindowTitle m_setWindowTitle = null!;

    // GPU resources
    private IUniformBuffer? m_projBuffer;
    private ITexture? m_fontTexture;
    private ISampler? m_fontSampler;
    private IShader? m_vertexShader;
    private IShader? m_fragmentShader;
    private IPipelineState? m_pipeline;
    private IResourceSet? m_set0;
    private IResourceSet? m_fontTextureSet;

    // Texture bindings (set = 1)
    private readonly Dictionary<ITexture, IntPtr> m_idsByTexture = new();
    private readonly Dictionary<IntPtr, (ITexture tex, IResourceSet set1)> m_bindingById = new();
    private readonly IntPtr m_fontAtlasId = 1;
    private int m_lastAssignedId = 100;

    // Fonts
    private readonly ImFontConfigPtr m_baseCfg;
    private readonly ImFontConfigPtr m_mergeCfg;
    private readonly List<string> m_iconNames = new();
    private readonly Dictionary<string, (IntPtr, int)> m_fontCache = new();
    private readonly Dictionary<string, (float, (ushort, ushort))> m_iconSizeCache = new();
    private readonly Dictionary<string, IntPtr> m_rangePtrCache = new();

    #region Init Resources
    
    public ImGuiNETController(
        IWindowSystem windowSystem,
        IDisplaySystem displaySystem,
        IGraphicsDevice graphicsDevice,
        ImGuiColorSpaceHandling colorSpaceHandling)
    {
        m_windowSystem = windowSystem;
        m_displaySystem = displaySystem;
        m_graphicsDevice = graphicsDevice;
        m_colorSpaceHandling = colorSpaceHandling;
        m_assembly = typeof(ImGuiNETController).GetTypeInfo().Assembly;

        var ctx = ImGuiNET.ImGui.CreateContext();
        ImGuiNET.ImGui.SetCurrentContext(ctx);

        // IO
        var io = ImGuiNET.ImGui.GetIO();
        io.ConfigWindowsMoveFromTitleBarOnly = true;

        // Config Flags
        io.ConfigFlags |= ImGuiConfigFlags.DockingEnable;
        io.ConfigFlags |= ImGuiConfigFlags.ViewportsEnable;

        // Backend Flags
        io.BackendFlags |= ImGuiBackendFlags.HasMouseCursors;
        io.BackendFlags |= ImGuiBackendFlags.HasSetMousePos;
        io.BackendFlags |= ImGuiBackendFlags.PlatformHasViewports;
        io.BackendFlags |= ImGuiBackendFlags.RendererHasViewports;
        
        // Window Platform Interface
        ImGuiPlatformIOPtr platformIo = ImGuiNET.ImGui.GetPlatformIO();
        ImGuiViewportPtr mainViewport = platformIo.Viewports[0];
        m_mainImGuiWindow = new ImGuiNETWindow(windowSystem, graphicsDevice, mainViewport, true);
        SetupPlatformIO(platformIo);

        // Font configs
        unsafe
        {
            m_baseCfg = ImGuiNative.ImFontConfig_ImFontConfig();
            m_baseCfg.FontDataOwnedByAtlas = false;
            m_baseCfg.PixelSnapH = true;
            m_baseCfg.OversampleH = 2;
            m_baseCfg.OversampleV = 2;

            m_mergeCfg = ImGuiNative.ImFontConfig_ImFontConfig();
            m_mergeCfg.MergeMode = true;
            m_mergeCfg.FontDataOwnedByAtlas = false;
            m_mergeCfg.PixelSnapH = true;
        }
        io.Fonts.AddFontDefault(); // In case of no other fonts
        io.Fonts.Flags |= ImFontAtlasFlags.NoBakedLines;

        CreateDeviceResources();
        SetPerFrameImGuiData(1f / 60f);
    }
    
      private void CreateDeviceResources()
    {
        // Projection uniform
        m_projBuffer = m_graphicsDevice.CreateUniformBuffer("Projection", typeof(Matrix));

        // Sampler (for textures)
        m_fontSampler = m_graphicsDevice.CreateSampler(new SamplerDescription
        {
            filter = SamplerFilter.Linear,
            addressU = SamplerAddressMode.Clamp,
            addressV = SamplerAddressMode.Clamp
        });

        // Shaders
        var (vs, fs) = m_graphicsDevice.CreateVertexFragmentShader(
            new ShaderDescription
            {
                stage = ShaderStage.Vertex,
                sourceBytes = LoadEmbeddedShaderCode("imgui-vertex", ShaderStage.Vertex, m_colorSpaceHandling)
            },
            new ShaderDescription
            {
                stage = ShaderStage.Fragment,
                sourceBytes = LoadEmbeddedShaderCode("imgui-frag", ShaderStage.Fragment, m_colorSpaceHandling)
            });

        m_vertexShader = vs;
        m_fragmentShader = fs;

        // IMPORTANT:
        // Create font texture BEFORE pipeline, so we can define set=1 layout with 1 texture.
        RecreateFontDeviceTexture();

        // Pipeline
        m_pipeline = m_graphicsDevice.CreatePipelineState(new PipelineStateDescription
        {
            vertexShader = m_vertexShader,
            fragmentShader = m_fragmentShader,
            vertexLayoutTypes = [typeof(Vector2), typeof(Vector2), typeof(Color)],

            blendMode = BlendMode.AlphaBlend,
            primitiveTopology = PrimitiveTopology.TriangleList,
            depthStencilState = DepthStencilState.Disabled,

            // set = 0 and set = 1
            resourceLayoutSpecifiers =
            [
                // set 0: projection + sampler
                new ResourceSetBinding
                {
                    shaderStages = ShaderStage.Vertex | ShaderStage.Fragment,
                    uniformBuffers = [m_projBuffer],
                    textures = [],
                    samplers = [m_fontSampler]
                },

                // set 1: ONE texture (ImGui draw command binds texture id here)
                new ResourceSetBinding
                {
                    shaderStages = ShaderStage.Fragment,
                    uniformBuffers = [],
                    textures = [m_fontTexture!],
                    samplers = []
                }
            ]
        });

        // set0 (projection + sampler)
        m_set0 = m_graphicsDevice.CreateResourceSet(new ResourceSetBinding
        {
            shaderStages = ShaderStage.Vertex | ShaderStage.Fragment,
            uniformBuffers = [m_projBuffer],
            textures = [],
            samplers = [m_fontSampler]
        });
    }
      
    private byte[] LoadEmbeddedShaderCode(
        string name,
        ShaderStage stage,
        ImGuiColorSpaceHandling colorSpaceHandling)
    {
        return GetEmbeddedResourceBytes($"{name}{(stage == ShaderStage.Vertex && colorSpaceHandling == ImGuiColorSpaceHandling.Legacy ? "-legacy" : "")}.spv");
    }

    private byte[] GetEmbeddedResourceBytes(string shortName)
    {
        var resources = m_assembly.GetManifestResourceNames();
        var match = resources.FirstOrDefault(r => r.EndsWith(shortName, StringComparison.OrdinalIgnoreCase));
        if (match == null)
            throw new FileNotFoundException($"Embedded resource '{shortName}' not found. Ensure it exists under ImGui/Assets and is marked as EmbeddedResource.");

        using Stream s = m_assembly.GetManifestResourceStream(match)!;
        byte[] ret = new byte[s.Length];
        s.ReadExactly(ret, 0, (int)s.Length);
        return ret;
    }
    
    #endregion

    #region Window Platform Interface

    public IReadOnlyCollection<ImGuiNETWindow> GetViewportWindows()
    {
        return m_windowHolders.Values;
    }
    
    private void CreateWindow(ImGuiViewportPtr vp)
    {
        m_windowHolders[vp.ID] = new ImGuiNETWindow(m_windowSystem, m_graphicsDevice, vp, false);
    }

    private void DestroyWindow(ImGuiViewportPtr vp)
    {
        if (vp.PlatformUserData != IntPtr.Zero)
        {
            ImGuiNETWindow? window = (ImGuiNETWindow?)GCHandle.FromIntPtr(vp.PlatformUserData).Target;
            m_windowHolders.Remove(vp.ID);
            window?.Dispose();

            vp.PlatformUserData = IntPtr.Zero;
        }
    }

    private void ShowWindow(ImGuiViewportPtr vp)
    {
        ImGuiNETWindow? window = (ImGuiNETWindow?) GCHandle.FromIntPtr(vp.PlatformUserData).Target;
        if (window != null) window.window.Show();
    }

    private unsafe void GetWindowPos(ImGuiViewportPtr vp, System.Numerics.Vector2* outPos)
    {
        ImGuiNETWindow? window = (ImGuiNETWindow?) GCHandle.FromIntPtr(vp.PlatformUserData).Target;
        if (window != null)
        {
            var pos = window.window.position;
            *outPos = new System.Numerics.Vector2(pos.x, pos.y);
        }
    }

    private void SetWindowPos(ImGuiViewportPtr vp, System.Numerics.Vector2 pos)
    {
        ImGuiNETWindow? window = (ImGuiNETWindow?) GCHandle.FromIntPtr(vp.PlatformUserData).Target;
        if (window != null)
        {
            window.window.position = new Vector2Int((int)pos.X, (int)pos.Y);
        }
    }

    private void SetWindowSize(ImGuiViewportPtr vp, System.Numerics.Vector2 size)
    {
        ImGuiNETWindow? window = (ImGuiNETWindow?) GCHandle.FromIntPtr(vp.PlatformUserData).Target;
        if (window != null)
        {
            window.window.size = new Vector2Int((int)size.X, (int)size.Y);
        }
    }

    private unsafe void GetWindowSize(ImGuiViewportPtr vp, System.Numerics.Vector2* outSize)
    {
        ImGuiNETWindow? window = (ImGuiNETWindow?) GCHandle.FromIntPtr(vp.PlatformUserData).Target;
        if (window != null)
        {
            var size = window.window.size;
            *outSize = new System.Numerics.Vector2(size.x, size.y);
        }
    }

    private void SetWindowFocus(ImGuiViewportPtr vp)
    {
        ImGuiNETWindow? window = (ImGuiNETWindow?) GCHandle.FromIntPtr(vp.PlatformUserData).Target;
        if (window != null) window.window.Focus();
    }

    private byte GetWindowFocus(ImGuiViewportPtr vp)
    {
        ImGuiNETWindow? window = (ImGuiNETWindow?) GCHandle.FromIntPtr(vp.PlatformUserData).Target;
        if (window != null)
        {
            return window.window.focused ? (byte)1 : (byte)0;
        }
        throw new Exception("Window not found");
    }

    private byte GetWindowMinimized(ImGuiViewportPtr vp)
    {
        ImGuiNETWindow? window = (ImGuiNETWindow?) GCHandle.FromIntPtr(vp.PlatformUserData).Target;
        if (window != null)
        {
            return window.window.minimized ? (byte)1 : (byte)0;
        }
        throw new Exception("Window not found");
    }

    private unsafe void SetWindowTitle(ImGuiViewportPtr vp, IntPtr title)
    {
        ImGuiNETWindow? window = (ImGuiNETWindow?) GCHandle.FromIntPtr(vp.PlatformUserData).Target;
        byte* titlePtr = (byte*)title;
        int count = 0;
        while (titlePtr[count] != 0)
        {
            count += 1;
        }

        if (window != null) window.window.title = System.Text.Encoding.ASCII.GetString(titlePtr, count);
    }
    
    private unsafe void SetupPlatformIO(ImGuiPlatformIOPtr platformIo)
    {
        m_createWindow = CreateWindow;
        m_destroyWindow = DestroyWindow;
        m_getWindowPos = GetWindowPos;
        m_showWindow = ShowWindow;
        m_setWindowPos = SetWindowPos;
        m_setWindowSize = SetWindowSize;
        m_getWindowSize = GetWindowSize;
        m_setWindowFocus = SetWindowFocus;
        m_getWindowFocus = GetWindowFocus;
        m_getWindowMinimized = GetWindowMinimized;
        m_setWindowTitle = SetWindowTitle;

        platformIo.Platform_CreateWindow = Marshal.GetFunctionPointerForDelegate(m_createWindow);
        platformIo.Platform_DestroyWindow = Marshal.GetFunctionPointerForDelegate(m_destroyWindow);
        platformIo.Platform_ShowWindow = Marshal.GetFunctionPointerForDelegate(m_showWindow);
        platformIo.Platform_SetWindowPos = Marshal.GetFunctionPointerForDelegate(m_setWindowPos);
        platformIo.Platform_SetWindowSize = Marshal.GetFunctionPointerForDelegate(m_setWindowSize);
        platformIo.Platform_GetWindowSize = Marshal.GetFunctionPointerForDelegate(m_getWindowSize);
        platformIo.Platform_SetWindowFocus = Marshal.GetFunctionPointerForDelegate(m_setWindowFocus);
        platformIo.Platform_GetWindowFocus = Marshal.GetFunctionPointerForDelegate(m_getWindowFocus);
        platformIo.Platform_GetWindowMinimized = Marshal.GetFunctionPointerForDelegate(m_getWindowMinimized);
        platformIo.Platform_SetWindowTitle = Marshal.GetFunctionPointerForDelegate(m_setWindowTitle);
        
        ImGuiNative.ImGuiPlatformIO_Set_Platform_GetWindowPos(platformIo.NativePtr, Marshal.GetFunctionPointerForDelegate(m_getWindowPos));
        ImGuiNative.ImGuiPlatformIO_Set_Platform_GetWindowSize(platformIo.NativePtr, Marshal.GetFunctionPointerForDelegate(m_getWindowSize));
    }
    
    #endregion

    #region Fonts
    
    public void ClearAllFonts()
    {
        var io = ImGuiNET.ImGui.GetIO();
        io.Fonts.Clear();
        
        foreach (var (ptr, _) in m_fontCache.Values) if (ptr != IntPtr.Zero) Marshal.FreeHGlobal(ptr);
        m_fontCache.Clear();
        
        foreach (var p in m_rangePtrCache.Values) if (p != IntPtr.Zero) Marshal.FreeHGlobal(p);
        m_rangePtrCache.Clear();
        
        m_iconNames.Clear();
        m_iconSizeCache.Clear();
    }

    public void RegisterFontIcon(string baseFontFile, float sizePixels, (ushort, ushort) range)
    {
        LoadEmbeddedFontTTF(baseFontFile, out _);
        m_iconNames.Add(baseFontFile);
        m_iconSizeCache[baseFontFile] = (sizePixels, range);
    }

    public ImFontPtr AddIcons()
    {
        var io = ImGuiNET.ImGui.GetIO();
        ImFontPtr iconFont = new ImFontPtr();

        bool first = true;
        foreach (var iconName in m_iconNames)
        {
            var fontCache = m_fontCache[iconName];
            var sizeCache = m_iconSizeCache[iconName];
            var rangePtr = GetOrCreateIconRangePtr(iconName, sizeCache.Item2);

            if (first)
            {
                iconFont = io.Fonts.AddFontFromMemoryTTF(fontCache.Item1, fontCache.Item2, sizeCache.Item1, m_baseCfg, rangePtr);
                first = false;
            }
            else
            {
                io.Fonts.AddFontFromMemoryTTF(fontCache.Item1, fontCache.Item2, sizeCache.Item1, m_mergeCfg, rangePtr);
            }
            
        }
        
        return iconFont;
    }

    public ImFontPtr AddFontBase(string baseFontFile, float sizePixels)
    {
        var io = ImGuiNET.ImGui.GetIO();
        var basePtr = LoadEmbeddedFontTTF(baseFontFile, out var baseLen);
        var baseFont = io.Fonts.AddFontFromMemoryTTF(basePtr, baseLen, sizePixels, m_baseCfg);

        return baseFont;
    }
    
    private IntPtr LoadEmbeddedFontTTF(string shortName, out int length)
    {
        if (m_fontCache.TryGetValue(shortName, out var cached))
        {
            length = cached.Item2;
            return cached.Item1;
        }

        var resources = m_assembly.GetManifestResourceNames();
        var match = resources.FirstOrDefault(r => r.EndsWith(shortName, StringComparison.OrdinalIgnoreCase));
        if (match == null)
            throw new FileNotFoundException($"Embedded resource '{shortName}' not found.");

        byte[] fontData;
        using (var s = m_assembly.GetManifestResourceStream(match)!)
        using (var ms = new MemoryStream())
        {
            s.CopyTo(ms);
            fontData = ms.ToArray();
        }

        var ptr = Marshal.AllocHGlobal(fontData.Length);
        Marshal.Copy(fontData, 0, ptr, fontData.Length);

        m_fontCache[shortName] = (ptr, fontData.Length);

        length = fontData.Length;
        return ptr;
    }
    
    private IntPtr GetOrCreateIconRangePtr(string iconName, (ushort start, ushort end) range)
    {
        string key = $"{iconName}:{range.start:X4}-{range.end:X4}";
        if (m_rangePtrCache.TryGetValue(key, out var p) && p != IntPtr.Zero)
            return p;

        IntPtr mem = Marshal.AllocHGlobal(sizeof(ushort) * 3);

        unsafe
        {
            ushort* u = (ushort*)mem;
            u[0] = range.start;
            u[1] = range.end;
            u[2] = 0;
        }

        m_rangePtrCache[key] = mem;
        return mem;
    }

    public void RebuildFontTexture()
    {
        RecreateFontDeviceTexture();
    }
    
    private void RecreateFontDeviceTexture()
    {
        // Dispose previous
        m_fontTextureSet?.Dispose();
        m_fontTexture?.Dispose();
        m_fontTextureSet = null;
        m_fontTexture = null;

        var io = ImGuiNET.ImGui.GetIO();

        unsafe
        {
            io.Fonts.GetTexDataAsRGBA32(out byte* pixels, out int width, out int height, out int bytesPerPixel);
            if (pixels == null || width <= 0 || height <= 0)
                throw new InvalidOperationException("ImGui font atlas pixels were not generated.");

            int size = width * height * bytesPerPixel;
            var managed = new byte[size];
            Marshal.Copy((IntPtr)pixels, managed, 0, size);

            m_fontTexture = m_graphicsDevice.CreateTexture(new TextureDescription
            {
                width = width,
                height = height,
                mipLevelCount = 1,
                format = PixelFormat.R8_G8_B8_A8_UNorm,
                usage = TextureUsage.Sampled,
                dimension = TextureDimension.Texture2D
            });
            m_fontTexture.Set(ref managed);

            m_fontTextureSet = m_graphicsDevice.CreateResourceSet(new ResourceSetBinding
            {
                shaderStages = ShaderStage.Fragment,
                uniformBuffers = [],
                textures = [m_fontTexture],
                samplers = []
            });

            // Tell ImGui what to use as "font texture id"
            io.Fonts.SetTexID(m_fontAtlasId);
        }
    }

    #endregion

    #region Textures
    public IntPtr GetOrBindTexture(ITexture texture)
    {
        if (texture == null) throw new ArgumentNullException(nameof(texture));

        if (m_idsByTexture.TryGetValue(texture, out var id))
            return id;

        id = ++m_lastAssignedId;

        var set1 = m_graphicsDevice.CreateResourceSet(new ResourceSetBinding
        {
            shaderStages = ShaderStage.Fragment,
            textures = [texture],
            uniformBuffers = [],
            samplers = []
        });

        m_idsByTexture[texture] = id;
        m_bindingById[id] = (texture, set1);
        return id;
    }

    public void UnbindTexture(ITexture texture)
    {
        if (!m_idsByTexture.Remove(texture, out var id)) return;

        if (m_bindingById.TryGetValue(id, out var entry))
        {
            entry.set1.Dispose();
            m_bindingById.Remove(id);
        }
    }
    
    #endregion

    #region Updates
    
    public void Update(float deltaSeconds, EventSnapshot mainSnapshot)
    {
        if (m_frameBegun)
        {
            throw new InvalidOperationException("ImGuiController.Update called while a frame is active.");
        }
        
        SetPerFrameImGuiData(deltaSeconds);
        
        // Inputs
        UpdateImGuiGlobalMouseInput();
        UpdateImGuiMouseWheelInput(mainSnapshot);
        UpdateImGuiKeyInput(mainSnapshot);
        
        foreach (var extraWindowSnapshot in PumpExtraWindowEvents())
        {
            UpdateImGuiMouseWheelInput(extraWindowSnapshot);
            UpdateImGuiKeyInput(extraWindowSnapshot);
        }
        
        // Cursor
        UpdateMouseCursor();
        
        // Monitor
        UpdateMonitors();
        
        // Begins
        m_frameBegun = true;
        ImGuiNET.ImGui.NewFrame();
    }
    
    private void SetPerFrameImGuiData(float deltaSeconds)
    {
        var io = ImGuiNET.ImGui.GetIO();
        var mainWindow = m_windowSystem.mainWindow;
        
        // DisplaySize
        io.DisplaySize = new Vector2(mainWindow.size.x, mainWindow.size.y);
        io.DeltaTime = deltaSeconds;
        
        // Framebuffer
        io.DisplayFramebufferScale = mainWindow.GetFrameBufferScale();
        ImGuiNET.ImGui.GetPlatformIO().Viewports[0].Pos = new Vector2(mainWindow.position.x, mainWindow.position.y);
        ImGuiNET.ImGui.GetPlatformIO().Viewports[0].Size = new Vector2(mainWindow.size.x, mainWindow.size.y);
    }

    private EventSnapshot[] PumpExtraWindowEvents()
    {
        ImVector<ImGuiViewportPtr> viewports = ImGuiNET.ImGui.GetPlatformIO().Viewports;
        var snapshots = new List<EventSnapshot>();

        for (int i = 1; i < viewports.Size; i++)
        {
            ImGuiViewportPtr v = viewports[i];
            var window = (ImGuiNETWindow?)GCHandle.FromIntPtr(v.PlatformUserData).Target;
            if (window != null)
            {
                window.PumpEvents();
                snapshots.Add(window.GetPumpedEvents());
            }
        }

        return snapshots.ToArray();
    }
    
    private unsafe void UpdateMonitors()
    {
        ImGuiPlatformIOPtr platformIo = ImGuiNET.ImGui.GetPlatformIO();
        Marshal.FreeHGlobal(platformIo.NativePtr->Monitors.Data);
        int numMonitors = m_displaySystem.GetDisplayNumber();
        IntPtr data = Marshal.AllocHGlobal(Unsafe.SizeOf<ImGuiPlatformMonitor>() * numMonitors);
        platformIo.NativePtr->Monitors = new ImVector(numMonitors, numMonitors, data);
        for (int i = 0; i < numMonitors; i++)
        {
            Rect displayRect = m_displaySystem.GetDisplayBounds(i);
            Rect usableRect = m_displaySystem.GetUsableDisplayBounds(i);
            ImGuiPlatformMonitorPtr monitor = platformIo.Monitors[i];
            monitor.DpiScale = 1;
            monitor.MainPos = new Vector2(displayRect.x, displayRect.y);
            monitor.MainSize = new Vector2(displayRect.width, displayRect.height);
            monitor.WorkPos = new Vector2(usableRect.x, usableRect.y);
            monitor.WorkSize = new Vector2(usableRect.width, usableRect.height);
        }
    }

    #endregion
    
    #region Render
    
    public void Render(ICommandList commandList)
    {
        if (m_frameBegun)
        {
            m_frameBegun = false;
            ImGuiNET.ImGui.Render();
            RenderImDrawData(ImGuiNET.ImGui.GetDrawData(), commandList, m_mainImGuiWindow);
            
            // Update and Render additional Platform Windows
            if ((ImGuiNET.ImGui.GetIO().ConfigFlags & ImGuiConfigFlags.ViewportsEnable) != 0)
            {
                ImGuiNET.ImGui.UpdatePlatformWindows();
                
                ImGuiPlatformIOPtr platformIo = ImGuiNET.ImGui.GetPlatformIO();
                for (int i = 1; i < platformIo.Viewports.Size; i++)
                {
                    ImGuiViewportPtr vp = platformIo.Viewports[i];
                    ImGuiNETWindow? window = (ImGuiNETWindow?) GCHandle.FromIntPtr(vp.PlatformUserData).Target;
                    if (window != null)
                    {
                        RenderImDrawData(vp.DrawData, commandList, window);
                    }
                }
            }
        }
    }
    
    private void RenderImDrawData(ImDrawDataPtr drawData, ICommandList cl, ImGuiNETWindow viewportWindow)
    {
        // Validation check
        if (m_pipeline == null || m_projBuffer == null || m_set0 == null || m_fontTextureSet == null || drawData.CmdListsCount == 0)
            return;
        
        // MVP uniform
        var pos = drawData.DisplayPos;
        var mvpSys = System.Numerics.Matrix4x4.CreateOrthographicOffCenter(
            pos.X,
            pos.X + drawData.DisplaySize.X,
            pos.Y + drawData.DisplaySize.Y,
            pos.Y,
            -1.0f,
            1.0f);
        
        cl.UpdateUniform(m_projBuffer, ref mvpSys);
        
        // Apply window
        viewportWindow.Apply(drawData, cl);

        // Resources
        cl.SetPipelineState(m_pipeline);
        cl.SetResourceSet(0, m_set0);

        // Draw
        int globalVtxOffset = 0;
        int globalIdxOffset = 0;
        Vector2 clipOff = drawData.DisplayPos;
        Vector2 clipScale = drawData.FramebufferScale;
        for (int n = 0; n < drawData.CmdListsCount; n++)
        {
            var cmdList = drawData.CmdLists[n];

            for (int cmdi = 0; cmdi < cmdList.CmdBuffer.Size; cmdi++)
            {
                var pcmd = cmdList.CmdBuffer[cmdi];

                if (pcmd.UserCallback != IntPtr.Zero)
                {
                    // User callbacks are not supported in this backend.
                    continue;
                }

                // Bind texture (set = 1)
                IResourceSet set1 = ResolveTextureSet(pcmd.TextureId);
                cl.SetResourceSet(1, set1);

                // Project scissor rect into framebuffer space
                var cr = pcmd.ClipRect;
                float clipX1 = (cr.X - clipOff.x) * clipScale.x;
                float clipY1 = (cr.Y - clipOff.y) * clipScale.y;
                float clipX2 = (cr.Z - clipOff.x) * clipScale.x;
                float clipY2 = (cr.W - clipOff.y) * clipScale.y;

                if (clipX2 <= clipX1 || clipY2 <= clipY1)
                    continue;

                // Clamp
                int scX = (int)MathF.Max(0, clipX1);
                int scY = (int)MathF.Max(0, clipY1);
                int scW = (int)MathF.Min(viewportWindow.window.frameBuffer.width - scX, clipX2 - clipX1);
                int scH = (int)MathF.Min(viewportWindow.window.frameBuffer.height - scY, clipY2 - clipY1);
                if (scW <= 0 || scH <= 0) continue;

                cl.SetScissorRect(0, new Rect(scX, scY, scW, scH));
                cl.DrawIndexed(
                    pcmd.ElemCount,
                    (uint)(pcmd.IdxOffset + globalIdxOffset),
                    (int)(pcmd.VtxOffset + globalVtxOffset));
            }

            globalIdxOffset += cmdList.IdxBuffer.Size;
            globalVtxOffset += cmdList.VtxBuffer.Size;
        }
    }
    
    private IResourceSet ResolveTextureSet(IntPtr textureId)
    {
        if (textureId == IntPtr.Zero || textureId == m_fontAtlasId)
            return m_fontTextureSet!;

        if (m_bindingById.TryGetValue(textureId, out var entry))
            return entry.set1;

        // Fallback: font texture
        return m_fontTextureSet!;
    }
    
    #endregion
    
    #region Inputs
        
    private void UpdateImGuiGlobalMouseInput()
    {
        // Mouse Pos
        ImGuiIOPtr io = ImGuiNET.ImGui.GetIO();
        Vector2Int globalMousePos = m_displaySystem.GetGlobalMousePos();
        io.MousePos = new Vector2(globalMousePos.x, globalMousePos.y);
        
        // Mouse Button
        io.MouseDown[0] = false;
        io.MouseDown[1] = false;
        io.MouseDown[2] = false;
        foreach (Input.MouseButton button in m_displaySystem.GetGlobalMouseButton())
        {
            switch (button)
            {
                case Input.MouseButton.Left: io.MouseDown[0] = true; break;
                case Input.MouseButton.Right: io.MouseDown[1] = true; break;
                case Input.MouseButton.Middle: io.MouseDown[2] = true; break;
            }
        }
    }
    
    private void UpdateImGuiMouseWheelInput(EventSnapshot snapshot)
    {
        ImGuiIOPtr io = ImGuiNET.ImGui.GetIO();

        foreach (var e in snapshot.GetEvents(EventType.MouseScrolled))
        {
            if (e is MouseScrolledEvent mw) io.AddMouseWheelEvent(mw.offsetX, mw.offsetY);
        }
    }
    
    private void UpdateImGuiKeyInput(EventSnapshot snapshot)
    {
        var io = ImGuiNET.ImGui.GetIO();
        
        // Input chars
        foreach (var c in snapshot.GetInputChars())
        {
            io.AddInputCharacter(c);
        }

        // Keys
        foreach (var e in snapshot.GetEvents(EventType.KeyPressed))
        {
            if (e is KeyPressedEvent kp)
            {
                if (TryMapKey(kp.key, out var imguiKey))
                {
                    io.AddKeyEvent(imguiKey, true);
                }

                UpdateModifiers(io, kp.modifiers);
            }
        }
        foreach (var e in snapshot.GetEvents(EventType.KeyReleased))
        {
            if (e is KeyReleasedEvent kr)
            {
                if (TryMapKey(kr.key, out var imguiKey))
                    io.AddKeyEvent(imguiKey, false);

                UpdateModifiers(io, kr.modifiers);
            }
        }
    }
    
    private static bool TryMapKey(Input.KeyCode key, out ImGuiKey imguiKey)
    {
        // Letters
        if (key >= Input.KeyCode.A && key <= Input.KeyCode.Z)
        {
            imguiKey = ImGuiKey.A + ((int)key - (int)Input.KeyCode.A);
            return true;
        }

        // Digits
        if (key >= Input.KeyCode.D0 && key <= Input.KeyCode.D9)
        {
            imguiKey = ImGuiKey._0 + ((int)key - (int)Input.KeyCode.D0);
            return true;
        }

        imguiKey = key switch
        {
            Input.KeyCode.Tab => ImGuiKey.Tab,
            Input.KeyCode.LeftArrow => ImGuiKey.LeftArrow,
            Input.KeyCode.RightArrow => ImGuiKey.RightArrow,
            Input.KeyCode.UpArrow => ImGuiKey.UpArrow,
            Input.KeyCode.DownArrow => ImGuiKey.DownArrow,
            Input.KeyCode.PageUp => ImGuiKey.PageUp,
            Input.KeyCode.PageDown => ImGuiKey.PageDown,
            Input.KeyCode.Home => ImGuiKey.Home,
            Input.KeyCode.End => ImGuiKey.End,
            Input.KeyCode.Insert => ImGuiKey.Insert,
            Input.KeyCode.Delete => ImGuiKey.Delete,
            Input.KeyCode.Backspace => ImGuiKey.Backspace,
            Input.KeyCode.Space => ImGuiKey.Space,
            Input.KeyCode.Enter => ImGuiKey.Enter,
            Input.KeyCode.Escape => ImGuiKey.Escape,
            Input.KeyCode.LeftCtrl => ImGuiKey.LeftCtrl,
            Input.KeyCode.RightCtrl => ImGuiKey.RightCtrl,
            Input.KeyCode.LeftShift => ImGuiKey.LeftShift,
            Input.KeyCode.RightShift => ImGuiKey.RightShift,
            Input.KeyCode.LeftAlt => ImGuiKey.LeftAlt,
            Input.KeyCode.RightAlt => ImGuiKey.RightAlt,
            Input.KeyCode.LeftSuper => ImGuiKey.LeftSuper,
            Input.KeyCode.RightSuper => ImGuiKey.RightSuper,
            Input.KeyCode.F1 => ImGuiKey.F1,
            Input.KeyCode.F2 => ImGuiKey.F2,
            Input.KeyCode.F3 => ImGuiKey.F3,
            Input.KeyCode.F4 => ImGuiKey.F4,
            Input.KeyCode.F5 => ImGuiKey.F5,
            Input.KeyCode.F6 => ImGuiKey.F6,
            Input.KeyCode.F7 => ImGuiKey.F7,
            Input.KeyCode.F8 => ImGuiKey.F8,
            Input.KeyCode.F9 => ImGuiKey.F9,
            Input.KeyCode.F10 => ImGuiKey.F10,
            Input.KeyCode.F11 => ImGuiKey.F11,
            Input.KeyCode.F12 => ImGuiKey.F12,
            _ => ImGuiKey.None
        };

        return imguiKey != ImGuiKey.None;
    }

    private static void UpdateModifiers(ImGuiIOPtr io, Input.KeyModifier mods)
    {
        io.AddKeyEvent(ImGuiKey.ModCtrl, (mods & Input.KeyModifier.Control) != 0);
        io.AddKeyEvent(ImGuiKey.ModShift, (mods & Input.KeyModifier.Shift) != 0);
        io.AddKeyEvent(ImGuiKey.ModAlt, (mods & Input.KeyModifier.Alt) != 0);
        io.AddKeyEvent(ImGuiKey.ModSuper, (mods & Input.KeyModifier.Super) != 0);
    }
    
    private void UpdateMouseCursor()
    {
        var io = ImGuiNET.ImGui.GetIO();
        if (io.MouseDrawCursor) return;

        ImGuiMouseCursor cursor = ImGuiNET.ImGui.GetMouseCursor();
        if (cursor == ImGuiMouseCursor.None)
        {
            m_displaySystem.ShowCursor(false);
        }
        else
        {
            m_displaySystem.ShowCursor(true);
            Input.MouseCursor windowCursor = cursor switch
            {
                ImGuiMouseCursor.Arrow => Input.MouseCursor.Arrow,
                ImGuiMouseCursor.TextInput => Input.MouseCursor.TextInput,
                ImGuiMouseCursor.ResizeAll => Input.MouseCursor.ResizeAll,
                ImGuiMouseCursor.ResizeNS => Input.MouseCursor.ResizeNS,
                ImGuiMouseCursor.ResizeEW => Input.MouseCursor.ResizeEW,
                ImGuiMouseCursor.ResizeNESW => Input.MouseCursor.ResizeNESW,
                ImGuiMouseCursor.ResizeNWSE => Input.MouseCursor.ResizeNWSE,
                ImGuiMouseCursor.Hand => Input.MouseCursor.Hand,
                _ => Input.MouseCursor.Arrow
            };
            
            m_displaySystem.SetCursor(windowCursor);
        }
    }
    
    #endregion

    public void Dispose()
    {
        // Texture bindings
        foreach (var kv in m_bindingById.Values)
            kv.set1.Dispose();
        m_bindingById.Clear();
        m_idsByTexture.Clear();

        m_fontTextureSet?.Dispose();
        m_fontTexture?.Dispose();
        m_set0?.Dispose();

        m_pipeline?.Dispose();
        m_vertexShader?.Dispose();
        m_fragmentShader?.Dispose();

        m_fontSampler?.Dispose();
        m_projBuffer?.Dispose();

        unsafe
        {
            ImGuiNative.ImFontConfig_destroy(m_baseCfg);
            ImGuiNative.ImFontConfig_destroy(m_mergeCfg);
        }
    }
}
