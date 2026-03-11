using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;

using Inno.Core.Math;
using Inno.Platform.Graphics;
using Inno.Platform.Window;
using Inno.Platform.Display;

using ImGuiNET;

namespace Inno.ImGui.Backend;

/// <summary>
/// Platform-level ImGui implementation which relies only on Inno.Platform interfaces.
/// </summary>
internal sealed class ImGuiNETBackend : IImGuiBackend
{
	// Platform Systems
    private readonly IWindowSystem m_windowSystem;
    private readonly IGraphicsDevice m_graphicsDevice;
    
    // Resource
    private readonly ICommandList m_commandList;
    private readonly ImGuiNETController m_controller;

    // Contexts
    private readonly IntPtr m_mainMainContextPtrImpl;
    private readonly IntPtr m_virtualContextPtrImpl;

    // Fonts
    private float m_zoomRate;
    private ImGuiAlias m_currentFont;
    private readonly float m_dpiScale;

    private readonly Dictionary<ImGuiFontSize, ImFontPtr> m_fontRegular = new();
    private readonly Dictionary<ImGuiFontSize, ImFontPtr> m_fontBold = new();
    private readonly Dictionary<ImGuiFontSize, ImFontPtr> m_fontItalic = new();
    private readonly Dictionary<ImGuiFontSize, ImFontPtr> m_fontBoldItalic = new();
    private readonly Dictionary<ImGuiFontSize, ImFontPtr> m_icon = new();

    // Ini
    private readonly string m_iniPath;
    private DateTime m_lastIniWriteUtc;
    
    // Payloads
    private readonly Dictionary<int, object> m_payloadObjects = new();
    private int m_nextPayloadId = 1;
    
    // Invisible
    private bool m_inInvisible = false;
    private Vector2 m_invisibleSizeCache = Vector2.ZERO;

    #region Inits
    public ImGuiNETBackend(
	    IWindowSystem windowSystem, 
	    IDisplaySystem displaySystem, 
	    IGraphicsDevice graphicsDevice,
	    ImGuiColorSpaceHandling colorSpaceHandling)
    {
	    m_windowSystem = windowSystem;
	    m_graphicsDevice = graphicsDevice;
	    
	    // Window
	    var dpiScaleVec2 = m_windowSystem.mainWindow.GetFrameBufferScale();
	    m_dpiScale = MathF.Max(dpiScaleVec2.x, dpiScaleVec2.y);
	    
	    // ImGui Renderer
        m_commandList = graphicsDevice.CreateCommandList();
        m_controller = new ImGuiNETController(windowSystem, displaySystem, graphicsDevice, colorSpaceHandling);

        // Fonts: caller/editor owns the policy; platform ships sane defaults.
        m_controller.ClearAllFonts();
        SetupFonts(m_dpiScale);
        SetupIcons(m_dpiScale);
        m_controller.RebuildFontTexture();

        // Main Context
        m_mainMainContextPtrImpl = ImGuiNET.ImGui.GetCurrentContext();
        ImGuiNET.ImGui.SetCurrentContext(m_mainMainContextPtrImpl);
        ImGuiNET.ImGui.GetIO().FontGlobalScale = 1f / m_dpiScale;
        SetupStyle();
        
		// Main IO
		m_iniPath = ImGuiNET.ImGui.GetIO().IniFilename;
		ImGuiNET.ImGui.LoadIniSettingsFromDisk(m_iniPath);
		ImGuiIniDataFile.LoadAndEnsure(m_iniPath);
		m_lastIniWriteUtc = File.Exists(m_iniPath) ? File.GetLastWriteTimeUtc(m_iniPath) : DateTime.MinValue;

		// Virtual Context
		unsafe
		{
			m_virtualContextPtrImpl = ImGuiNET.ImGui.CreateContext(ImGuiNET.ImGui.GetIO().Fonts.NativePtr);
			ImGuiNET.ImGui.SetCurrentContext(m_virtualContextPtrImpl);
			ImGuiNET.ImGui.GetIO().FontGlobalScale = 1f / m_dpiScale;
			SetupStyle();
		}
		
		// Virtual IO: Ensure virtual context never saves ini (avoid main/virtual competing)
		unsafe
		{
			var vio = ImGuiNET.ImGui.GetIO();
			vio.WantSaveIniSettings = false;
			vio.NativePtr->IniFilename = null;
		}

		ImGuiNET.ImGui.SetCurrentContext(m_mainMainContextPtrImpl);
    }
    
