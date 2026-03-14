/*
 * Copyright 2011-2026 Branimir Karadzic. All rights reserved.
 * License: https://github.com/bkaradzic/bgfx/blob/master/LICENSE
 */

/*
 *
 * Copy from auto-generated binding with namespace changed
 *
 */

using Inno.Native.Dll;

namespace Inno.Native.Bgfx;

public static partial class bgfx
{
#if DEBUG
    const string DllName = "bgfx-shared-lib-debug";
#else
    const string DllName = "bgfx-shared-lib-release";
#endif

    static bgfx()
    {
        NativeDllLoader.RegisterResolver(typeof(bgfx).Assembly);
    }
}
