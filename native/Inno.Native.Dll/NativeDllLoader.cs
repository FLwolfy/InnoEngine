using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Inno.Native.Dll;

public static class NativeDllLoader
{
    private const string REPO_ROOT_MARKER_FILE = "InnoEngine.sln";
    private const string NATIVE_DIR_NAME = "native";
    private const string LIB_DIR_NAME = "lib";

    [ModuleInitializer]
    internal static void Initialize()
    {
        EnsureNativeOutputFromRepoLib();
    }

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
        foreach (var root in GetSearchRoots())
        {
            var match = Directory.EnumerateFiles(root, fileName, SearchOption.AllDirectories).FirstOrDefault();
            if (match != null)
            {
                return match;
            }
        }

        throw new FileNotFoundException($"Native file not found under search roots: {fileName}");
    }

    public static void EnsureNativeOutputFromRepoLib(params string[] fileNames)
    {
        var repoRoot = FindRepoRoot(AppContext.BaseDirectory);
        if (repoRoot == null)
        {
            return;
        }

        var libRoot = Path.Combine(repoRoot, LIB_DIR_NAME);
        if (!Directory.Exists(libRoot))
        {
            return;
        }

        var nativeRoot = Path.Combine(AppContext.BaseDirectory, NATIVE_DIR_NAME);
        Directory.CreateDirectory(nativeRoot);

        var files = fileNames.Length == 0
            ? Directory.EnumerateFiles(libRoot, "*", SearchOption.AllDirectories)
            : ResolveFilesFromLibRoot(libRoot, fileNames);

        foreach (var src in files)
        {
            var relative = Path.GetRelativePath(libRoot, src);
            var dest = Path.Combine(nativeRoot, relative);
            var destDir = Path.GetDirectoryName(dest);
            if (!string.IsNullOrEmpty(destDir))
            {
                Directory.CreateDirectory(destDir);
            }

            File.Copy(src, dest, overwrite: true);
        }
    }

    private static IntPtr ResolveNativeLibrary(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        var candidateNames = GetLibraryFileNames(libraryName);
        foreach (var root in GetSearchRoots())
        {
            foreach (var name in candidateNames)
            {
                var match = Directory.EnumerateFiles(root, name, SearchOption.AllDirectories).FirstOrDefault();
                if (match != null)
                {
                    return NativeLibrary.Load(match);
                }
            }
        }

        return IntPtr.Zero;
    }

    private static IReadOnlyList<string> GetLibraryFileNames(string libraryName)
    {
        var names = new List<string>();
        if (libraryName.Contains('.'))
        {
            names.Add(libraryName);
        }

        if (OperatingSystem.IsWindows())
        {
            names.Add($"{libraryName}.dll");
            return names;
        }

        if (OperatingSystem.IsMacOS())
        {
            names.Add($"lib{libraryName}.dylib");
            names.Add($"{libraryName}.dylib");
            return names;
        }

        names.Add($"lib{libraryName}.so");
        names.Add($"{libraryName}.so");
        return names;
    }

    private static IEnumerable<string> GetSearchRoots()
    {
        var baseDir = AppContext.BaseDirectory;
        var nativeRoot = Path.Combine(baseDir, NATIVE_DIR_NAME);
        if (Directory.Exists(nativeRoot))
        {
            yield return nativeRoot;
        }
    }

    private static IEnumerable<string> ResolveFilesFromLibRoot(string libRoot, IEnumerable<string> fileNames)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in fileNames)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            set.Add(name);
        }

        if (set.Count == 0)
        {
            return Array.Empty<string>();
        }

        var matches = new List<string>();
        foreach (var name in set)
        {
            var match = Directory.EnumerateFiles(libRoot, name, SearchOption.AllDirectories).FirstOrDefault();
            if (match != null)
            {
                matches.Add(match);
            }
        }

        return matches;
    }

    private static string? FindRepoRoot(string startDir)
    {
        var dir = new DirectoryInfo(startDir);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, REPO_ROOT_MARKER_FILE)))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        return null;
    }
}