	private void SetupStyle()
	{
	    var style = ImGuiNET.ImGui.GetStyle();

	    style.Alpha = 1.0f;
	    style.DisabledAlpha = 0.1f;

	    // --- Window / Child / Popup ---
	    style.WindowPadding = new Vector2(6.0f, 6.0f);
	    style.WindowRounding = 2.0f;
	    style.WindowBorderSize = 1.0f;
	    style.WindowMinSize = new Vector2(30.0f, 30.0f);
	    style.WindowTitleAlign = new Vector2(0.5f, 0.5f);
	    style.WindowMenuButtonPosition = ImGuiDir.Right;

	    style.ChildRounding = 2.0f;
	    style.ChildBorderSize = 1.0f;

	    style.PopupRounding = 2.0f;
	    style.PopupBorderSize = 0.0f;

	    // --- Frame / Items ---
	    style.FramePadding = new Vector2(6.0f, 2.0f);
	    style.FrameRounding = 2.0f;
	    style.FrameBorderSize = 0.0f;

	    style.ItemSpacing = new Vector2(4.0f, 3.0f);
	    style.ItemInnerSpacing = new Vector2(4.0f, 4.0f);
	    style.CellPadding = new Vector2(3.0f, 2.0f);

	    style.IndentSpacing = 20.0f;
	    style.ColumnsMinSpacing = 4.0f;

	    // --- Scrollbar / Grab ---
	    style.ScrollbarSize = 12.0f;
	    style.ScrollbarRounding = 2.0f;
	    style.GrabMinSize = 12.0f;
	    style.GrabRounding = 2.0f;

	    // --- Tabs ---
	    style.TabRounding = 2.0f;
	    style.TabBorderSize = 0.0f;
	    style.TabMinWidthForCloseButton = 0.0f;

	    style.ColorButtonPosition = ImGuiDir.Right;
	    style.ButtonTextAlign = new Vector2(0.5f, 0.5f);
	    style.SelectableTextAlign = new Vector2(0.0f, 0.0f);

	    // --- Colors ---
	    style.Colors[(int)ImGuiCol.Text] = new Vector4(1.0f, 1.0f, 1.0f, 1.0f);
	    style.Colors[(int)ImGuiCol.TextDisabled] = new Vector4(1.0f, 1.0f, 1.0f, 0.360515f);
	    style.Colors[(int)ImGuiCol.WindowBg] = new Vector4(0.18f, 0.18f, 0.18f, 1.0f);
	    style.Colors[(int)ImGuiCol.ChildBg] = new Vector4(1.0f, 0.0f, 0.0f, 0.0f);
	    style.Colors[(int)ImGuiCol.PopupBg] = new Vector4(0.09803922f, 0.09803922f, 0.09803922f, 1.0f);
	    style.Colors[(int)ImGuiCol.Border] = new Vector4(0.32f, 0.34f, 0.37f, 0.65f);
	    style.Colors[(int)ImGuiCol.BorderShadow] = new Vector4(0.0f, 0.0f, 0.0f, 0.45f);
	    style.Colors[(int)ImGuiCol.FrameBg] = new Vector4(0.15686275f, 0.15686275f, 0.15686275f, 1.0f);
	    style.Colors[(int)ImGuiCol.FrameBgHovered] = new Vector4(0.38039216f, 0.42352942f, 0.57254905f, 0.54901963f);
	    style.Colors[(int)ImGuiCol.FrameBgActive] = new Vector4(0.61960787f, 0.5764706f, 0.76862746f, 0.54901963f);
	    style.Colors[(int)ImGuiCol.TitleBg] = new Vector4(0.14f, 0.14f, 0.14f, 1.0f);
	    style.Colors[(int)ImGuiCol.TitleBgActive] = new Vector4(0.18f, 0.18f, 0.18f, 1.0f);
	    style.Colors[(int)ImGuiCol.TitleBgCollapsed] = new Vector4(0.12f, 0.12f, 0.12f, 1.0f);
	    style.Colors[(int)ImGuiCol.MenuBarBg] = new Vector4(0.0f, 0.0f, 0.0f, 0.0f);
	    style.Colors[(int)ImGuiCol.ScrollbarBg] = new Vector4(0.15686275f, 0.15686275f, 0.15686275f, 0.0f);
	    style.Colors[(int)ImGuiCol.ScrollbarGrab] = new Vector4(0.15686275f, 0.15686275f, 0.15686275f, 1.0f);
	    style.Colors[(int)ImGuiCol.ScrollbarGrabHovered] = new Vector4(0.23529412f, 0.23529412f, 0.23529412f, 1.0f);
	    style.Colors[(int)ImGuiCol.ScrollbarGrabActive] = new Vector4(0.29411766f, 0.29411766f, 0.29411766f, 1.0f);
	    style.Colors[(int)ImGuiCol.CheckMark] = new Vector4(0.29411766f, 0.29411766f, 0.29411766f, 1.0f);
	    style.Colors[(int)ImGuiCol.SliderGrab] = new Vector4(0.61960787f, 0.5764706f, 0.76862746f, 0.54901963f);
	    style.Colors[(int)ImGuiCol.SliderGrabActive] = new Vector4(0.8156863f, 0.77254903f, 0.9647059f, 0.54901963f);
	    style.Colors[(int)ImGuiCol.Button] = new Vector4(0.61960787f, 0.5764706f, 0.76862746f, 0.54901963f);
	    style.Colors[(int)ImGuiCol.ButtonHovered] = new Vector4(0.7372549f, 0.69411767f, 0.8862745f, 0.54901963f);
	    style.Colors[(int)ImGuiCol.ButtonActive] = new Vector4(0.8156863f, 0.77254903f, 0.9647059f, 0.54901963f);
	    style.Colors[(int)ImGuiCol.Header] = new Vector4(0.61960787f, 0.5764706f, 0.76862746f, 0.54901963f);
	    style.Colors[(int)ImGuiCol.HeaderHovered] = new Vector4(0.7372549f, 0.69411767f, 0.8862745f, 0.54901963f);
	    style.Colors[(int)ImGuiCol.HeaderActive] = new Vector4(0.8156863f, 0.77254903f, 0.9647059f, 0.54901963f);
	    style.Colors[(int)ImGuiCol.Separator] = new Vector4(0.61960787f, 0.5764706f, 0.76862746f, 0.54901963f);
	    style.Colors[(int)ImGuiCol.SeparatorHovered] = new Vector4(0.7372549f, 0.69411767f, 0.8862745f, 0.54901963f);
	    style.Colors[(int)ImGuiCol.SeparatorActive] = new Vector4(0.8156863f, 0.77254903f, 0.9647059f, 0.54901963f);
	    style.Colors[(int)ImGuiCol.ResizeGrip] = new Vector4(0.61960787f, 0.5764706f, 0.76862746f, 0.54901963f);
	    style.Colors[(int)ImGuiCol.ResizeGripHovered] = new Vector4(0.7372549f, 0.69411767f, 0.8862745f, 0.54901963f);
	    style.Colors[(int)ImGuiCol.ResizeGripActive] = new Vector4(0.8156863f, 0.77254903f, 0.9647059f, 0.54901963f);
	    style.Colors[(int)ImGuiCol.Tab] = new Vector4(0.61960787f, 0.5764706f, 0.76862746f, 0.24901963f);
	    style.Colors[(int)ImGuiCol.TabHovered] = new Vector4(0.7372549f, 0.69411767f, 0.8862745f, 0.74901963f);
	    style.Colors[(int)ImGuiCol.TabSelected] = new Vector4(0.8156863f, 0.77254903f, 0.9647059f, 0.74901963f);
	    style.Colors[(int)ImGuiCol.TabDimmed] = new Vector4(0.61960787f, 0.5764706f, 0.76862746f, 0.24901963f);
	    style.Colors[(int)ImGuiCol.TabDimmedSelected] = new Vector4(0.8156863f, 0.77254903f, 0.9647059f, 0.44901963f);
	    style.Colors[(int)ImGuiCol.TabSelectedOverline] = new Vector4(0.13333334f, 0.25882354f, 0.42352942f, 0.0f);
	    style.Colors[(int)ImGuiCol.PlotLines] = new Vector4(0.29411766f, 0.29411766f, 0.29411766f, 1.0f);
	    style.Colors[(int)ImGuiCol.PlotLinesHovered] = new Vector4(0.7372549f, 0.69411767f, 0.8862745f, 0.54901963f);
	    style.Colors[(int)ImGuiCol.PlotHistogram] = new Vector4(0.61960787f, 0.5764706f, 0.76862746f, 0.54901963f);
	    style.Colors[(int)ImGuiCol.PlotHistogramHovered] = new Vector4(0.7372549f, 0.69411767f, 0.8862745f, 0.54901963f);
	    style.Colors[(int)ImGuiCol.TableHeaderBg] = new Vector4(0.1882353f, 0.1882353f, 0.2f, 1.0f);
	    style.Colors[(int)ImGuiCol.TableBorderStrong] = new Vector4(0.42352942f, 0.38039216f, 0.57254905f, 0.54901963f);
	    style.Colors[(int)ImGuiCol.TableBorderLight] = new Vector4(0.42352942f, 0.38039216f, 0.57254905f, 0.2918455f);
	    style.Colors[(int)ImGuiCol.TableRowBg] = new Vector4(0.0f, 0.0f, 0.0f, 0.0f);
	    style.Colors[(int)ImGuiCol.TableRowBgAlt] = new Vector4(1.0f, 1.0f, 1.0f, 0.03433478f);
	    style.Colors[(int)ImGuiCol.TextSelectedBg] = new Vector4(0.7372549f, 0.69411767f, 0.8862745f, 0.54901963f);
	    style.Colors[(int)ImGuiCol.DragDropTarget] = new Vector4(1.0f, 1.0f, 0.0f, 0.9f);
	    style.Colors[(int)ImGuiCol.NavWindowingHighlight] = new Vector4(1.0f, 1.0f, 1.0f, 0.7f);
	    style.Colors[(int)ImGuiCol.NavWindowingDimBg] = new Vector4(0.8f, 0.8f, 0.8f, 0.2f);
	    style.Colors[(int)ImGuiCol.ModalWindowDimBg] = new Vector4(0.8f, 0.8f, 0.8f, 0.35f);
	    style.Colors[(int)ImGuiCol.DockingPreview] = new Vector4(0.8156863f, 0.77254903f, 0.9647059f, 0.54901963f);
	}
    
