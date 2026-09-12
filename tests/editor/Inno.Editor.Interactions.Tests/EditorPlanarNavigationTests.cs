using System;
using System.Numerics;
using Xunit;

namespace Inno.Editor.Interactions.Tests;

public sealed class EditorPlanarNavigationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PanOwnsPointerOutsideCanvasAndReleasesWithoutLocalReleaseEvent(bool primary)
    {
        var navigation = new EditorPlanarNavigation();
        Assert.True(navigation.Update(true, primary, !primary, primary, !primary, primary));
        Assert.True(navigation.Update(false, false, false, primary, !primary, false));
        Assert.False(navigation.Update(false, false, false, false, false, false));
    }

    [Fact]
    public void AltOrbitDoesNotStealPanAndCancelReleasesCapture()
    {
        var navigation = new EditorPlanarNavigation();
        Assert.False(navigation.Update(true, true, false, true, false, true, allowAltPrimary: false));
        Assert.True(navigation.Update(true, false, true, false, true, false));
        navigation.Cancel();
        Assert.False(navigation.isPanning);
    }

    [Fact]
    public void ZoomPreservesPointerAnchorAndIsIndependentOfDisplayDensity()
    {
        Vector2 origin = new(50, 30), pivot = new(110, 150);
        float scale = EditorPlanarNavigation.WheelFactor(3);
        Vector2 updated = EditorPlanarNavigation.ZoomOrigin(origin, pivot, 1, scale);
        Assert.True(Vector2.Distance(pivot - origin, (pivot - updated) / scale) < 0.0001f);
        Vector2 highDpi = EditorPlanarNavigation.ZoomOrigin(origin * 2, pivot * 2, 1, scale);
        Assert.True(Vector2.Distance(updated, highDpi / 2) < 0.0001f);
        Assert.InRange(scale * EditorPlanarNavigation.WheelFactor(-3), 0.999999f, 1.000001f);
        Assert.Throws<ArgumentOutOfRangeException>(() => EditorPlanarNavigation.WheelFactor(float.NaN));
    }
}
