/*
 * Copyright 2011-2026 Branimir Karadzic. All rights reserved.
 * License: https://github.com/bkaradzic/bgfx/blob/master/LICENSE
 */

/*
 *
 * Copy from auto-generated binding with namespace changed
 *
 */

using Inno.Native.LibraryLoading;

namespace Inno.Native.Bgfx;

/// <summary>
/// bgfx native bindings loader.
/// </summary>
public static partial class bgfx
{
#if DEBUG
    const string DLL_NAME = "bgfx-shared-lib-debug";
#else
    const string DLL_NAME = "bgfx-shared-lib-release";
#endif
    
    static bgfx()
    {
        NativeDllLoader.EnsureNativeDll(DLL_NAME);
        NativeDllLoader.LoadNativeDll(DLL_NAME);
    }
}