    #endregion

    #region Life Cycle
    
    public void BeginLayoutImpl(float deltaTime)
    {
	    // Begin Render
	    m_commandList.Begin();
	    m_commandList.SetFrameBuffer(m_graphicsDevice.swapchainFrameBuffer);
	    
	    // Virtual Context
	    ImGuiNET.ImGui.SetCurrentContext(m_virtualContextPtrImpl);
	    ImGuiNET.ImGui.GetIO().DisplaySize = new Vector2(m_windowSystem.mainWindow.size.x, m_windowSystem.mainWindow.size.y);
	    ImGuiNET.ImGui.NewFrame();
	    ImGuiNET.ImGui.PushFont(m_fontRegular[ImGuiHost.C_DEFAULT_FONT_SIZE]);

	    // Main Context
	    ImGuiNET.ImGui.SetCurrentContext(m_mainMainContextPtrImpl);
	    m_controller.Update(deltaTime, m_windowSystem.mainWindow.GetPumpedEvents());
	    ImGuiNET.ImGui.PushFont(m_fontRegular[ImGuiHost.C_DEFAULT_FONT_SIZE]);

        // Default font
        UseFontImpl(ImGuiFontStyle.Regular, (float)ImGuiHost.C_DEFAULT_FONT_SIZE);
    }

