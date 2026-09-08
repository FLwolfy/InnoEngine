#nullable disable

namespace Inno.Native.ImGui
{
    using BGCS.Runtime;
    using Inno.Native.LibraryLoading;
    using System.Diagnostics;

    /// <summary>
    /// Provides the generated native Dear ImGui ABI surface used exclusively by the platform adapter.
    /// </summary>
public static class ImGuiConfig
    {
        /// <summary>
        /// The aot static link value used as part of this type's public representation.
        /// </summary>
public static bool AotStaticLink;
    }

    /// <summary>
    /// Provides the generated native Dear ImGui ABI surface used exclusively by the platform adapter.
    /// </summary>
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

        /// <summary>
        /// Retrieves the requested library name value from current authoritative state.
        /// </summary>
        /// <returns>
        /// The validated text representation owned by the caller.
        /// </returns>
public static string GetLibraryName()
        {
            return "cimgui";
        }

        /// <summary>
        /// The im draw callback reset render state value used as part of this type's public representation.
        /// </summary>
public const nint ImDrawCallbackResetRenderState = -8;
    }
}
