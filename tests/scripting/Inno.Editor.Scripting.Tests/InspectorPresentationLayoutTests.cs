using System;
using System.Numerics;
using Inno.Editor.ImGui;
using Inno.Native.ImGui;
using EditorWidget = Inno.Editor.ImGui.ImGuiWidget.ImGuiWidget;
using NativeImGui = Inno.Native.ImGui.ImGui;
using Xunit;

namespace Inno.Editor.Scripting.Tests;

public sealed class InspectorPresentationLayoutTests : IDisposable
{
    private readonly ImGuiContextPtr m_context = NativeImGui.CreateContext();

    public InspectorPresentationLayoutTests()
    {
        ImGuiIOPtr io = NativeImGui.GetIO();
        io.DisplaySize = new Vector2(640, 480);
        io.DeltaTime = 1f / 60f;
        io.BackendFlags |= ImGuiBackendFlags.RendererHasTextures;
        io.Fonts.RendererHasTextures = true;
        EditorWidget.style.ResetZoom();
        EditorWidget.SetupStyle();
    }

    public void Dispose() => NativeImGui.DestroyContext(m_context);

    [Fact]
    public void FloatingPanelCloseButtonClosesOnMouseRelease()
    {
        bool open = true;
        void Frame()
        {
            NativeImGui.NewFrame();
            NativeImGui.SetNextWindowPos(new Vector2(30, 30));
            NativeImGui.SetNextWindowSize(new Vector2(320, 180));
            EditorWidget.PanelWindow("Floating acceptance", ref open, () => NativeImGui.TextUnformatted("Body"));
            NativeImGui.Render();
        }
        Frame();
        Frame();
        ImGuiWindowPtr window = ImGuiP.FindWindowByName("Floating acceptance");
        ImGuiIOPtr io = NativeImGui.GetIO();
        io.AddMousePosEvent(window.Pos.X + window.Size.X - 12, window.Pos.Y + window.TitleBarHeight * 0.5f);
        Frame();
        io.AddMouseButtonEvent(0, true);
        Frame();
        Assert.True(open);
        io.AddMouseButtonEvent(0, false);
        Frame();
        Assert.False(open);
    }

    [Theory]
    [InlineData(640, 480, 1f)]
    [InlineData(360, 240, 1.5f)]
    public void TooltipAtBottomRightFitsParentViewportAndAllText(int width, int height, float zoom)
    {
        ImGuiIOPtr io = NativeImGui.GetIO();
        io.DisplaySize = new Vector2(width, height);
        EditorWidget.style.SetZoom(zoom);
        EditorWidget.SetupStyle();
        Vector2 hover = default;
        const string text = "The profile is evaluated after this camera stack is composed. The final words must remain readable.";
        void Frame()
        {
            NativeImGui.NewFrame();
            NativeImGui.SetNextWindowPos(new Vector2(width - 210, height - 80));
            NativeImGui.SetNextWindowSize(new Vector2(200, 70));
            NativeImGui.Begin("Edge acceptance");
            NativeImGui.Button("Hover near edge");
            hover = (NativeImGui.GetItemRectMin() + NativeImGui.GetItemRectMax()) * 0.5f;
            EditorWidget.DrawItemTooltip(text);
            NativeImGui.End();
            NativeImGui.Render();
        }
        Frame();
        Frame();
        io.AddMousePosEvent(hover.X, hover.Y);
        for (int index = 0; index < 4; index++) Frame();
        ImGuiWindowPtr tooltip = ImGuiP.FindWindowByName("##Tooltip_00");
        Assert.False(tooltip.IsNull);
        Assert.True(tooltip.Active);
        Assert.InRange(tooltip.Pos.X, 0, width);
        Assert.InRange(tooltip.Pos.Y, 0, height);
        Assert.True(tooltip.Pos.X + tooltip.Size.X <= width);
        Assert.True(tooltip.Pos.Y + tooltip.Size.Y <= height);
        Assert.True(tooltip.ContentSize.X <= tooltip.InnerRect.Max.X - tooltip.InnerRect.Min.X + 1);
        Assert.True(tooltip.ContentSize.Y <= tooltip.InnerRect.Max.Y - tooltip.InnerRect.Min.Y + 1);
    }