    public void EndLayoutImpl()
    {
	    // Clear Cache
	    ClearDragPayloadCache();
		    
	    // Virtual Context
	    ImGuiNET.ImGui.SetCurrentContext(m_virtualContextPtrImpl);
	    ImGuiNET.ImGui.PopFont();
	    ImGuiNET.ImGui.EndFrame();
	    
	    // Main Context
	    ImGuiNET.ImGui.SetCurrentContext(m_mainMainContextPtrImpl);
	    ImGuiNET.ImGui.PopFont();
	    
		// Render
	    m_controller.Render(m_commandList);

        // Ini self-heal
        if (!string.IsNullOrWhiteSpace(m_iniPath) && File.Exists(m_iniPath))
        {
            var writeUtc = File.GetLastWriteTimeUtc(m_iniPath);
            if (writeUtc != m_lastIniWriteUtc)
            {
                ImGuiIniDataFile.EnsureSectionPresent(m_iniPath);
                m_lastIniWriteUtc = File.GetLastWriteTimeUtc(m_iniPath);
            }
        }

        m_commandList.End();
        m_graphicsDevice.Submit(m_commandList);
    }
    
    #endregion
    
    #region Texture

    public IntPtr GetOrBindTextureImpl(ITexture texture) => m_controller.GetOrBindTexture(texture);

