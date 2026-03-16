namespace Inno.Native.ImGuizmo
{
    using HexaGen.Runtime;
    using Inno.Native.Dll;
    using System.Diagnostics;

    public static class ImGuizmoConfig
    {
        public static bool AotStaticLink;
    }

    public static unsafe partial class ImGuizmo
    {
#if DEBUG
        private const string DLL_NAME = "libcimguizmo-debug";
        private const string CIMGUI_DLL_NAME = "libcimgui-debug";
#else
        private const string DLL_NAME = "libcimguizmo-release";
        private const string CIMGUI_DLL_NAME = "libcimgui-release";
#endif

        static ImGuizmo()
        {
            if (ImGuizmoConfig.AotStaticLink)
            {
                InitApi(new NativeLibraryContext(Process.GetCurrentProcess().MainModule!.BaseAddress));
                return;
            }

            NativeDllLoader.EnsureNativeDll(CIMGUI_DLL_NAME);
            NativeDllLoader.LoadNativeDll(CIMGUI_DLL_NAME);
            NativeDllLoader.EnsureNativeDll(DLL_NAME);
            var handle = NativeDllLoader.LoadNativeDll(DLL_NAME);
            InitApi(new NativeLibraryContext(handle));
        }

        public static string GetLibraryName()
        {
            return "cimguizmo";
        }
    }
}
