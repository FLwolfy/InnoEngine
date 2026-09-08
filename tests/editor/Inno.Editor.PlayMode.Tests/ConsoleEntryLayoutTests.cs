using System.Collections.Generic;
using System.Numerics;

using Inno.Native.ImGui;

using NativeImGui = Inno.Native.ImGui.ImGui;

using Xunit;

namespace Inno.Editor.PlayMode.Tests;

public sealed class ConsoleEntryLayoutTests
{
    [Fact]
    public void DistinctEntryDomainsDoNotReuseImGuiCardIdentity()
    {
        Assert.Equal((-2L).GetHashCode(), 1L.GetHashCode());

        ImGuiContextPtr context = NativeImGui.CreateContext();
        try
        {
            ConfigureContext();
            NativeImGui.NewFrame();
            _ = NativeImGui.Begin("Console identity test");

            uint logCardId = GetCardId("occurrence/log/1");
            uint diagnosticCardId = GetCardId("occurrence/diagnostic/1");

            Assert.NotEqual(logCardId, diagnosticCardId);
            NativeImGui.End();
            NativeImGui.Render();
        }
        finally
        {
            NativeImGui.DestroyContext(context);
        }
    }

    [Fact]
    public void CollapsedCardsUseOneUniformNativeItemSpacing()
    {
        ImGuiContextPtr context = NativeImGui.CreateContext();
        try
        {
            ConfigureContext();
            DrawCardFrame(assertSpacing: false);
            DrawCardFrame(assertSpacing: true);
        }
        finally
        {
            NativeImGui.DestroyContext(context);
        }
    }

    private static uint GetCardId(string identity)
    {
        NativeImGui.PushID(identity);
        uint result = NativeImGui.GetID("##ConsoleEntryCard");
        NativeImGui.PopID();
        return result;
    }

    private static void DrawCardFrame(bool assertSpacing)
    {
        NativeImGui.NewFrame();
        NativeImGui.SetNextWindowSize(new Vector2(480f, 320f), ImGuiCond.Always);
        _ = NativeImGui.Begin("Console spacing test");

        string[] identities =
        [
            "occurrence/log/1",
            "occurrence/diagnostic/1",
            "occurrence/log/2"
        ];
        var bounds = new List<(Vector2 minimum, Vector2 maximum)>();
        for (int i = 0; i < identities.Length; i++)
        {
            NativeImGui.PushID(identities[i]);
            if (NativeImGui.BeginChild(
                    "##ConsoleEntryCard",
                    Vector2.Zero,
                    ImGuiChildFlags.FrameStyle | ImGuiChildFlags.AutoResizeY,
                    ImGuiWindowFlags.NoScrollbar |
                    ImGuiWindowFlags.NoScrollWithMouse |
                    ImGuiWindowFlags.NoSavedSettings))
            {
                NativeImGui.TextUnformatted($"Entry {i}");
            }
            NativeImGui.EndChild();
            bounds.Add((NativeImGui.GetItemRectMin(), NativeImGui.GetItemRectMax()));
            NativeImGui.PopID();
        }

        if (assertSpacing)
        {
            float expectedSpacing = NativeImGui.GetStyle().ItemSpacing.Y;
            float firstGap = bounds[1].minimum.Y - bounds[0].maximum.Y;
            float secondGap = bounds[2].minimum.Y - bounds[1].maximum.Y;
            Assert.Equal(expectedSpacing, firstGap, 3);
            Assert.Equal(expectedSpacing, secondGap, 3);
        }

        NativeImGui.End();
        NativeImGui.Render();
    }

    private static void ConfigureContext()
    {
        ImGuiIOPtr io = NativeImGui.GetIO();
        io.DisplaySize = new Vector2(640f, 480f);
        io.DeltaTime = 1f / 60f;
        io.BackendFlags |= ImGuiBackendFlags.RendererHasTextures;
        io.Fonts.RendererHasTextures = true;
    }
}
