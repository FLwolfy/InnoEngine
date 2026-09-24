using System;
using BGCS.Runtime;
using Inno.Native.LibraryLoading;

namespace Inno.Native.Sdl3;

/// <summary>
/// Provides the generated SDL3 ABI surface used exclusively by the SDL platform adapter.
/// </summary>
public static unsafe partial class SDL
{
#if DEBUG
    private const string DLL_NAME = "SDL3-debug";
#else
    private const string DLL_NAME = "SDL3-release";
#endif

    static SDL()
    {
        NativeDllLoader.EnsureNativeDll(DLL_NAME);
        var handle = NativeDllLoader.LoadNativeDll(DLL_NAME);
        InitApi(new NativeLibraryContext(handle));
    }

}
