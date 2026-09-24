using Xunit;
using Xunit.Abstractions;

namespace Inno.Native.ImGuizmo.Tests;

public sealed class ImGuizmoInitTests
{
    private readonly ITestOutputHelper output;

    public ImGuizmoInitTests(ITestOutputHelper output)
    {
        this.output = output;
    }

    [Fact]
    public void Init_ShouldLoad()
    {
        bool isUsing = ImGuizmo.IsUsing();
        output.WriteLine($"ImGuizmo.IsUsing: {isUsing}");
        Assert.False(isUsing);
    }
}
