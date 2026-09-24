using Inno.Native.LibraryLoading;

namespace Inno.Native.Bgfx;

/// <summary>
/// bgfx native bindings loader.
/// </summary>
public static partial class bgfx
{
#if DEBUG
    internal const string LibName = "bgfx-shared-lib-debug";
#else
    internal const string LibName = "bgfx-shared-lib-release";
#endif
    private const string DLL_NAME = LibName;
    
    static bgfx()
    {
        NativeDllLoader.EnsureNativeDll(LibName);
        NativeDllLoader.LoadNativeDll(LibName);
    }
}
