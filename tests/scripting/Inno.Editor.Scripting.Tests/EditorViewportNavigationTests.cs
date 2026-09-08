using System;

using Inno.Core.Mathematics;
using Inno.Editor.Rendering;

using Xunit;

namespace Inno.Editor.Scripting.Tests;

public sealed class EditorViewportNavigationTests
{
    [Fact]
    public void Profile_RejectsDefaultModeMissingFromCapabilities()
    {
        Assert.Throws<ArgumentException>(() => new EditorViewportNavigationProfile(
            new EditorViewportNavigationProfileId("tests.navigation.invalid-default"),
            EditorViewportNavigationCapabilities.Pan,
            EditorViewportNavigationMode.Fly));

        _ = new EditorViewportNavigationProfile(
            new EditorViewportNavigationProfileId("tests.navigation.pan-only"),
            EditorViewportNavigationCapabilities.Pan,
            EditorViewportNavigationMode.Planar);
    }

    [Fact]
    public void ConfigurePerspective_IsAtomicWhenValidationFails()
    {
        var state = new EditorViewportNavigationState();
        state.ConfigureOrthographic(new Vector3(2f, 3f, 4f), Quaternion.identity, 8f);

        Assert.Throws<ArgumentOutOfRangeException>(() => state.ConfigurePerspective(
            new Vector3(40f, 50f, 60f),
            Quaternion.FromEulerAnglesXYZDegrees(new Vector3(10f, 20f, 30f)),
            200f,
            0.1f,
            1000f));

        Assert.Equal(EditorViewportProjection.Orthographic, state.projection);
        Assert.Equal(new Vector3(2f, 3f, 4f), state.position);
        Assert.Equal(Quaternion.identity, state.rotation);
        Assert.Equal(8f, state.orthographicSize);
    }

    [Fact]
    public void State_RejectsUndefinedProjectionAndMode()
    {
        var state = new EditorViewportNavigationState();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            state.projection = (EditorViewportProjection)100);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            state.mode = (EditorViewportNavigationMode)100);
    }
}
