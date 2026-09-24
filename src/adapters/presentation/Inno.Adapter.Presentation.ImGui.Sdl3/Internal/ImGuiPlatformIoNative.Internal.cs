using System.Numerics;
using System.Runtime.InteropServices;

using Inno.Native.ImGui;
using ImGuiNative = Inno.Native.ImGui.ImGui;

namespace Inno.Adapter.Presentation.ImGui;

internal static unsafe class ImGuiPlatformIoNative
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal unsafe delegate void PlatformGetWindowPosCallback(ImGuiViewport* viewport, Vector2* outPos);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal unsafe delegate void PlatformGetWindowSizeCallback(ImGuiViewport* viewport, Vector2* outSize);

    internal static void SetPlatformGetWindowPos(ImGuiPlatformIOPtr platformIo, PlatformGetWindowPosCallback callback)
    {
        var callbackPtr = Marshal.GetFunctionPointerForDelegate(callback);
        var setterArg = (delegate* unmanaged[Cdecl]<ImGuiViewport*, Vector2*, void>)callbackPtr;
        ImGuiNative.PlatformIOSetPlatformGetWindowPos(platformIo, setterArg);
    }

    internal static void SetPlatformGetWindowSize(ImGuiPlatformIOPtr platformIo, PlatformGetWindowSizeCallback callback)
    {
        var callbackPtr = Marshal.GetFunctionPointerForDelegate(callback);
        var setterArg = (delegate* unmanaged[Cdecl]<ImGuiViewport*, Vector2*, void>)callbackPtr;
        ImGuiNative.PlatformIOSetPlatformGetWindowSize(platformIo, setterArg);
    }
}
