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
        var libraryName = ImGuizmo.GetLibraryName();
        output.WriteLine($"ImGuizmo.GetLibraryName: {libraryName}");
        Assert.Equal("cimguizmo", libraryName);
    }
}
