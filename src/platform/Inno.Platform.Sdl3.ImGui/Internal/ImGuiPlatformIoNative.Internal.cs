using System.Numerics;
using System.Runtime.InteropServices;

using Inno.Native.ImGui;
using ImGuiNative = Inno.Native.ImGui.ImGui;

namespace Inno.Platform.Sdl3.ImGui;

internal static unsafe class ImGuiPlatformIoNative
{
    internal unsafe delegate void PlatformGetWindowPosCallback(ImGuiViewport* viewport, Vector2* outPos);

    internal unsafe delegate void PlatformGetWindowSizeCallback(ImGuiViewport* viewport, Vector2* outSize);

    internal static void SetPlatformGetWindowPos(ImGuiPlatformIOPtr platformIo, PlatformGetWindowPosCallback callback)
    {
        var callbackPtr = Marshal.GetFunctionPointerForDelegate(callback);
        var setterArg = (delegate*<ImGuiPlatformIO*, delegate*<ImGuiViewport*, Vector2*, void>, void>)callbackPtr;
        ImGuiNative.PlatformIOSetPlatformGetWindowPos(platformIo, setterArg);
    }

    internal static void SetPlatformGetWindowSize(ImGuiPlatformIOPtr platformIo, PlatformGetWindowSizeCallback callback)
    {
        var callbackPtr = Marshal.GetFunctionPointerForDelegate(callback);
        var setterArg = (delegate*<ImGuiPlatformIO*, delegate*<ImGuiViewport*, Vector2*, void>, void>)callbackPtr;
        ImGuiNative.PlatformIOSetPlatformGetWindowSize(platformIo, setterArg);
    }
}
