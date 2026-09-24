using System;
using Xunit;

namespace Inno.Native.ImGui.Tests;

public sealed unsafe class ImGuiAbiLayoutTests
{
    [Fact]
    public void ExplicitManagedLayoutsMatchNativeHeaders()
    {
        Assert.Equal(16, sizeof(ImVector<int>));
        Assert.Equal(8, sizeof(ImTextureID));

        ImVector<int> vector = new(2, 3, null);
        Assert.Equal(2, vector.Size);
        Assert.Equal(3, vector.Capacity);
        Assert.Throws<IndexOutOfRangeException>(() => _ = vector[0]);
        Assert.Throws<ArgumentOutOfRangeException>(() => vector.Resize(-1));
    }
}
