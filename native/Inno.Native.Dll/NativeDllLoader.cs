using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;

namespace Inno.Native.Dll;

/// <summary>
/// Copies native binaries into output and loads them from the native folder.
/// </summary>
public static class NativeDllLoader
{
    private static readonly Lock RESOLVER_LOCK = new();
    private static readonly HashSet<Assembly> REGISTERED_RESOLVERS = new();

    /// <summary>
    /// Loads a native library from the output native folder, registering a resolver for the calling assembly.
    /// </summary>
    /// <param name="libraryName">Library name without platform extension.</param>
    /// <returns>Handle to the loaded library.</returns>
    public static IntPtr LoadNativeDll(string libraryName)
    {
        var targetAssembly = Assembly.GetCallingAssembly();
        RegisterResolverOnce(targetAssembly);
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

        throw new DllNotFoundException($"Native library not found under native output: {libraryName}");
    }

    /// <summary>
    /// Finds a file under the output native folder.
    /// </summary>
    /// <param name="fileName">Exact file name to locate.</param>
    /// <returns>Full path to the file.</returns>
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

    /// <summary>
    /// Copies a native library from repo lib into the output native folder.
    /// </summary>
    /// <param name="libraryName">Library name without platform extension.</param>
    /// <returns>Full path to the copied file.</returns>
    public static string EnsureNativeDll(string libraryName)
    {
        var candidateNames = GetLibraryFileNames(libraryName);
        foreach (var name in candidateNames)
        {
            var copied = EnsureNativeFile(name, throwIfMissing: false);
            if (!string.IsNullOrEmpty(copied))
            {
                return copied;
            }
        }

        throw new FileNotFoundException($"Native library not found in repo lib: {libraryName}");
    }

    /// <summary>
    /// Copies a file from repo lib into the output native folder.
    /// </summary>
    /// <param name="fileName">Exact file name to copy.</param>
    /// <returns>Full path to the copied file.</returns>
    public static string EnsureNativeFile(string fileName)
    {
        var copied = EnsureNativeFile(fileName, throwIfMissing: true);
        return copied;
    }

    private static string EnsureNativeFile(string fileName, bool throwIfMissing)
    {
        var repoRoot = FindRepoRoot(AppContext.BaseDirectory);
        if (repoRoot == null)
        {
            if (throwIfMissing)
            {
                throw new DirectoryNotFoundException("Repo root not found. Cannot resolve lib path.");
            }

            return string.Empty;
        }

        var libRoot = Path.Combine(repoRoot, NativeDllConstants.LIB_DIR_NAME);
        if (!Directory.Exists(libRoot))
        {
            if (throwIfMissing)
            {
                throw new DirectoryNotFoundException($"Lib directory not found: {libRoot}");
            }

            return string.Empty;
        }

        var nativeRoot = Path.Combine(AppContext.BaseDirectory, NativeDllConstants.NATIVE_DIR_NAME);
        Directory.CreateDirectory(nativeRoot);

        var srcFile = Directory.EnumerateFiles(libRoot, fileName, SearchOption.AllDirectories).FirstOrDefault();
        if (srcFile == null)
        {
            if (throwIfMissing)
            {
                throw new FileNotFoundException($"Native file not found in repo lib: {fileName}");
            }

            return string.Empty;
        }

        var relative = Path.GetRelativePath(libRoot, srcFile);
        var dest = Path.Combine(nativeRoot, relative);
        var destDir = Path.GetDirectoryName(dest);
        if (!string.IsNullOrEmpty(destDir))
        {
            Directory.CreateDirectory(destDir);
        }

        File.Copy(srcFile, dest, overwrite: true);
        return dest;
    }

    private static void RegisterResolverOnce(Assembly targetAssembly)
    {
        lock (RESOLVER_LOCK)
        {
            if (!REGISTERED_RESOLVERS.Add(targetAssembly))
            {
                return;
            }

            NativeLibrary.SetDllImportResolver(targetAssembly, ResolveNativeLibrary);
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
            if (!libraryName.StartsWith("lib", StringComparison.OrdinalIgnoreCase))
            {
                names.Add($"lib{libraryName}.dll");
            }
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
        var nativeRoot = Path.Combine(baseDir, NativeDllConstants.NATIVE_DIR_NAME);
        if (Directory.Exists(nativeRoot))
        {
            yield return nativeRoot;
        }
    }

    private static string? FindRepoRoot(string startDir)
    {
        var dir = new DirectoryInfo(startDir);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, NativeDllConstants.REPO_ROOT_MARKER_FILE)))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        return null;
    }
}
