using System;
using System.Reflection;

using Inno.Engine.Scene.Layers;

using Xunit;

namespace Inno.Editor.Scripting.Tests;

public sealed class GameObjectLayerSelectorTests
{
    [Theory]
    [InlineData(0, "Default", "(0) Default")]
    [InlineData(1, "Player", "(1) Player")]
    [InlineData(31, "Utilities", "(31) Utilities")]
    public void LayerLabelsPlaceTheIndexBeforeTheName(
        int index,
        string name,
        string expected)
    {
        MethodInfo formatter = GetSelectorType().GetMethod(
            "FormatLayerLabel",
            BindingFlags.NonPublic | BindingFlags.Static)!;

        string actual = (string)formatter.Invoke(null, [new GameLayer(index), name])!;

        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("Default", "Default")]
    [InlineData("Player", "Player")]
    [InlineData(null, "Undefined")]
    public void LayerPreviewsDisplayOnlyTheName(string? name, string expected)
    {
        MethodInfo formatter = GetSelectorType().GetMethod(
            "FormatLayerPreview",
            BindingFlags.NonPublic | BindingFlags.Static)!;

        string actual = (string)formatter.Invoke(null, [name])!;

        Assert.Equal(expected, actual);
    }

    private static Type GetSelectorType()
        => Assembly.Load("Inno.Editor.Panel.Inspector").GetType(
            "Inno.Editor.Panel.Inspector.GameObjectLayerSelector",
            throwOnError: true)!;
}
