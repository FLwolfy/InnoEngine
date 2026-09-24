using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;

namespace Inno.Native.LibraryLoading;

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
    /// <param name="libraryName">
    /// Library name without platform extension.
    /// </param>
    /// <returns>
    /// Handle to the loaded library.
    /// </returns>
    public static IntPtr LoadNativeDll(string libraryName)
    {
        var targetAssembly = Assembly.GetCallingAssembly();
        RegisterResolverOnce(targetAssembly);
        var candidateNames = GetLibraryFileNames(libraryName);
        foreach (var root in GetSearchRoots())
        {
            foreach (var name in candidateNames)
            {
                var match = FindPreferredTargetFile(root, name);
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
    /// <param name="fileName">
    /// Exact file name to locate.
    /// </param>
    /// <returns>
    /// Full path to the file.
    /// </returns>
    public static string FindNativeFile(string fileName)
    {
        foreach (var root in GetSearchRoots())
        {
            var match = FindPreferredTargetFile(root, fileName);
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
    /// <param name="libraryName">
    /// Library name without platform extension.
    /// </param>
    /// <returns>
    /// Full path to the copied file.
    /// </returns>
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
    /// <param name="fileName">
    /// Exact file name to copy.
    /// </param>
    /// <returns>
    /// Full path to the copied file.
    /// </returns>
    public static string EnsureNativeFile(string fileName)
    {
        var copied = EnsureNativeFile(fileName, throwIfMissing: true);
        return copied;
    }

    /// <summary>
    /// Deploys a known native file into a relative path under the current output's native directory.
    /// </summary>
    /// <param name="sourcePath">Existing source file to deploy.</param>
    /// <param name="relativeOutputPath">Relative path below the native output directory.</param>
    /// <returns>The absolute deployed file path.</returns>
    public static string DeployNativeFile(string sourcePath, string relativeOutputPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(relativeOutputPath);
        if (Path.IsPathRooted(relativeOutputPath))
        {
            throw new ArgumentException("Native output path must be relative.", nameof(relativeOutputPath));
        }

        string fullSourcePath = Path.GetFullPath(sourcePath);
        if (!File.Exists(fullSourcePath))
        {
            throw new FileNotFoundException("Native source file was not found.", fullSourcePath);
        }

        string nativeRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, NativeDllConstants.NATIVE_DIR_NAME));
        string destinationPath = Path.GetFullPath(Path.Combine(nativeRoot, relativeOutputPath));
        string nativeRootPrefix = Path.TrimEndingDirectorySeparator(nativeRoot) + Path.DirectorySeparatorChar;
        StringComparison pathComparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (!destinationPath.StartsWith(nativeRootPrefix, pathComparison))
        {
            throw new ArgumentException("Native output path escapes the native directory.", nameof(relativeOutputPath));
        }

        string destinationDirectory = Path.GetDirectoryName(destinationPath)
            ?? throw new InvalidOperationException("Native output path does not have a parent directory.");
        Directory.CreateDirectory(destinationDirectory);
        if (!File.Exists(destinationPath) || !FileContentsMatch(fullSourcePath, destinationPath))
        {
            File.Copy(fullSourcePath, destinationPath, overwrite: true);
        }

        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(destinationPath, File.GetUnixFileMode(fullSourcePath));
        }

        File.SetLastWriteTimeUtc(destinationPath, File.GetLastWriteTimeUtc(fullSourcePath));
        return destinationPath;
    }

    private static string EnsureNativeFile(string fileName, bool throwIfMissing)
    {
        string? deployed = FindNativeOutputFile(fileName);
        var repoRoot = FindRepoRoot(AppContext.BaseDirectory);
        if (repoRoot == null)
        {
            if (deployed is not null)
                return deployed;

            if (throwIfMissing)
            {
                throw new DirectoryNotFoundException("Repo root not found. Cannot resolve lib path.");
            }

            return string.Empty;
        }

        var libRoot = Path.Combine(repoRoot, NativeDllConstants.LIB_DIR_NAME);
        if (!Directory.Exists(libRoot))
        {
            if (deployed is not null)
                return deployed;

            if (throwIfMissing)
            {
                throw new DirectoryNotFoundException($"Lib directory not found: {libRoot}");
            }

            return string.Empty;
        }

        var nativeRoot = Path.Combine(AppContext.BaseDirectory, NativeDllConstants.NATIVE_DIR_NAME);
        Directory.CreateDirectory(nativeRoot);

        var srcFile = FindPreferredTargetFile(libRoot, fileName);
        if (srcFile == null)
        {
            if (deployed is not null)
                return deployed;

            if (throwIfMissing)
            {
                throw new FileNotFoundException($"Native file not found in repo lib: {fileName}");
            }

            return string.Empty;
        }

        var relative = Path.GetRelativePath(libRoot, srcFile);
        var dest = deployed ?? Path.Combine(nativeRoot, relative);
        var destDir = Path.GetDirectoryName(dest);
        if (!string.IsNullOrEmpty(destDir))
        {
            Directory.CreateDirectory(destDir);
        }

        var sourceInfo = new FileInfo(srcFile);
        if (!File.Exists(dest) || !FileContentsMatch(srcFile, dest))
        {
            File.Copy(srcFile, dest, overwrite: true);
            File.SetLastWriteTimeUtc(dest, sourceInfo.LastWriteTimeUtc);
        }

        return dest;
    }

    private static bool FileContentsMatch(string firstPath, string secondPath)
    {
        var firstInfo = new FileInfo(firstPath);
        var secondInfo = new FileInfo(secondPath);
        if (firstInfo.Length != secondInfo.Length)
            return false;

        using FileStream first = File.OpenRead(firstPath);
        using FileStream second = File.OpenRead(secondPath);
        byte[] firstHash = SHA256.HashData(first);
        byte[] secondHash = SHA256.HashData(second);
        return firstHash.AsSpan().SequenceEqual(secondHash);
    }

    private static string? FindNativeOutputFile(string fileName)
    {
        foreach (string root in GetSearchRoots())
        {
            string? match = FindPreferredTargetFile(root, fileName);
            if (match is not null)
                return match;
        }
        return null;
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
                var match = FindPreferredTargetFile(root, name);
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

    private static string? FindPreferredTargetFile(string root, string fileName)
    {
        var candidates = Directory
            .EnumerateFiles(root, fileName, SearchOption.AllDirectories)
            .ToArray();
        if (candidates.Length == 0)
        {
            return null;
        }

        string targetSegment = $"/{GetTargetPlatformIdentifier()}/";
        string? targetMatch = candidates.FirstOrDefault(path =>
            NormalizePath(path).Contains(targetSegment, StringComparison.OrdinalIgnoreCase));
        if (targetMatch is not null)
        {
            return targetMatch;
        }

        return candidates.FirstOrDefault(path => !HasPlatformScope(NormalizePath(path)));
    }

    private static string GetTargetPlatformIdentifier()
    {
        string operatingSystem = OperatingSystem.IsMacOS()
            ? "osx"
            : OperatingSystem.IsWindows()
                ? "windows"
                : OperatingSystem.IsLinux()
                    ? "linux"
                    : throw new PlatformNotSupportedException("Native library loading supports Windows, macOS, and Linux.");
        string architecture = RuntimeInformation.OSArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.Arm64 => "arm64",
            Architecture.X86 => "x86",
            Architecture.Arm => "arm",
            _ => throw new PlatformNotSupportedException(
                $"Unsupported native architecture: {RuntimeInformation.OSArchitecture}.")
        };
        return $"{operatingSystem}-{architecture}";
    }

    private static bool HasPlatformScope(string normalizedPath)
    {
        return normalizedPath.Contains("/windows-", StringComparison.OrdinalIgnoreCase)
            || normalizedPath.Contains("/osx-", StringComparison.OrdinalIgnoreCase)
            || normalizedPath.Contains("/linux-", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizePath(string path)
        => path.Replace('\\', '/');

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
