using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

using Inno.Core.Logging;
using Inno.Platform.Display;
using Inno.Platform.Display.Bridge;
using Inno.Platform.Graphics;
using Inno.Platform.Window;
using Inno.Platform.Window.Bridge;

namespace Inno.Platform;

/// <summary>
/// Enumeration of supported platform backends.
/// A platform backend defines how windowing, display, and graphics systems
/// are created and wired together for a specific runtime environment.
/// </summary>
public enum PlatformBackend
{
    Veldrid_Sdl2
}

/// <summary>
/// Static platform-level utilities and OS integration helpers.
/// </summary>
public static class PlatformAPI
{
    /// <summary>
    /// Creates and initializes a <see cref="PlatformAPI"/> instance.
    /// </summary>
    /// <param name="mainWindowInfo">
    /// Initial configuration for the primary application window.
    /// </param>
    /// <param name="platformBackend">
    /// Platform backend used to create window, display, and graphics subsystems.
    /// </param>
    /// <param name="graphicsBackend">
    /// Graphics API backend used by the rendering device (e.g. Vulkan, Metal).
    /// </param>
    /// <returns>
    /// A fully initialized <see cref="PlatformAPI"/> instance owning all
    /// platform-level subsystems.
    /// </returns>
    public static PlatformRuntime CreatePlatform(
        WindowInfo mainWindowInfo,
        PlatformBackend platformBackend,
        GraphicsBackend graphicsBackend)
    {
        return new PlatformRuntime(mainWindowInfo, platformBackend, graphicsBackend);
    }
    
    /// <summary>
    /// Reveals a file or directory in the host operating system's file explorer.
    /// </summary>
    /// <param name="nativePath">
    /// Absolute native path to a file or directory.
    /// If a file is provided, the file will be selected when supported
    /// by the operating system.
    /// </param>
    /// <remarks>
    /// <list type="bullet">
    /// <item><description>Windows: Uses <c>explorer.exe</c> with <c>/select</c> when possible.</description></item>
    /// <item><description>macOS: Uses <c>open -R</c> to reveal items in Finder.</description></item>
    /// <item><description>Linux/Unix: Uses <c>xdg-open</c> on the containing directory.</description></item>
    /// </list>
    /// Failures are logged but otherwise ignored.
    /// </remarks>
    public static void RevealInSystem(string nativePath)
    {
        if (string.IsNullOrWhiteSpace(nativePath))
            return;

        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                if (File.Exists(nativePath))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = $"/select,\"{nativePath}\"",
                        UseShellExecute = true
                    });
                }
                else
                {
                    string dir = Directory.Exists(nativePath)
                        ? nativePath
                        : (Path.GetDirectoryName(nativePath) ?? nativePath);

                    if (Directory.Exists(dir))
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = "explorer.exe",
                            Arguments = $"\"{dir}\"",
                            UseShellExecute = true
                        });
                    }
                }

                return;
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                if (File.Exists(nativePath) || Directory.Exists(nativePath))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "open",
                        Arguments = $"-R \"{nativePath}\"",
                        UseShellExecute = false
                    });
                }
                else
                {
                    string dir = Path.GetDirectoryName(nativePath) ?? nativePath;
                    if (Directory.Exists(dir))
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = "open",
                            Arguments = $"\"{dir}\"",
                            UseShellExecute = false
                        });
                    }
                }

                return;
            }

            string unixDir = Directory.Exists(nativePath)
                ? nativePath
                : (Path.GetDirectoryName(nativePath) ?? nativePath);

            if (!Directory.Exists(unixDir))
                return;

            Process.Start(new ProcessStartInfo
            {
                FileName = "xdg-open",
                Arguments = $"\"{unixDir}\"",
                UseShellExecute = false
            });
        }
        catch (Exception e)
        {
            Log.Error($"RevealInSystem failed: {e.Message}");
        }
    }
}

/// <summary>
/// Owns and manages all platform subsystems for a running application instance.
/// </summary>
public sealed class PlatformRuntime : IDisposable
{
    public readonly IWindowSystem windowSystem;
    public readonly IDisplaySystem displaySystem;
    public readonly IGraphicsDevice graphicsDevice;

    internal PlatformRuntime(
        WindowInfo mainWindowInfo,
        PlatformBackend platformBackend,
        GraphicsBackend graphicsBackend)
    {
        switch (platformBackend)
        {
            case PlatformBackend.Veldrid_Sdl2:
            {
                windowSystem = new VeldridSdl2WindowSystem(
                    mainWindowInfo,
                    graphicsBackend,
                    out var vgd);

                displaySystem = new VeldridSdl2DisplaySystem();
                graphicsDevice = vgd;
                return;
            }
        }

        throw new PlatformNotSupportedException($"Platform backend {platformBackend} is not supported.");
    }

    public void Dispose()
    {
        windowSystem.Dispose();
        displaySystem.Dispose();
        graphicsDevice.Dispose();
    }
}
