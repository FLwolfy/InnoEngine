using System;
using System.Numerics;

using ImGuiNative = Inno.Native.ImGui.ImGui;
using Inno.Native.SDL3;

namespace Inno.Platform.ImGui;

internal static class ImGuiPackedColor
{
    private static bool s_initialized;
    private static int s_rShift;
    private static int s_gShift;
    private static int s_bShift;
    private static int s_aShift;

    internal static void EnsureInitialized()
    {
        if (s_initialized)
        {
            return;
        }

        var probe = new Vector4(0.125f, 0.5f, 0.875f, 1.0f);
        var packed = ImGuiNative.GetColorU32(probe);
        var expectedR = (int)(probe.X * 255.0f + 0.5f);
        var expectedG = (int)(probe.Y * 255.0f + 0.5f);
        var expectedB = (int)(probe.Z * 255.0f + 0.5f);
        var expectedA = (int)(probe.W * 255.0f + 0.5f);

        var b0 = (int)(packed & 0xFF);
        var b1 = (int)((packed >> 8) & 0xFF);
        var b2 = (int)((packed >> 16) & 0xFF);
        var b3 = (int)((packed >> 24) & 0xFF);
        Span<int> packedBytes = stackalloc int[4] { b0, b1, b2, b3 };

        var usedMask = 0;
        s_rShift = SelectBestShift(packedBytes, expectedR, ref usedMask);
        s_gShift = SelectBestShift(packedBytes, expectedG, ref usedMask);
        s_bShift = SelectBestShift(packedBytes, expectedB, ref usedMask);
        s_aShift = SelectBestShift(packedBytes, expectedA, ref usedMask);
        s_initialized = true;
    }

    internal static SDLFColor ToSdlFColor(uint packedColor)
    {
        EnsureInitialized();

        const float inv255 = 1f / 255f;
        var r = ((packedColor >> s_rShift) & 0xFF) * inv255;
        var g = ((packedColor >> s_gShift) & 0xFF) * inv255;
        var b = ((packedColor >> s_bShift) & 0xFF) * inv255;
        var a = ((packedColor >> s_aShift) & 0xFF) * inv255;
        return new SDLFColor(r, g, b, a);
    }

    private static int SelectBestShift(ReadOnlySpan<int> packedBytes, int expected, ref int usedMask)
    {
        var bestIndex = 0;
        var bestError = int.MaxValue;
        for (var i = 0; i < 4; i++)
        {
            if ((usedMask & (1 << i)) != 0)
            {
                continue;
            }

            var error = Math.Abs(packedBytes[i] - expected);
            if (error < bestError)
            {
                bestError = error;
                bestIndex = i;
            }
        }

        usedMask |= 1 << bestIndex;
        return bestIndex * 8;
    }
}