    public void UnbindTextureImpl(ITexture texture) => m_controller.UnbindTexture(texture);

    #endregion
    
    #region Fonts
    
    public void UseFontImpl(ImGuiFontStyle style, float? size)
    {
	    m_currentFont = size == null ? new ImGuiAlias(style, m_currentFont.size) : new ImGuiAlias(style, (float)size);
	    var sizeInFloat = m_currentFont.size * m_zoomRate;
	    
	    // 1. Select font family by style
	    var family = style switch
	    {
		    ImGuiFontStyle.Bold       => m_fontBold,
		    ImGuiFontStyle.Italic     => m_fontItalic,
		    ImGuiFontStyle.BoldItalic => m_fontBoldItalic,
		    
		    ImGuiFontStyle.Icon		  => m_icon,
		    _                         => m_fontRegular
	    };

	    // 3. Iterate enum to find exact or nearest
	    ImGuiFontSize nearest = default;
	    float nearestDist = float.MaxValue;
	    bool exactMatch = false;

	    foreach (ImGuiFontSize s in Enum.GetValues(typeof(ImGuiFontSize)))
	    {
		    float enumSize = (float)s;
		    float dist = MathF.Abs(sizeInFloat - enumSize);

		    // exact hit (with tolerance)
		    if (MathHelper.AlmostEquals(sizeInFloat, enumSize))
		    {
			    nearest = s;
			    exactMatch = true;
			    break;
		    }

		    // nearest
		    if (dist < nearestDist)
		    {
			    nearestDist = dist;
			    nearest = s;
		    }
	    }

	    float scale = exactMatch ? 1f : sizeInFloat / (float)nearest;
	    var font = family[nearest];

	    // 4. Apply to virtual context
	    ImGuiNET.ImGui.SetCurrentContext(m_virtualContextPtrImpl);
	    ImGuiNET.ImGui.PopFont();
	    ImGuiNET.ImGui.GetIO().FontGlobalScale = scale / m_dpiScale;
	    ImGuiNET.ImGui.PushFont(font);

	    // 5. Apply to main context
	    ImGuiNET.ImGui.SetCurrentContext(m_mainMainContextPtrImpl);
	    ImGuiNET.ImGui.PopFont();
	    ImGuiNET.ImGui.GetIO().FontGlobalScale = scale / m_dpiScale;
	    ImGuiNET.ImGui.PushFont(font);
	    
	    // 6. Apply to default font
	    unsafe
	    {
		    ImGuiNET.ImGui.GetIO().NativePtr->FontDefault = font;
	    }
    }
    
    public ImGuiAlias GetCurrentFontImpl() => m_currentFont;
    
    public void ZoomImpl(float zoomRate) => m_zoomRate = zoomRate;
    
    private void SetupFonts(float scale)
    {
	    foreach (var fontSize in Enum.GetValues<ImGuiFontSize>())
	    {
		    var sizePixels = (float)fontSize * scale;

		    m_fontRegular[fontSize] = m_controller.AddFontBase("JetBrainsMono-Regular.ttf", sizePixels);
		    m_fontBold[fontSize] = m_controller.AddFontBase("JetBrainsMono-Bold.ttf", sizePixels);
		    m_fontItalic[fontSize] = m_controller.AddFontBase("JetBrainsMono-Italic.ttf", sizePixels);
		    m_fontBoldItalic[fontSize] = m_controller.AddFontBase("JetBrainsMono-BoldItalic.ttf", sizePixels);
	    }
    }
    
