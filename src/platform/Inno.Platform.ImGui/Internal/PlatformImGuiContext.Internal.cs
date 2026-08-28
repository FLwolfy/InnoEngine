using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;

using Inno.Native.SDL3;
using Inno.Native.ImGui;
using ImGuiNative = Inno.Native.ImGui.ImGui;

namespace Inno.Platform.ImGui;

public sealed partial class PlatformImGuiContext
{
    private const float DEFAULT_KEY_REPEAT_DELAY_SECONDS = 0.275f;
    private const float DEFAULT_KEY_REPEAT_RATE_SECONDS = 0.050f;
    private const float DEFAULT_FONT_SIZE_PIXELS = 16.0f;
    private const float DEFAULT_ICON_SIZE_PIXELS = 14.0f;
    private const float DEFAULT_MOUSE_SCROLLING_UNITS = 0.3f;
    private const string DEFAULT_FONTS_DIRECTORY_RELATIVE_PATH = "BuiltIn/Fonts";
    private const string DEFAULT_ICONS_DIRECTORY_RELATIVE_PATH = "BuiltIn/Icons";
    private const string DEFAULT_FONT_FILE_NAME = "JetBrainsMono-Regular.ttf";
    private const double LIVE_RESIZE_HOVER_LOCK_TIMEOUT_SECONDS = 0.25;

    private readonly PlatformWindow m_window;
    private readonly ImGuiContextPtr m_context;
    private readonly PlatformImGuiViewportBackend? m_viewports;
    private readonly IPlatformImGuiRenderer m_renderer;
    private readonly Dictionary<ImGuiMouseCursor, SDLCursorPtr> m_cursors = [];
    private readonly HashSet<uint> m_pendingLiveResizeWindowIds = [];
    private readonly Stopwatch m_frameTimer = Stopwatch.StartNew();

    private ImGuiMouseCursor m_currentCursor = ImGuiMouseCursor.None;
    private SDLWindowPtr m_textInputWindow = SDLWindowPtr.Null;
    private TimeSpan m_lastFrameTime;
    private TimeSpan m_lastLiveResizeLockTime;
    private Action? m_lastDrawFrame;
    private IntPtr m_iniFilename;
    private Vector2 m_leftMousePressPosition;
    private uint m_leftMousePressWindowId;
    private uint m_liveResizeLockedWindowId;
    private uint m_mousePendingLeaveWindowId;
    private uint m_mouseWindowId;
    private int m_mouseButtonsDown;
    private int m_mousePendingLeaveFrame;
    private readonly bool m_enableSmoothResize;
    private bool m_hasStartedFrame;
    private bool m_isFrameActive;
    private bool m_leftMouseWasDragged;
    private bool m_textInputActive;
    private bool m_disposed;

    internal PlatformImGuiContext(
        PlatformWindow window,
        ImGuiContextFlags contextFlags,
        IPlatformImGuiRenderer? renderer)
    {
        var enableViewports = (contextFlags & ImGuiContextFlags.EnableViewports) != 0;
        var enableDocking = (contextFlags & ImGuiContextFlags.EnableDocking) != 0;
        m_enableSmoothResize = (contextFlags & ImGuiContextFlags.EnableSmoothResize) != 0;

        m_window = window;
        m_context = ImGuiNative.CreateContext();
        ImGuiNative.SetCurrentContext(m_context);

        var io = ImGuiNative.GetIO();
        // SDL cannot reliably identify the viewport beneath a transient NoInputs viewport while
        // docking, and the official SDL backend disables this capability on macOS. Let Dear ImGui
        // use its viewport heuristic instead of publishing incorrect hovered viewport identities.
        io.BackendFlags |= ImGuiBackendFlags.HasMouseCursors
            | ImGuiBackendFlags.HasSetMousePos
            | ImGuiBackendFlags.RendererHasTextures;
        io.ConfigFlags |= ImGuiConfigFlags.NavEnableKeyboard;

        if (enableDocking)
        {
            io.ConfigFlags |= ImGuiConfigFlags.DockingEnable;
        }

        if (enableViewports)
        {
            io.ConfigFlags |= ImGuiConfigFlags.ViewportsEnable;
            io.ConfigDpiScaleViewports = true;
            io.BackendFlags |= ImGuiBackendFlags.PlatformHasViewports | ImGuiBackendFlags.RendererHasViewports;
        }

        io.ConfigDpiScaleFonts = true;
        io.ConfigMacOSXBehaviors = OperatingSystem.IsMacOS();
        io.MouseDrawCursor = false;
        io.Fonts.RendererHasTextures = true;
        ConfigureKeyRepeat(io);
        ConfigureFonts(m_context, io);
        ImGuiPackedColor.EnsureInitialized();

        UpdateDisplayMetrics(io);
        m_renderer = renderer ?? new PlatformImGuiSdlRenderer(window);
        m_viewports = enableViewports ? new PlatformImGuiViewportBackend(window, m_renderer) : null;
    }

    public partial void SetIniFile(string? filePath)
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
        if (m_hasStartedFrame)
            throw new InvalidOperationException("The ImGui layout file must be configured before the first frame.");

        ImGuiNative.SetCurrentContext(m_context);
        var io = ImGuiNative.GetIO();
        if (m_iniFilename != IntPtr.Zero)
        {
            Marshal.FreeCoTaskMem(m_iniFilename);
            m_iniFilename = IntPtr.Zero;
        }
        if (string.IsNullOrWhiteSpace(filePath))
        {
            SetIniFilename(io, IntPtr.Zero);
            return;
        }

