using System;
using BGCS.Runtime;
using Inno.Native.Dll;

namespace Inno.Native.SDL3;

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

    public static Exception? GetErrorAsException()
    {
        byte* ex = GetError();

        if (ex == null || ex[0] == '\0')
        {
            return null;
        }

        return new Exception(Utils.DecodeStringUTF8(ex));
    }

    public static string GetLibraryName()
    {
        return "SDL3";
    }
}
