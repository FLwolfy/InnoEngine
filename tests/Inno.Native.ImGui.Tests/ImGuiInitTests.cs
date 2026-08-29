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
}
