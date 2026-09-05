#nullable disable

namespace Inno.Native.MiniAudio
{
    using BGCS.Runtime;
    using Inno.Native.LibraryLoading;
    using System.Diagnostics;

    /// <summary>
    /// Configures native miniaudio binding initialization before the generated API is first used.
    /// </summary>
    public static class MiniAudioConfig
    {
        /// <summary>
        /// Selects process-module symbol resolution for statically linked AOT applications.
        /// </summary>
        public static bool AotStaticLink;
    }

    /// <summary>
    /// Provides the generated miniaudio C ABI surface used exclusively by the audio backend adapter.
    /// </summary>
    public static unsafe partial class MiniAudio
    {
#if DEBUG
        private const string DLL_NAME = "miniaudio-debug";
#else
        private const string DLL_NAME = "miniaudio-release";
#endif

        static MiniAudio()
        {
            if (MiniAudioConfig.AotStaticLink)
            {
                InitApi(new NativeLibraryContext(Process.GetCurrentProcess().MainModule!.BaseAddress));
                return;
            }

            NativeDllLoader.EnsureNativeDll(DLL_NAME);
            nint handle = NativeDllLoader.LoadNativeDll(DLL_NAME);
            InitApi(new NativeLibraryContext(handle));
        }

        /// <summary>
        /// Gets the platform-independent native library stem used by miniaudio.
        /// </summary>
        /// <returns>
        /// The constant library stem <c>miniaudio</c>.
        /// </returns>
        public static string GetLibraryName()
        {
            return "miniaudio";
        }
    }
}
