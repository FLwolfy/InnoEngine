#nullable disable

using System.Runtime.CompilerServices;

[assembly: DisableRuntimeMarshalling]

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

    }
}