    private void SetupIcons(float scale)
    {
	    const float c_iconScaleMultiplier = 0.8f;
		
	    foreach (var fontSize in Enum.GetValues<ImGuiFontSize>())
	    {
		    var sizePixels = (float)fontSize * scale;
		    
		    m_controller.RegisterFontIcon(ImGuiIcon.FontIconFileNameFAR, sizePixels * c_iconScaleMultiplier, (ImGuiIcon.IconMin, ImGuiIcon.IconMax));
		    m_controller.RegisterFontIcon(ImGuiIcon.FontIconFileNameFAS, sizePixels * c_iconScaleMultiplier, (ImGuiIcon.IconMin, ImGuiIcon.IconMax));

		    m_icon[fontSize] = m_controller.AddIcons();
	    }
    }
    
    #endregion

    #region Storage

    public void SetStorageDataImpl(string key, object? value)
    {
        ImGuiDataStore.DATA[key] = ImGuiDataCodec.Encode(value);
        ImGuiNET.ImGui.GetIO().WantSaveIniSettings = true;
    }

    public T? GetStorageDataImpl<T>(string key, T? defaultValue)
    {
        return ImGuiDataStore.DATA.TryGetValue(key, out var payload)
            ? ImGuiDataCodec.Decode(payload, defaultValue)
            : defaultValue;
    }
    
    #endregion
    
	#region Payload

	public void SetDragPayloadImpl<T>(string type, in T data)
	{
	    ClearDragPayloadCache();

	#if DEBUG
	    if (typeof(T).IsByRefLike)
	        throw new NotSupportedException($"Drag payload does not support ref-like type: {typeof(T)}");
	#else
	    if (typeof(T).IsByRefLike)
	        return;
	#endif

	    if (!RuntimeHelpers.IsReferenceOrContainsReferences<T>())
	    {
	        SetDragPayloadRaw(type, in data);
	        return;
	    }

	    SetDragPayloadObject(type, data!);
	}

	public bool TryAcceptDragPayloadImpl<T>(string type, out T value, Predicate<T>? predicate = null)
	{
	    ClearDragPayloadCache();

	#if DEBUG
	    if (typeof(T).IsByRefLike)
	        throw new NotSupportedException($"Drag payload does not support ref-like type: {typeof(T)}");
	#else
	    if (typeof(T).IsByRefLike)
	    {
	        value = default!;
	        return false;
	    }
	#endif

	    if (!TryPeekDragPayload<T>(type, out var peek))
	    {
	        value = default!;
	        return false;
	    }

	    if (predicate != null && !predicate(peek))
	    {
	        value = default!;
	        return false;
	    }

	    return TryDeliverDragPayload(type, out value);
	}

	private bool TryPeekDragPayload<T>(string type, out T value)
	{
	    if (!RuntimeHelpers.IsReferenceOrContainsReferences<T>())
	        return TryPeekDragPayloadRaw(type, out value);

	    if (TryPeekDragPayloadObject(type, out var obj) && obj is T t)
	    {
	        value = t;
	        return true;
	    }

	    value = default!;
	    return false;
	}

	private bool TryDeliverDragPayload<T>(string type, out T value)
	{
	    var flags = ImGuiDragDropFlags.AcceptBeforeDelivery;

	    if (!RuntimeHelpers.IsReferenceOrContainsReferences<T>())
	        return TryAcceptDragPayloadRaw(type, out value, flags);

	    if (TryAcceptDragPayloadObject(type, out var obj, flags) && obj is T t)
	    {
	        value = t;
	        return true;
	    }

	    value = default!;
	    return false;
	}

	private unsafe void SetDragPayloadRaw<T>(string type, in T data)
	{
	    int size = Unsafe.SizeOf<T>();
	    byte* buf = stackalloc byte[size];
	    Unsafe.CopyBlockUnaligned(buf, Unsafe.AsPointer(ref Unsafe.AsRef(in data)), (uint)size);
	    ImGuiNET.ImGui.SetDragDropPayload(type, (IntPtr)buf, (uint)size);
	}