        string fullPath = Path.GetFullPath(filePath);
        string? directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);
        m_iniFilename = Marshal.StringToCoTaskMemUTF8(fullPath);
        SetIniFilename(io, m_iniFilename);
    }

    public partial void LoadIniSettings(string? settings)
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
        if (m_hasStartedFrame)
            throw new InvalidOperationException("ImGui layout settings must be loaded before the first frame.");
        if (string.IsNullOrEmpty(settings))
            return;

        ImGuiNative.SetCurrentContext(m_context);
        ImGuiNative.LoadIniSettingsFromMemory(settings);
    }

    public unsafe partial void DrawImage(
        ImGuiTextureHandle texture,
        Vector2 size,
        Vector2 uv0,
        Vector2 uv1)
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
        if (!texture.isValid)
        {
            throw new ArgumentException("ImGui texture token is invalid.", nameof(texture));
        }

        if (uv1 == default)
        {
            uv1 = Vector2.One;
        }

        ImGuiNative.Image(
            new ImTextureRef(texId: new ImTextureID(texture.value)),
            size,
            uv0,
            uv1);
    }

    public partial bool TryCaptureIniSettings(out string settings, bool force)
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
        ImGuiNative.SetCurrentContext(m_context);
        ImGuiIOPtr io = ImGuiNative.GetIO();
        if (!force && !io.WantSaveIniSettings)
        {
            settings = string.Empty;
            return false;
        }

        settings = ImGuiNative.SaveIniSettingsToMemoryS();
        io.WantSaveIniSettings = false;
        return true;
    }

    private static void ConfigureKeyRepeat(ImGuiIOPtr io)
    {
        io.KeyRepeatDelay = DEFAULT_KEY_REPEAT_DELAY_SECONDS;
        io.KeyRepeatRate = DEFAULT_KEY_REPEAT_RATE_SECONDS;
    }

    public partial void RegisterFontStyle(
        ImGuiFontStyle style,
        string filePath,
        float fontSizePixels)
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
        if (m_hasStartedFrame)
            throw new InvalidOperationException("Font styles must be registered before the first ImGui frame.");
        ImGuiFont.ValidateStyle(style);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        if (!float.IsFinite(fontSizePixels) || fontSizePixels <= 0f)
            throw new ArgumentOutOfRangeException(nameof(fontSizePixels));

        string fullPath = Path.GetFullPath(filePath);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException("The font file does not exist.", fullPath);

        ImGuiNative.SetCurrentContext(m_context);
        ImGuiIOPtr io = ImGuiNative.GetIO();
        ImFontPtr loadedFont = LoadFont(io.Fonts, fullPath, fontSizePixels);
        if (loadedFont.IsNull)
            throw new InvalidOperationException($"Could not load the font '{fullPath}'.");

        ResolveFontPaths(out _, out List<string> iconFontPaths);
        MergeIconFontsIntoCurrentBaseFont(io.Fonts, iconFontPaths);
        ImGuiFont.RegisterStyle(m_context, style, loadedFont);
        if (style == ImGuiFontStyle.Regular)
            io.FontDefault = loadedFont;
    }

    private static void ConfigureFonts(ImGuiContextPtr context, ImGuiIOPtr io)
    {
        var fonts = io.Fonts;
        fonts.Clear();

        ResolveFontPaths(out var fontPaths, out var iconFontPaths);
        var styleFonts = new Dictionary<ImGuiFontStyle, ImFontPtr>();
        ImFontPtr defaultFont = ImFontPtr.Null;
        var preferredIndex = FindPreferredFontIndex(fontPaths);
        for (var i = 0; i < fontPaths.Count; i++)
        {
            ImFontPtr loadedFont = LoadFont(fonts, fontPaths[i], DEFAULT_FONT_SIZE_PIXELS);
            if (loadedFont.IsNull)
            {
                continue;
            }

            MergeIconFontsIntoCurrentBaseFont(fonts, iconFontPaths);
            styleFonts[GetFontStyle(fontPaths[i])] = loadedFont;
            if (i == preferredIndex)
            {
                defaultFont = loadedFont;
            }
            else if (defaultFont.IsNull)
            {
                defaultFont = loadedFont;
            }
        }

        if (defaultFont.IsNull)
        {
            defaultFont = LoadDefaultFont(fonts);
            if (!defaultFont.IsNull)
            {
                MergeIconFontsIntoCurrentBaseFont(fonts, iconFontPaths);
            }
        }

        io.FontDefault = defaultFont;
        if (!defaultFont.IsNull && !styleFonts.ContainsKey(ImGuiFontStyle.Regular))
            styleFonts[ImGuiFontStyle.Regular] = defaultFont;
        ImGuiFont.RegisterContext(context, styleFonts);
    }

    private static ImGuiFontStyle GetFontStyle(string fontPath)
    {
        string name = Path.GetFileNameWithoutExtension(fontPath);
        bool bold = name.Contains("Bold", StringComparison.OrdinalIgnoreCase);
        bool italic = name.Contains("Italic", StringComparison.OrdinalIgnoreCase);
        return (bold ? ImGuiFontStyle.Bold : ImGuiFontStyle.Regular) |
               (italic ? ImGuiFontStyle.Italic : ImGuiFontStyle.Regular);
    }

    private static int FindPreferredFontIndex(List<string> fontPaths)
    {
        if (fontPaths.Count == 0 || string.IsNullOrWhiteSpace(DEFAULT_FONT_FILE_NAME))
        {
            return -1;
        }

        for (var i = 0; i < fontPaths.Count; i++)
        {
            var fileName = Path.GetFileName(fontPaths[i]);
            if (fileName.Equals(DEFAULT_FONT_FILE_NAME, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    private static void ResolveFontPaths(out List<string> baseFonts, out List<string> iconFonts)
    {
        baseFonts = new List<string>();
        iconFonts = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var fontDirectory in ResolveAssetDirectories(DEFAULT_FONTS_DIRECTORY_RELATIVE_PATH))
        {
            CollectFontFiles(fontDirectory, baseFonts, seen, includeSubdirectories: false);
        }

        foreach (var iconDirectory in ResolveAssetDirectories(DEFAULT_ICONS_DIRECTORY_RELATIVE_PATH))
        {
            CollectFontFiles(iconDirectory, iconFonts, seen, includeSubdirectories: false);
        }

        baseFonts.Sort(StringComparer.OrdinalIgnoreCase);
        iconFonts.Sort(StringComparer.OrdinalIgnoreCase);
    }

    private static List<string> ResolveAssetDirectories(string relativePath)
    {
        var directories = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, relativePath),
            Path.Combine(Path.GetDirectoryName(typeof(PlatformImGuiContext).Assembly.Location) ?? string.Empty, relativePath)
        };

        foreach (var candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                continue;
            }

            var fullPath = Path.GetFullPath(candidate);
            if (Directory.Exists(fullPath) && seen.Add(fullPath))
            {
                directories.Add(fullPath);
            }
        }

        return directories;
    }

    private static void CollectFontFiles(string directory, List<string> output, HashSet<string> seen, bool includeSubdirectories)
    {
        var searchOption = includeSubdirectories ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var fontFiles = Directory.GetFiles(directory, "*.*", searchOption);
        foreach (var fontFile in fontFiles)
        {
            var extension = Path.GetExtension(fontFile);
            if (!extension.Equals(".ttf", StringComparison.OrdinalIgnoreCase)
                && !extension.Equals(".otf", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var fullPath = Path.GetFullPath(fontFile);
            if (seen.Add(fullPath))
            {
                output.Add(fullPath);
            }
        }
    }

    private static unsafe void MergeIconFontsIntoCurrentBaseFont(ImFontAtlasPtr fonts, List<string> iconFontPaths)
    {
        if (iconFontPaths.Count == 0)
        {
            return;
        }

        Span<uint> iconGlyphRanges = stackalloc uint[] { 0xE000, 0xF8FF, 0 };
        fixed (uint* pGlyphRanges = iconGlyphRanges)
        {
            for (var i = 0; i < iconFontPaths.Count; i++)
            {
                var mergeConfig = ImGuiNative.ImFontConfig();
                mergeConfig.MergeMode = true;
                mergeConfig.PixelSnapH = true;
                try
                {
                    _ = fonts.AddFontFromFileTTF(iconFontPaths[i], DEFAULT_ICON_SIZE_PIXELS, mergeConfig, pGlyphRanges);
                }
                finally
                {
                    ImGuiNative.Destroy(mergeConfig);
                }
            }
        }
    }

    internal void ProcessEvent(ref SDLEvent sdlEvent)
    {
        if (m_disposed)
        {
            return;
        }

        ImGuiNative.SetCurrentContext(m_context);
        var io = ImGuiNative.GetIO();
        var eventType = (SDLEventType)sdlEvent.Type;

        if (!TryGetWindowId(ref sdlEvent, out var eventWindowId))
        {
            return;
        }

        if (eventWindowId != m_window.windowId && (m_viewports == null || !m_viewports.OwnsWindow(eventWindowId)))
        {
            return;
        }

        if (m_enableSmoothResize)
            RefreshLiveResizeHoverLock(eventType, eventWindowId, ref sdlEvent);

        m_viewports?.ProcessEvent(ref sdlEvent, eventWindowId);

        switch (eventType)
        {
            case SDLEventType.KeyDown:
            case SDLEventType.KeyUp:
            {
                var down = eventType == SDLEventType.KeyDown;
                if (down && sdlEvent.Key.Repeat != 0)
                {
                    // Keep key-repeat timing driven by ImGui (io.KeyRepeatDelay/KeyRepeatRate).
                    break;
                }

                UpdateKeyModifiers(io, sdlEvent.Key.Mod);

                var key = TranslateKey(sdlEvent.Key.Scancode);
                if (key != ImGuiKey.None)
                {
                    io.AddKeyEvent(key, down);
                    io.SetKeyEventNativeData(key, sdlEvent.Key.Key, (int)sdlEvent.Key.Scancode, (int)sdlEvent.Key.Scancode);
                }
                break;
            }

            case SDLEventType.TextInput:
            {
                string? text = GetTextInputText(sdlEvent);
                if (!string.IsNullOrEmpty(text))
                {
                    io.AddInputCharactersUTF8(text);
                }
                break;
            }

            case SDLEventType.MouseMotion:
            {
                m_mouseWindowId = eventWindowId;
                m_mousePendingLeaveFrame = 0;
                m_mousePendingLeaveWindowId = 0;
                Vector2 mousePosition = GetEventMousePosition(
                    io,
                    eventWindowId,
                    sdlEvent.Motion.X,
                    sdlEvent.Motion.Y);
                UpdateLeftMouseDragState(io, mousePosition);
                io.AddMousePosEvent(mousePosition.X, mousePosition.Y);
                break;
            }

            case SDLEventType.MouseButtonDown:
            case SDLEventType.MouseButtonUp:
            {
                var down = eventType == SDLEventType.MouseButtonDown;
                uint pointerFocusSourceWindowId = 0;
                if (TryTranslateMouseButton(sdlEvent.Button.Button, out var button))
                {
                    int mask = 1 << button;
                    if (down)
                    {
                        m_mouseButtonsDown |= mask;
                        if (button == 0)
                        {
                            m_leftMousePressWindowId = eventWindowId;
                            m_leftMousePressPosition = GetEventMousePosition(
                                io,
                                eventWindowId,
                                sdlEvent.Button.X,
                                sdlEvent.Button.Y);
                            m_leftMouseWasDragged = false;
                        }
                    }
                    else
                    {
                        m_mouseButtonsDown &= ~mask;
                        if (button == 0)
                        {
                            Vector2 mousePosition = GetEventMousePosition(
                                io,
                                eventWindowId,
                                sdlEvent.Button.X,
                                sdlEvent.Button.Y);
                            UpdateLeftMouseDragState(io, mousePosition);
                            if (m_leftMouseWasDragged)
                            {
                                pointerFocusSourceWindowId = m_leftMousePressWindowId;
                            }

                            m_leftMousePressPosition = default;
                            m_leftMousePressWindowId = 0;
                            m_leftMouseWasDragged = false;
                        }
                    }
                    io.AddMouseButtonEvent(button, down);
                    // Docking can destroy the source viewport before SDL emits MouseUp. Capturing
                    // the mouse keeps the complete press/release sequence inside this application.
                    _ = SDL.CaptureMouse(m_mouseButtonsDown != 0);
                    if (pointerFocusSourceWindowId != 0)
                    {
                        m_viewports?.FocusPointerTarget(pointerFocusSourceWindowId);
                    }
                }
                break;
            }

            case SDLEventType.MouseWheel:
            {
                var wheelX = sdlEvent.Wheel.X;
                var wheelY = sdlEvent.Wheel.Y;

                // Flip the horizontal wheel for macOS due to natural scrolling
                if (OperatingSystem.IsMacOS())
                {
                    wheelX = -wheelX;
                }

                io.AddMouseWheelEvent(
                    wheelX * DEFAULT_MOUSE_SCROLLING_UNITS,
                    wheelY * DEFAULT_MOUSE_SCROLLING_UNITS);
                break;
            }

            case SDLEventType.WindowFocusGained:
                io.AddFocusEvent(true);
                break;

            case SDLEventType.WindowFocusLost:
                io.AddFocusEvent(false);
                break;

            case SDLEventType.WindowMouseEnter:
                m_mouseWindowId = eventWindowId;
                m_mousePendingLeaveFrame = 0;
                m_mousePendingLeaveWindowId = 0;
                break;

            case SDLEventType.WindowMouseLeave:
                // SDL may deliver Leave before Enter while crossing viewport windows. Defer the
                // reset so a matching Enter or Motion event can cancel it without breaking drag.
                if (m_mouseWindowId == eventWindowId)
                {
                    m_mousePendingLeaveFrame = ImGuiNative.GetFrameCount() + 1;
                    m_mousePendingLeaveWindowId = eventWindowId;
                }
                break;

            case SDLEventType.WindowDisplayScaleChanged:
            case SDLEventType.WindowResized:
            case SDLEventType.WindowPixelSizeChanged:
                m_viewports?.SynchronizeWindow(eventWindowId);
                UpdateDisplayMetrics(io);
                break;

            case SDLEventType.WindowExposed:
                UpdateDisplayMetrics(io);
                break;
        }
    }

    internal void RenderLiveResizeWindow(uint windowId)
    {
        if (m_disposed || !m_enableSmoothResize)
        {
            return;
        }

        var ownsMainWindow = windowId == m_window.windowId;
        var ownsViewportWindow = m_viewports?.OwnsWindow(windowId) == true;
        if (!ownsMainWindow && !ownsViewportWindow)
        {
            return;
        }

        m_pendingLiveResizeWindowIds.Add(windowId);
        if (m_isFrameActive)
        {
            return;
        }

        ImGuiNative.SetCurrentContext(m_context);
        var liveResizeDraw = m_lastDrawFrame;
        if (liveResizeDraw is null)
        {
            return;
        }

        var now = m_frameTimer.Elapsed;
        m_liveResizeLockedWindowId = windowId;
        m_lastLiveResizeLockTime = now;
        var deltaSeconds = (float)(now - m_lastFrameTime).TotalSeconds;
        m_lastFrameTime = now;

        SynchronizePendingLiveResizeWindows();
        BeginFrame(deltaSeconds);
        try
        {
            liveResizeDraw();
            _ = EndFrame();
        }
        catch
        {
            m_isFrameActive = false;
            throw;
        }
    }

    public partial IntPtr RenderFrame(Action drawFrame)
    {
        ArgumentNullException.ThrowIfNull(drawFrame);
        m_lastDrawFrame = drawFrame;

        var now = m_frameTimer.Elapsed;
        var deltaSeconds = (float)(now - m_lastFrameTime).TotalSeconds;
        m_lastFrameTime = now;

        SynchronizePendingLiveResizeWindows();
        BeginFrame(deltaSeconds);
        try
        {
            drawFrame();
            return EndFrame();
        }
        catch
        {
            m_isFrameActive = false;
            throw;
        }
    }

    private void BeginFrame(float deltaTimeSeconds)
    {
        if (m_disposed)
        {
            throw new ObjectDisposedException(nameof(PlatformImGuiContext));
        }

        ImGuiNative.SetCurrentContext(m_context);
        m_hasStartedFrame = true;

        var io = ImGuiNative.GetIO();
        io.MouseDrawCursor = false;
        UpdateDisplayMetrics(io);
        io.DeltaTime = deltaTimeSeconds > 0f ? deltaTimeSeconds : (1f / 60f);
        ReconcileMouseButtons(io);
        ReleaseCompletedLiveResizeHoverLock();
        FlushPendingMouseLeave(io);
        UpdateMouseData(io, m_window.GetSdlWindow());
        UpdateTextInputState(io);
        
        m_isFrameActive = true;
        ImGuiNative.NewFrame();
        
        // During live resize, prevent the main viewport from hosting other windows.
        var mainViewport = ImGuiNative.GetMainViewport();
        if (m_liveResizeLockedWindowId != 0
            && (io.ConfigFlags & ImGuiConfigFlags.ViewportsEnable) != 0
            && !mainViewport.IsNull)
        {
            mainViewport.Flags &= ~ImGuiViewportFlags.CanHostOtherWindows;
        }
    }

    private IntPtr EndFrame()
    {
        if (m_disposed)
        {
            throw new ObjectDisposedException(nameof(PlatformImGuiContext));
        }
        
        var io = ImGuiNative.GetIO();
        
        try
        {
            ImGuiNative.SetCurrentContext(m_context);
            UpdateMouseCursor(io);
            ImGuiNative.SetMouseCursor(ImGuiMouseCursor.None);
            ImGuiNative.Render();

            io.MouseDrawCursor = false;
            var drawData = ImGuiNative.GetDrawData();
            m_renderer.RenderMain(GetDrawDataAddress(drawData));
            if ((io.ConfigFlags & ImGuiConfigFlags.ViewportsEnable) != 0)
            {
                ImGuiNative.UpdatePlatformWindows();
                ImGuiNative.RenderPlatformWindowsDefault();
                ImGuiNative.SetCurrentContext(m_context);
            }

            return GetDrawDataAddress(drawData);
        }
        finally
        {
            m_isFrameActive = false;
        }
    }

    private void RefreshLiveResizeHoverLock(SDLEventType eventType, uint eventWindowId, ref SDLEvent sdlEvent)
    {
        if (m_liveResizeLockedWindowId == 0)
        {
            return;
        }

        float mouseX = 0f;
        float mouseY = 0f;
        var mouseButtons = SDL.GetMouseState(ref mouseX, ref mouseY);
        var leftMouseMask = 1u << (SDL.SDL_BUTTON_LEFT - 1);
        if ((mouseButtons & leftMouseMask) == 0)
        {
            m_liveResizeLockedWindowId = 0;
            return;
        }

        var now = m_frameTimer.Elapsed;
        if (eventType == SDLEventType.WindowExposed && eventWindowId == m_liveResizeLockedWindowId)
        {
            m_lastLiveResizeLockTime = now;
        }

        if ((now - m_lastLiveResizeLockTime).TotalSeconds > LIVE_RESIZE_HOVER_LOCK_TIMEOUT_SECONDS)
        {
            m_liveResizeLockedWindowId = 0;
            return;
        }

        if (eventType == SDLEventType.WindowExposed
            && eventWindowId == m_liveResizeLockedWindowId
            && sdlEvent.Window.Data1 == 0)
        {
            m_liveResizeLockedWindowId = 0;
            return;
        }

        if (eventType == SDLEventType.MouseButtonUp && sdlEvent.Button.Button == SDL.SDL_BUTTON_LEFT)
        {
            m_liveResizeLockedWindowId = 0;
        }
    }

    public partial void Dispose()
    {
        if (m_disposed)
        {
            return;
        }

        ImGuiNative.SetCurrentContext(m_context);
        var io = ImGuiNative.GetIO();
        if (m_textInputActive)
        {
            var textInputWindow = m_textInputWindow.IsNull ? m_window.GetSdlWindow() : m_textInputWindow;
            _ = SDL.StopTextInput(textInputWindow);
            m_textInputWindow = SDLWindowPtr.Null;
            m_textInputActive = false;
        }

        _ = SDL.CaptureMouse(false);
        m_mouseButtonsDown = 0;
        m_leftMousePressPosition = default;
        m_leftMousePressWindowId = 0;
        m_leftMouseWasDragged = false;
        m_mouseWindowId = 0;
        m_mousePendingLeaveFrame = 0;
        m_pendingLiveResizeWindowIds.Clear();

        if ((io.ConfigFlags & ImGuiConfigFlags.ViewportsEnable) != 0)
        {
            ImGuiNative.DestroyPlatformWindows();
        }

        foreach (var cursor in m_cursors.Values)
        {
            if (!cursor.IsNull)
            {
                SDL.DestroyCursor(cursor);
            }
        }

        m_cursors.Clear();
        m_viewports?.Dispose();
        m_renderer.Dispose();
        ImGuiFont.UnregisterContext(m_context);
        ImGuiNative.DestroyContext(m_context);
        if (m_iniFilename != IntPtr.Zero)
        {
            Marshal.FreeCoTaskMem(m_iniFilename);
            m_iniFilename = IntPtr.Zero;
        }
        m_disposed = true;
    }

    private void UpdateDisplayMetrics(ImGuiIOPtr io)
    {
        var windowWidth = 0;
        var windowHeight = 0;
        SDLWindowPtr sdlWindow = m_window.GetSdlWindow();
        SDL.GetWindowSize(sdlWindow, ref windowWidth, ref windowHeight);

        var pixelWidth = 0;
        var pixelHeight = 0;
        SDL.GetWindowSizeInPixels(sdlWindow, ref pixelWidth, ref pixelHeight);

        io.DisplaySize = new Vector2(windowWidth, windowHeight);

        if (windowWidth > 0 && windowHeight > 0)
        {
            io.DisplayFramebufferScale = new Vector2(
                pixelWidth / (float)windowWidth,
                pixelHeight / (float)windowHeight);
        }
        else
        {
            io.DisplayFramebufferScale = Vector2.One;
        }
    }

    private void UpdateTextInputState(ImGuiIOPtr io)
    {
        var targetWindow = ResolveTextInputWindow();
        if (io.WantTextInput)
        {
            if (!m_textInputActive || m_textInputWindow != targetWindow)
            {
                if (m_textInputActive && !m_textInputWindow.IsNull)
                {
                    _ = SDL.StopTextInput(m_textInputWindow);
                }

                _ = SDL.StartTextInputWithProperties(targetWindow, 0);
                m_textInputWindow = targetWindow;
                m_textInputActive = true;
            }
        }
        else if (m_textInputActive)
        {
            var textInputWindow = m_textInputWindow.IsNull ? targetWindow : m_textInputWindow;
            _ = SDL.StopTextInput(textInputWindow);
            m_textInputWindow = SDLWindowPtr.Null;
            m_textInputActive = false;
        }
    }

    private static unsafe void SetIniFilename(ImGuiIOPtr io, IntPtr fileName)
    {
        io.IniFilename = (byte*)fileName;
    }

    private static unsafe ImFontPtr LoadFont(ImFontAtlasPtr fonts, string filePath, float fontSizePixels) =>
        fonts.AddFontFromFileTTF(filePath, fontSizePixels);

    private static unsafe ImFontPtr LoadDefaultFont(ImFontAtlasPtr fonts) => fonts.AddFontDefault();

    private static unsafe string? GetTextInputText(SDLEvent sdlEvent) =>
        sdlEvent.Text.Text == null ? null : Marshal.PtrToStringUTF8((IntPtr)sdlEvent.Text.Text);

    private static unsafe IntPtr GetDrawDataAddress(ImDrawDataPtr drawData) => new(drawData);

    private SDLWindowPtr ResolveTextInputWindow()
    {
        var keyboardFocus = SDL.GetKeyboardFocus();
        if (keyboardFocus.IsNull)
        {
            return m_window.GetSdlWindow();
        }

        var focusedWindowId = SDL.GetWindowID(keyboardFocus);
        if (focusedWindowId == m_window.windowId || (m_viewports != null && m_viewports.OwnsWindow(focusedWindowId)))
        {
            return keyboardFocus;
        }

        return m_window.GetSdlWindow();
    }

    private static void UpdateMouseData(ImGuiIOPtr io, SDLWindowPtr window)
    {
        // Mouse position is fed from SDL mouse events (per-window coordinates).
        // Polling here would overwrite secondary viewport coordinates with the wrong window space.
        if (io.WantSetMousePos)
        {
            SDL.WarpMouseInWindow(window, io.MousePos.X, io.MousePos.Y);
        }
    }

    private void SynchronizePendingLiveResizeWindows()
    {
        if (m_pendingLiveResizeWindowIds.Count == 0)
        {
            return;
        }

        uint[] windowIds = [.. m_pendingLiveResizeWindowIds];
        m_pendingLiveResizeWindowIds.Clear();
        foreach (uint windowId in windowIds)
        {
            if (windowId == m_window.windowId)
            {
                int pixelWidth = 0;
                int pixelHeight = 0;
                SDL.GetWindowSizeInPixels(m_window.GetSdlWindow(), ref pixelWidth, ref pixelHeight);
                m_renderer.SynchronizeMainOutput(pixelWidth, pixelHeight);
            }
            else
                m_viewports?.SynchronizeWindow(windowId);
        }
    }

    private void ReconcileMouseButtons(ImGuiIOPtr io)
    {
        if (m_mouseButtonsDown == 0)
            return;

        uint pointerFocusSourceWindowId = 0;
        float mouseX = 0f;
        float mouseY = 0f;
        uint currentButtons = SDL.GetMouseState(ref mouseX, ref mouseY);
        ReadOnlySpan<byte> sdlButtons =
        [
            SDL.SDL_BUTTON_LEFT,
            SDL.SDL_BUTTON_RIGHT,
            SDL.SDL_BUTTON_MIDDLE,
            SDL.SDL_BUTTON_X1,
            SDL.SDL_BUTTON_X2
        ];
        for (int button = 0; button < sdlButtons.Length; button++)
        {
            int trackedMask = 1 << button;
            if ((m_mouseButtonsDown & trackedMask) == 0)
                continue;

            uint sdlMask = 1u << (sdlButtons[button] - 1);
            if ((currentButtons & sdlMask) != 0)
                continue;

            // Recover when a destroyed viewport prevented the corresponding MouseUp event from
            // reaching the event queue. Without this release ImGui keeps its active drag forever.
            m_mouseButtonsDown &= ~trackedMask;
            io.AddMouseButtonEvent(button, false);
            if (button == 0)
            {
                float globalMouseX = 0f;
                float globalMouseY = 0f;
                _ = SDL.GetGlobalMouseState(ref globalMouseX, ref globalMouseY);
                UpdateLeftMouseDragState(io, new Vector2(globalMouseX, globalMouseY));
                if (m_leftMouseWasDragged)
                {
                    pointerFocusSourceWindowId = m_leftMousePressWindowId;
                }

                m_leftMousePressPosition = default;
                m_leftMousePressWindowId = 0;
                m_leftMouseWasDragged = false;
            }
        }

        if (m_mouseButtonsDown == 0)
            _ = SDL.CaptureMouse(false);
        if (pointerFocusSourceWindowId != 0)
            m_viewports?.FocusPointerTarget(pointerFocusSourceWindowId);
    }

    private static Vector2 GetEventMousePosition(
        ImGuiIOPtr io,
        uint windowId,
        float localX,
        float localY)
    {
        Vector2 position = new(localX, localY);
        if ((io.ConfigFlags & ImGuiConfigFlags.ViewportsEnable) == 0)
        {
            return position;
        }

        SDLWindowPtr window = SDL.GetWindowFromID(windowId);
        if (window.IsNull)
        {
            return position;
        }

        var windowX = 0;
        var windowY = 0;
        _ = SDL.GetWindowPosition(window, ref windowX, ref windowY);
        return position + new Vector2(windowX, windowY);
    }

    private void UpdateLeftMouseDragState(ImGuiIOPtr io, Vector2 mousePosition)
    {
        if (m_leftMouseWasDragged || m_leftMousePressWindowId == 0)
        {
            return;
        }

        float dragThreshold = io.MouseDragThreshold;
        if (Vector2.DistanceSquared(m_leftMousePressPosition, mousePosition) >= dragThreshold * dragThreshold)
        {
            m_leftMouseWasDragged = true;
        }
    }

    private void FlushPendingMouseLeave(ImGuiIOPtr io)
    {
        if (!ShouldFlushPendingMouseLeave(
                m_mousePendingLeaveFrame,
                ImGuiNative.GetFrameCount(),
                m_mouseButtonsDown,
                m_mousePendingLeaveWindowId,
                m_liveResizeLockedWindowId))
        {
            return;
        }

        m_mouseWindowId = 0;
        m_mousePendingLeaveFrame = 0;
        m_mousePendingLeaveWindowId = 0;
        io.AddMousePosEvent(-float.MaxValue, -float.MaxValue);
    }

    private void ReleaseCompletedLiveResizeHoverLock()
    {
        if (m_liveResizeLockedWindowId == 0 ||
            (m_frameTimer.Elapsed - m_lastLiveResizeLockTime).TotalSeconds <=
            LIVE_RESIZE_HOVER_LOCK_TIMEOUT_SECONDS)
        {
            return;
        }

        float mouseX = 0f;
        float mouseY = 0f;
        uint mouseButtons = SDL.GetMouseState(ref mouseX, ref mouseY);
        uint leftMouseMask = 1u << (SDL.SDL_BUTTON_LEFT - 1);
        if ((mouseButtons & leftMouseMask) == 0)
            m_liveResizeLockedWindowId = 0;
    }

    private static bool ShouldFlushPendingMouseLeave(
        int pendingFrame,
        int currentFrame,
        int mouseButtonsDown,
        uint pendingWindowId,
        uint liveResizeLockedWindowId)
    {
        if (pendingFrame == 0 || mouseButtonsDown != 0 || currentFrame < pendingFrame)
            return false;

        return liveResizeLockedWindowId == 0 || pendingWindowId != liveResizeLockedWindowId;
    }

    private void UpdateMouseCursor(ImGuiIOPtr io)
    {
        if ((io.ConfigFlags & ImGuiConfigFlags.NoMouseCursorChange) != 0)
        {
            return;
        }

        var imguiCursor = ImGuiNative.GetMouseCursor();
        if (imguiCursor == ImGuiMouseCursor.None || io.MouseDrawCursor)
        {
            _ = SDL.HideCursor();
            m_currentCursor = ImGuiMouseCursor.None;
            return;
        }

        var sdlCursor = GetOrCreateCursor(imguiCursor);
        if (!sdlCursor.IsNull && m_currentCursor != imguiCursor)
        {
            _ = SDL.SetCursor(sdlCursor);
            m_currentCursor = imguiCursor;
        }

        _ = SDL.ShowCursor();
    }

    private SDLCursorPtr GetOrCreateCursor(ImGuiMouseCursor cursor)
    {
        if (m_cursors.TryGetValue(cursor, out var cachedCursor))
        {
            return cachedCursor;
        }

        var sdlCursorType = cursor switch
        {
            ImGuiMouseCursor.Arrow => SDLSystemCursor.Default,
            ImGuiMouseCursor.TextInput => SDLSystemCursor.Text,
            ImGuiMouseCursor.ResizeAll => SDLSystemCursor.Move,
            ImGuiMouseCursor.ResizeNs => SDLSystemCursor.NsResize,
            ImGuiMouseCursor.ResizeEw => SDLSystemCursor.EwResize,
            ImGuiMouseCursor.ResizeNesw => SDLSystemCursor.NeswResize,
            ImGuiMouseCursor.ResizeNwse => SDLSystemCursor.NwseResize,
            ImGuiMouseCursor.Hand => SDLSystemCursor.Pointer,
            ImGuiMouseCursor.Wait => SDLSystemCursor.Wait,
            ImGuiMouseCursor.Progress => SDLSystemCursor.Progress,
            ImGuiMouseCursor.NotAllowed => SDLSystemCursor.NotAllowed,
            _ => SDLSystemCursor.Default
        };

        var createdCursor = SDL.CreateSystemCursor(sdlCursorType);
        m_cursors[cursor] = createdCursor;
        return createdCursor;
    }

    private static bool TryTranslateMouseButton(byte sdlButton, out int imguiButton)
    {
        switch (sdlButton)
        {
            case SDL.SDL_BUTTON_LEFT:
                imguiButton = 0;
                return true;
            case SDL.SDL_BUTTON_RIGHT:
                imguiButton = 1;
                return true;
            case SDL.SDL_BUTTON_MIDDLE:
                imguiButton = 2;
                return true;
            case SDL.SDL_BUTTON_X1:
                imguiButton = 3;
                return true;
            case SDL.SDL_BUTTON_X2:
                imguiButton = 4;
                return true;
            default:
                imguiButton = 0;
                return false;
        }
    }

    private static void UpdateKeyModifiers(ImGuiIOPtr io, ushort modifiers)
    {
        var sdlModifiers = (uint)modifiers;
        io.AddKeyEvent(ImGuiKey.ModCtrl, (sdlModifiers & (SDL.SDL_KMOD_LCTRL | SDL.SDL_KMOD_RCTRL)) != 0);
        io.AddKeyEvent(ImGuiKey.ModShift, (sdlModifiers & (SDL.SDL_KMOD_LSHIFT | SDL.SDL_KMOD_RSHIFT)) != 0);
        io.AddKeyEvent(ImGuiKey.ModAlt, (sdlModifiers & (SDL.SDL_KMOD_LALT | SDL.SDL_KMOD_RALT)) != 0);
        io.AddKeyEvent(ImGuiKey.ModSuper, (sdlModifiers & (SDL.SDL_KMOD_LGUI | SDL.SDL_KMOD_RGUI)) != 0);
    }

    private static ImGuiKey TranslateKey(SDLScancode scancode)
    {
        return scancode switch
        {
            SDLScancode.Tab => ImGuiKey.Tab,
            SDLScancode.Left => ImGuiKey.LeftArrow,
            SDLScancode.Right => ImGuiKey.RightArrow,
            SDLScancode.Up => ImGuiKey.UpArrow,
            SDLScancode.Down => ImGuiKey.DownArrow,
            SDLScancode.Pageup => ImGuiKey.PageUp,
            SDLScancode.Pagedown => ImGuiKey.PageDown,
            SDLScancode.Home => ImGuiKey.Home,
            SDLScancode.End => ImGuiKey.End,
            SDLScancode.Insert => ImGuiKey.Insert,
            SDLScancode.Delete => ImGuiKey.Delete,
            SDLScancode.Backspace => ImGuiKey.Backspace,
            SDLScancode.Space => ImGuiKey.Space,
            SDLScancode.Return => ImGuiKey.Enter,
            SDLScancode.Escape => ImGuiKey.Escape,
            SDLScancode.Apostrophe => ImGuiKey.Apostrophe,
            SDLScancode.Comma => ImGuiKey.Comma,
            SDLScancode.Minus => ImGuiKey.Minus,
            SDLScancode.Period => ImGuiKey.Period,
            SDLScancode.Slash => ImGuiKey.Slash,
            SDLScancode.Semicolon => ImGuiKey.Semicolon,
            SDLScancode.Equals => ImGuiKey.Equal,
            SDLScancode.Leftbracket => ImGuiKey.LeftBracket,
            SDLScancode.Backslash => ImGuiKey.Backslash,
            SDLScancode.Rightbracket => ImGuiKey.RightBracket,
            SDLScancode.Grave => ImGuiKey.GraveAccent,
            SDLScancode.Capslock => ImGuiKey.CapsLock,
            SDLScancode.Scrolllock => ImGuiKey.ScrollLock,
            SDLScancode.Numlockclear => ImGuiKey.NumLock,
            SDLScancode.Printscreen => ImGuiKey.PrintScreen,
            SDLScancode.Pause => ImGuiKey.Pause,
            SDLScancode.Kp0 => ImGuiKey.Keypad0,
            SDLScancode.Kp1 => ImGuiKey.Keypad1,
            SDLScancode.Kp2 => ImGuiKey.Keypad2,
            SDLScancode.Kp3 => ImGuiKey.Keypad3,
            SDLScancode.Kp4 => ImGuiKey.Keypad4,
            SDLScancode.Kp5 => ImGuiKey.Keypad5,
            SDLScancode.Kp6 => ImGuiKey.Keypad6,
            SDLScancode.Kp7 => ImGuiKey.Keypad7,
            SDLScancode.Kp8 => ImGuiKey.Keypad8,
            SDLScancode.Kp9 => ImGuiKey.Keypad9,
            SDLScancode.KpPeriod => ImGuiKey.KeypadDecimal,
            SDLScancode.KpDivide => ImGuiKey.KeypadDivide,
            SDLScancode.KpMultiply => ImGuiKey.KeypadMultiply,
            SDLScancode.KpMinus => ImGuiKey.KeypadSubtract,
            SDLScancode.KpPlus => ImGuiKey.KeypadAdd,
            SDLScancode.KpEnter => ImGuiKey.KeypadEnter,
            SDLScancode.KpEquals => ImGuiKey.KeypadEqual,
            SDLScancode.Lctrl => ImGuiKey.LeftCtrl,
            SDLScancode.Lshift => ImGuiKey.LeftShift,
            SDLScancode.Lalt => ImGuiKey.LeftAlt,
            SDLScancode.Lgui => ImGuiKey.LeftSuper,
            SDLScancode.Rctrl => ImGuiKey.RightCtrl,
            SDLScancode.Rshift => ImGuiKey.RightShift,
            SDLScancode.Ralt => ImGuiKey.RightAlt,
            SDLScancode.Rgui => ImGuiKey.RightSuper,
            SDLScancode.Menu => ImGuiKey.Menu,
            SDLScancode.Scancode0 => ImGuiKey.Key0,
            SDLScancode.Scancode1 => ImGuiKey.Key1,
            SDLScancode.Scancode2 => ImGuiKey.Key2,
            SDLScancode.Scancode3 => ImGuiKey.Key3,
            SDLScancode.Scancode4 => ImGuiKey.Key4,
            SDLScancode.Scancode5 => ImGuiKey.Key5,
            SDLScancode.Scancode6 => ImGuiKey.Key6,
            SDLScancode.Scancode7 => ImGuiKey.Key7,
            SDLScancode.Scancode8 => ImGuiKey.Key8,
            SDLScancode.Scancode9 => ImGuiKey.Key9,
            SDLScancode.A => ImGuiKey.A,
            SDLScancode.B => ImGuiKey.B,
            SDLScancode.C => ImGuiKey.C,
            SDLScancode.D => ImGuiKey.D,
            SDLScancode.E => ImGuiKey.E,
            SDLScancode.F => ImGuiKey.F,
            SDLScancode.G => ImGuiKey.G,
            SDLScancode.H => ImGuiKey.H,
            SDLScancode.I => ImGuiKey.I,
            SDLScancode.J => ImGuiKey.J,
            SDLScancode.K => ImGuiKey.K,
            SDLScancode.L => ImGuiKey.L,
            SDLScancode.M => ImGuiKey.M,
            SDLScancode.N => ImGuiKey.N,
            SDLScancode.O => ImGuiKey.O,
            SDLScancode.P => ImGuiKey.P,
            SDLScancode.Q => ImGuiKey.Q,
            SDLScancode.R => ImGuiKey.R,
            SDLScancode.S => ImGuiKey.S,
            SDLScancode.T => ImGuiKey.T,
            SDLScancode.U => ImGuiKey.U,
            SDLScancode.V => ImGuiKey.V,
            SDLScancode.W => ImGuiKey.W,
            SDLScancode.X => ImGuiKey.X,
            SDLScancode.Y => ImGuiKey.Y,
            SDLScancode.Z => ImGuiKey.Z,
            SDLScancode.F1 => ImGuiKey.F1,
            SDLScancode.F2 => ImGuiKey.F2,
            SDLScancode.F3 => ImGuiKey.F3,
            SDLScancode.F4 => ImGuiKey.F4,
            SDLScancode.F5 => ImGuiKey.F5,
            SDLScancode.F6 => ImGuiKey.F6,
            SDLScancode.F7 => ImGuiKey.F7,
            SDLScancode.F8 => ImGuiKey.F8,
            SDLScancode.F9 => ImGuiKey.F9,
            SDLScancode.F10 => ImGuiKey.F10,
            SDLScancode.F11 => ImGuiKey.F11,
            SDLScancode.F12 => ImGuiKey.F12,
            _ => ImGuiKey.None
        };
    }

    private static bool TryGetWindowId(ref SDLEvent sdlEvent, out uint windowId)
    {
        var eventType = (SDLEventType)sdlEvent.Type;
        switch (eventType)
        {
            case SDLEventType.KeyDown:
            case SDLEventType.KeyUp:
                windowId = sdlEvent.Key.WindowID;
                return true;

            case SDLEventType.TextInput:
                windowId = sdlEvent.Text.WindowID;
                return true;

            case SDLEventType.MouseMotion:
                windowId = sdlEvent.Motion.WindowID;
                return true;

            case SDLEventType.MouseButtonDown:
            case SDLEventType.MouseButtonUp:
                windowId = sdlEvent.Button.WindowID;
                return true;

            case SDLEventType.MouseWheel:
                windowId = sdlEvent.Wheel.WindowID;
                return true;

            case SDLEventType.WindowFocusGained:
            case SDLEventType.WindowFocusLost:
            case SDLEventType.WindowMouseEnter:
            case SDLEventType.WindowMouseLeave:
            case SDLEventType.WindowDisplayScaleChanged:
            case SDLEventType.WindowExposed:
            case SDLEventType.WindowMoved:
            case SDLEventType.WindowResized:
            case SDLEventType.WindowPixelSizeChanged:
            case SDLEventType.WindowCloseRequested:
                windowId = sdlEvent.Window.WindowID;
                return true;

            default:
                windowId = 0;
                return false;
        }
    }

}
