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

    /// <summary>
    /// Retrieves the requested error as exception value from current authoritative state.
    /// </summary>
    /// <returns>
    /// The validated exception? that represents the completed operation.
    /// </returns>
public static Exception? GetErrorAsException()
    {
        byte* ex = GetError();

        if (ex == null || ex[0] == '\0')
        {
            return null;
        }

        return new Exception(Utils.DecodeStringUTF8(ex));
    }

    /// <summary>
    /// Retrieves the requested library name value from current authoritative state.
    /// </summary>
    /// <returns>
    /// The validated text representation owned by the caller.
    /// </returns>
public static string GetLibraryName()
    {
        return "SDL3";
    }
}