	private unsafe bool TryPeekDragPayloadRaw<T>(string type, out T value)
	{
	    var payload = ImGuiNET.ImGui.GetDragDropPayload();
	    if (payload.NativePtr == null || !payload.IsDataType(type) || payload.Data == IntPtr.Zero || payload.DataSize <= 0)
	    {
	        value = default!;
	        return false;
	    }

	    int size = Unsafe.SizeOf<T>();
	    if (payload.DataSize < size)
	    {
	        value = default!;
	        return false;
	    }

	    value = Unsafe.ReadUnaligned<T>(payload.Data.ToPointer());
	    return true;
	}

	private unsafe bool TryAcceptDragPayloadRaw<T>(
	    string type,
	    out T value,
	    ImGuiNET.ImGuiDragDropFlags flags)
	{
	    var payload = ImGuiNET.ImGui.AcceptDragDropPayload(type, flags);
	    if (payload.NativePtr == null || payload.Data == IntPtr.Zero || payload.DataSize <= 0 || !payload.Delivery)
	    {
	        value = default!;
	        return false;
	    }

	    int size = Unsafe.SizeOf<T>();
	    if (payload.DataSize < size)
	    {
	        value = default!;
	        return false;
	    }

	    value = Unsafe.ReadUnaligned<T>(payload.Data.ToPointer());
	    return true;
	}

	private void SetDragPayloadObject(string type, object obj)
	{
	    int id = m_nextPayloadId++;
	    m_payloadObjects[id] = obj;
	    SetDragPayloadRaw(type, id);
	}

	private bool TryPeekDragPayloadObject(string type, out object obj)
	{
	    obj = null!;
	    return TryPeekDragPayloadRaw(type, out int pid) && m_payloadObjects.TryGetValue(pid, out obj!);
	}

	private bool TryAcceptDragPayloadObject(
	    string type,
	    out object obj,
	    ImGuiDragDropFlags flags)
	{
	    obj = null!;
	    var payload = ImGuiNET.ImGui.AcceptDragDropPayload(type, flags);
	    unsafe
	    {
		    if (payload.NativePtr == null || payload.Data == IntPtr.Zero || payload.DataSize < sizeof(int))
			    return false;

	        int pid = Unsafe.ReadUnaligned<int>(payload.Data.ToPointer());
	        if (!m_payloadObjects.TryGetValue(pid, out obj!))
	            return false;

	        if (!payload.Delivery)
	        {
	            obj = null!;
	            return false;
	        }

	        m_payloadObjects.Remove(pid);
	        return true;
	    }
	}

	private void ClearDragPayloadCache()
	{
	    if (m_payloadObjects.Count == 0)
	        return;

	    unsafe
	    {
	        if (ImGuiNET.ImGui.GetDragDropPayload().NativePtr == null)
	            m_payloadObjects.Clear();
	    }
	}

	#endregion


	#region Invisible

	public void BeginInvisibleImpl()
	{
		if (m_inInvisible) throw new InvalidOperationException("Cannot nest invisible groups.");
		m_inInvisible = true;

		var currentAvailSize = ImGuiNET.ImGui.GetContentRegionAvail();
        
		ImGuiNET.ImGui.SetCurrentContext(m_virtualContextPtrImpl);
		ImGuiNET.ImGui.PushID("INVISIBLE_ID");
		ImGuiNET.ImGui.BeginChild("INVISIBLE_GROUP", currentAvailSize);
		ImGuiNET.ImGui.BeginGroup();
	}

	public void EndInvisibleImpl()
	{
		ImGuiNET.ImGui.EndGroup();
		m_invisibleSizeCache = new Vector2(ImGuiNET.ImGui.GetItemRectSize().X, ImGuiNET.ImGui.GetItemRectSize().Y);
		ImGuiNET.ImGui.EndChild();
		ImGuiNET.ImGui.PopID();
		ImGuiNET.ImGui.SetCurrentContext(m_mainMainContextPtrImpl);
        
		m_inInvisible = false;
	}

	public Vector2 GetInvisibleItemSizeImpl()
	{
		return m_invisibleSizeCache;
	}

	#endregion

    public void Dispose()
    {
        m_controller.Dispose();
        m_commandList.Dispose();
    }
}
