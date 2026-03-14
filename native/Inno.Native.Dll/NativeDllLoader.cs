using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Inno.Native.Dll;

public static class NativeDllLoader
{
    public static void RegisterResolver(Assembly? assembly = null)
    {
        var target = assembly ?? Assembly.GetCallingAssembly();
        NativeLibrary.SetDllImportResolver(target, ResolveNativeLibrary);
    }

    public static IntPtr Load(string libraryName)
    {
        return ResolveNativeLibrary(libraryName, Assembly.GetCallingAssembly(), null);
    }

    public static string FindNativeFile(string fileName)
    {
        var nativeRoot = GetNativeRoot();
        var match = Directory.EnumerateFiles(nativeRoot, fileName, SearchOption.AllDirectories).FirstOrDefault();
        if (match == null)
        {
            throw new FileNotFoundException($"Native file not found under {nativeRoot}: {fileName}");
        }

        return match;
    }

    private static IntPtr ResolveNativeLibrary(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        var nativeRoot = GetNativeRoot();

        var fileName = GetLibraryFileName(libraryName);
        var match = Directory.EnumerateFiles(nativeRoot, fileName, SearchOption.AllDirectories).FirstOrDefault();
        if (match == null)
        {
            return IntPtr.Zero;
        }

        return NativeLibrary.Load(match);
    }

    private static string GetLibraryFileName(string libraryName)
    {
        if (OperatingSystem.IsWindows())
        {
            return $"{libraryName}.dll";
        }

        if (OperatingSystem.IsMacOS())
        {
            return $"lib{libraryName}.dylib";
        }

        return $"lib{libraryName}.so";
    }

    private static string GetNativeRoot()
    {
        var nativeRoot = Path.Combine(AppContext.BaseDirectory, "native");
        if (!Directory.Exists(nativeRoot))
        {
            throw new DirectoryNotFoundException($"Native root not found: {nativeRoot}");
        }

        return nativeRoot;
    }
}