    [Fact]
    public void HelpBoxUsesWrappedCardHeightWithoutHorizontalOverflow()
    {
        for (int index = 0; index < 2; index++)
        {
            NativeImGui.NewFrame();
            NativeImGui.SetNextWindowSize(new Vector2(280, 220));
            NativeImGui.Begin("HelpBox acceptance");
            float width = NativeImGui.GetContentRegionAvail().X;
            EditorWidget.HelpBox("Assign a Sprite to enable atlas geometry, borders, pixel density, and sampling controls.", "i", EditorPalette.warning);
            Vector2 size = NativeImGui.GetItemRectSize();
            Assert.Equal(width, size.X, 2);
            Assert.True(size.Y > NativeImGui.GetTextLineHeight() * 2);
            NativeImGui.End();
            NativeImGui.Render();
        }
    }

    [Theory]
    [InlineData(0.9f, 1f)]
    [InlineData(0.9f, 2f)]
    [InlineData(1f, 1f)]
    [InlineData(1f, 2f)]
    [InlineData(1.25f, 1.5f)]
    [InlineData(1.5f, 2f)]
    public void RoundedWindowTitleAndBodyHaveContinuousOpaqueCoverage(float zoom, float framebufferScale)
    {
        NativeImGui.GetIO().DisplayFramebufferScale = new Vector2(framebufferScale);
        EditorWidget.style.SetZoom(zoom);
        EditorWidget.SetupStyle();
        NativeImGui.GetStyle().WindowRounding = 6f * zoom;
        NativeImGui.GetStyle().FrameBorderSize = 0;
        for (int frame = 0; frame < 2; frame++)
        {
            NativeImGui.NewFrame();
            NativeImGui.SetNextWindowPos(new Vector2(30, 30));
            NativeImGui.SetNextWindowSize(new Vector2(320, 180));
            NativeImGui.Begin("Opaque surface", ImGuiWindowFlags.NoCollapse);
            NativeImGui.End();
            NativeImGui.Render();
        }

        ImGuiWindowPtr window = ImGuiP.FindWindowByName("Opaque surface");
        float seam = window.Pos.Y + window.TitleBarHeight;
        // Sample subpixel positions as well as device pixel centers. A border line must not
        // conceal a transparent join between independently anti-aliased background shapes.
        for (int sample = -10; sample <= 10; sample++)
        {
            Vector2 point = new(window.Pos.X + window.Size.X * 0.47f, seam + sample * 0.1f);
            float coverage = CoverageAt(window.DrawList, point);
            Assert.True(coverage >= 0.999f, $"Transparent title/body join: zoom={zoom}, scale={framebufferScale}, y={point.Y}, alpha={coverage}");
        }
        for (int pixel = -2; pixel <= 2; pixel++)
        {
            float y = (MathF.Floor(seam * framebufferScale) + pixel + 0.5f) / framebufferScale;
            Vector2 point = new(window.Pos.X + window.Size.X * 0.47f, y);
            Assert.True(CoverageAt(window.DrawList, point) >= 0.999f,
                $"Transparent device pixel: zoom={zoom}, scale={framebufferScale}, y={y}");
        }
        Assert.True(NativeImGui.GetStyle().AntiAliasedFill);
        Assert.Equal(6f * zoom, window.WindowRounding);
    }

    private static float CoverageAt(ImDrawListPtr list, Vector2 point)
    {
        float result = 0;
        for (int commandIndex = 0; commandIndex < list.CmdBuffer.Size; commandIndex++)
        {
            ImDrawCmd command = list.CmdBuffer[commandIndex];
            Vector4 clip = command.ClipRect;
            if (point.X < clip.X || point.X >= clip.Z || point.Y < clip.Y || point.Y >= clip.W)
                continue;
            for (uint index = command.IdxOffset; index < command.IdxOffset + command.ElemCount; index += 3)
            {
                ImDrawVert a = list.VtxBuffer[(int)command.VtxOffset + list.IdxBuffer[(int)index]];
                ImDrawVert b = list.VtxBuffer[(int)command.VtxOffset + list.IdxBuffer[(int)index + 1]];
                ImDrawVert c = list.VtxBuffer[(int)command.VtxOffset + list.IdxBuffer[(int)index + 2]];
                float area = Cross(b.Pos - a.Pos, c.Pos - a.Pos);
                if (MathF.Abs(area) < 0.000001f) continue;
                float wa = Cross(b.Pos - point, c.Pos - point) / area;
                float wb = Cross(c.Pos - point, a.Pos - point) / area;
                float wc = 1 - wa - wb;
                if (wa < 0 || wb < 0 || wc < 0) continue;
                float alpha = (wa * (a.Col >> 24) + wb * (b.Col >> 24) + wc * (c.Col >> 24)) / 255f;
                result = alpha + result * (1 - alpha);
            }
        }
        return result;
    }

    private static float Cross(Vector2 a, Vector2 b) => a.X * b.Y - a.Y * b.X;
}
