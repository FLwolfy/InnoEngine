namespace Inno.Native.ImGuizmo
{
    using BGCS.Runtime;
    using Inno.Native.LibraryLoading;
    using System.Diagnostics;

    /// <summary>
    /// Provides the generated native ImGuizmo ABI surface used exclusively by the editor adapter.
    /// </summary>
public static class ImGuizmoConfig
    {
        /// <summary>
        /// The aot static link value used as part of this type's public representation.
        /// </summary>
public static bool AotStaticLink;
    }

    /// <summary>
    /// Provides the generated native ImGuizmo ABI surface used exclusively by the editor adapter.
    /// </summary>
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

        /// <summary>
        /// Retrieves the requested library name value from current authoritative state.
        /// </summary>
        /// <returns>
        /// The validated text representation owned by the caller.
        /// </returns>
public static string GetLibraryName()
        {
            return "cimguizmo";
        }
    }
}
