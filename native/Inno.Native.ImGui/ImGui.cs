#nullable disable

namespace Inno.Native.ImGui
{
    using HexaGen.Runtime;
    using Inno.Native.Dll;
    using System.Diagnostics;

    public static class ImGuiConfig
    {
        public static bool AotStaticLink;
    }

    public static partial class ImGui
    {
#if DEBUG
        private const string DLL_NAME = "libcimgui-debug";
#else
        private const string DLL_NAME = "libcimgui-release";
#endif

        static ImGui()
        {
            if (ImGuiConfig.AotStaticLink)
            {
                InitApi(new NativeLibraryContext(Process.GetCurrentProcess().MainModule!.BaseAddress));
                return;
            }

            NativeDllLoader.EnsureNativeDll(DLL_NAME);
            var handle = NativeDllLoader.LoadNativeDll(DLL_NAME);
            InitApi(new NativeLibraryContext(handle));
        }

        public static string GetLibraryName()
        {
            return "cimgui";
        }

        public const nint ImDrawCallbackResetRenderState = -8;
    }
}
