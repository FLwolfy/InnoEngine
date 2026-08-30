using System.Numerics;
using Xunit;
using Xunit.Abstractions;

namespace Inno.Native.ImGui.Tests;

public sealed class ImGuiInitTests
{
    private readonly ITestOutputHelper output;

    public ImGuiInitTests(ITestOutputHelper output)
    {
        this.output = output;
    }

    [Fact]
    public void CreateAndDestroyContext_ShouldSucceed()
    {
        var context = ImGui.CreateContext();
        Assert.False(context.IsNull);

        var version = ImGui.GetVersionS();
        output.WriteLine($"ImGui.GetVersion: {version}");
        Assert.False(string.IsNullOrWhiteSpace(version));

        ImGui.DestroyContext(context);
    }

    [Fact]
    public void TextDisabled_UsesTheLoadedCimguiSymbol()
    {
        var context = ImGui.CreateContext();
        try
        {
            ImGuiIOPtr io = ImGui.GetIO();
            io.DisplaySize = new Vector2(320f, 200f);
            io.DeltaTime = 1f / 60f;
            io.BackendFlags |= ImGuiBackendFlags.RendererHasTextures;
            io.Fonts.RendererHasTextures = true;

            ImGui.NewFrame();
            _ = ImGui.Begin("Native Text Test");
            ImGui.TextDisabled("Plugin status");
            ImGui.End();
            ImGui.Render();
        }
        finally
        {
            ImGui.DestroyContext(context);
        }
    }

    [Fact]
    public void OverlayScrollbarsDoNotReserveWindowContentWidth()
    {
        ImGuiContextPtr context = ImGui.CreateContext();
        try
        {
            ImGuiIOPtr io = ImGui.GetIO();
            io.DisplaySize = new Vector2(640f, 480f);
            io.DeltaTime = 1f / 60f;
            io.BackendFlags |= ImGuiBackendFlags.RendererHasTextures;
            io.ConfigFlags |= ImGuiConfigFlags.InnoOverlayScrollbars;
            io.Fonts.RendererHasTextures = true;
            ImGui.GetStyle().ScrollbarSize = 14f;

            int verticesBeforeEnd = 0;
            int verticesAfterEnd = 0;
            for (int frame = 0; frame < 3; frame++)
            {
                ImGui.NewFrame();
                ImGui.SetNextWindowSize(new Vector2(240f, 140f), ImGuiCond.Always);
                _ = ImGui.Begin("Overlay Scroll Test");
                ImGui.Dummy(new Vector2(80f, 640f));
                if (frame == 1)
                    ImGui.SetScrollY(40f);
                if (frame == 2)
                    verticesBeforeEnd = ImGui.GetForegroundDrawList().VtxBuffer.Size;
                ImGui.End();
                if (frame == 2)
                    verticesAfterEnd = ImGui.GetForegroundDrawList().VtxBuffer.Size;
                ImGui.Render();
            }

            ImGuiWindowPtr window = ImGuiP.FindWindowByName("Overlay Scroll Test");
            Assert.NotEqual(ImGuiWindowPtr.Null, window);
            Assert.True(window.ScrollbarY);
            Assert.Equal(0f, window.ScrollbarSizes.X);
            Assert.True(window.InnerRect.Max.X - window.InnerRect.Min.X > 200f);
            Assert.True(verticesAfterEnd > verticesBeforeEnd);
        }
        finally
        {
            ImGui.DestroyContext(context);
        }
    }
}
