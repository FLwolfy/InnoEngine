using Inno.Core.Mathematics;
using Inno.Native.ImGui;

namespace Inno.Editor.ImGui;

using NativeImGui = Inno.Native.ImGui.ImGui;

public static partial class ImGuiWidget
{
    public static void SetupStyle()
	{
	    var style = NativeImGui.GetStyle();

	    style.Alpha = 1.0f;
	    style.DisabledAlpha = 0.1f;
	    style.FontScaleMain = 1.25f;

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
	    style.TabBarOverlineSize = 0.0f;
	    
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
	    style.Colors[(int)ImGuiCol.Tab] = new Vector4(0.44f, 0.38f, 0.58f, 0.65f);
	    style.Colors[(int)ImGuiCol.TabHovered] = new Vector4(0.54f, 0.48f, 0.70f, 0.85f);
	    style.Colors[(int)ImGuiCol.TabSelected] = new Vector4(0.62f, 0.56f, 0.80f, 0.95f);
	    style.Colors[(int)ImGuiCol.TabDimmed] = new Vector4(0.32f, 0.29f, 0.42f, 0.60f);
	    style.Colors[(int)ImGuiCol.TabDimmedSelected] = new Vector4(0.50f, 0.45f, 0.67f, 0.80f);
	    style.Colors[(int)ImGuiCol.TabSelectedOverline] = new Vector4(0.72f, 0.66f, 0.90f, 1.0f);
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
}
