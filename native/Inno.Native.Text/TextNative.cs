#nullable disable

namespace Inno.Native.Text
{
    using BGCS.Runtime;
    using Inno.Native.LibraryLoading;
    using System.Diagnostics;

    /// <summary>
    /// Configures native text binding initialization before the generated API is first used.
    /// </summary>
    public static class TextNativeConfig
    {
        /// <summary>
        /// Selects process-module symbol resolution for statically linked AOT applications.
        /// </summary>
        public static bool AotStaticLink;
    }

    /// <summary>
    /// Provides the generated Inno text C ABI used exclusively by the FreeType and HarfBuzz adapter.
    /// </summary>
    public static unsafe partial class TextNative
    {
#if DEBUG
        private const string DLL_NAME = "inno-text-debug";
#else
        private const string DLL_NAME = "inno-text-release";
#endif

        static TextNative()
        {
            if (TextNativeConfig.AotStaticLink)
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
