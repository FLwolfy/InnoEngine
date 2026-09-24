#nullable disable

namespace Inno.Native.UI
{
    using BGCS.Runtime;
    using Inno.Native.LibraryLoading;
    using System.Diagnostics;

    /// <summary>
    /// Configures native UI binding initialization before the generated API is first used.
    /// </summary>
    public static class UiNativeConfig
    {
        /// <summary>
        /// Selects process-module symbol resolution for statically linked AOT applications.
        /// </summary>
        public static bool AotStaticLink;
    }

    /// <summary>
    /// Provides the generated Inno UI C ABI used exclusively by the RmlUi adapter.
    /// </summary>
    public static unsafe partial class UiNative
    {
#if DEBUG
        private const string DLL_NAME = "inno-ui-debug";
#else
        private const string DLL_NAME = "inno-ui-release";
#endif

        static UiNative()
        {
            if (UiNativeConfig.AotStaticLink)
            {
                InitApi(new NativeLibraryContext(Process.GetCurrentProcess().MainModule!.BaseAddress));
                return;
            }
            NativeDllLoader.EnsureNativeDll(DLL_NAME);
            nint handle = NativeDllLoader.LoadNativeDll(DLL_NAME);
            InitApi(new NativeLibraryContext(handle));
        }

    }
}
