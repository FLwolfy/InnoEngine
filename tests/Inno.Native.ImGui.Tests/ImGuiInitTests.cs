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
}
