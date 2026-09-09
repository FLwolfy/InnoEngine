using System;
using System.Diagnostics.CodeAnalysis;
using Inno.Adapter;
using Inno.Adapter.Authoring.Default;
using Inno.Adapter.Presentation;
using Inno.Rendering;

namespace Inno.Editor.Application;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (!TryGetRunOptions(
                args,
                out string? projectDirectory,
                out int? smokeFrameLimit,
                out GraphicsApi? graphicsApi))
        {
            Console.Error.WriteLine(
                "Usage: Inno.Editor.Application <project-directory> [--graphics-api <d3d11|d3d12|metal|vulkan|opengl>] [--smoke-frames <positive-count>]");
            return 2;
        }
        try
        {
            var adapterCatalog = new DefaultAuthoringAdapterCatalog();
            using EditorHost host = EditorHost.Create(
                adapterCatalog,
                AdapterSelection.defaultValue,
                PresentationBackend.ImGui,
                projectDirectory,
                graphicsApi);
            int exitCode = host.Run(smokeFrameLimit);
            return exitCode;
        }
        catch (Exception ex)
        {
            string msg = $"[{DateTime.Now:O}] Unhandled exception:{Environment.NewLine}{ex}{Environment.NewLine}";
            Console.Error.WriteLine(msg);
            return 1;
        }
    }

    internal static bool TryGetProjectDirectory(
        string[] args,
        [NotNullWhen(true)] out string? projectDirectory)
    {
        ArgumentNullException.ThrowIfNull(args);
        projectDirectory = args.Length == 1 ? args[0] : null;
        return projectDirectory is not null;
    }

    private static bool TryGetRunOptions(
        string[] args,
        [NotNullWhen(true)] out string? projectDirectory,
        out int? smokeFrameLimit,
        out GraphicsApi? graphicsApi)
    {
        ArgumentNullException.ThrowIfNull(args);
        smokeFrameLimit = null;
        graphicsApi = null;
        projectDirectory = args.Length > 0 ? args[0] : null;
        if (projectDirectory is null)
            return false;
        for (int index = 1; index < args.Length; index += 2)
        {
            if (index + 1 >= args.Length)
                return false;
            if (string.Equals(args[index], "--smoke-frames", StringComparison.Ordinal))
            {
                if (smokeFrameLimit is not null
                    || !int.TryParse(args[index + 1], out int parsedFrameLimit)
                    || parsedFrameLimit <= 0)
                {
                    return false;
                }
                smokeFrameLimit = parsedFrameLimit;
                continue;
            }
            if (string.Equals(args[index], "--graphics-api", StringComparison.Ordinal))
            {
                if (graphicsApi is not null || !TryParseGraphicsApi(args[index + 1], out GraphicsApi parsedApi))
                    return false;
                graphicsApi = parsedApi;
                continue;
            }
            return false;
        }
        return true;
    }

    private static bool TryParseGraphicsApi(string value, out GraphicsApi api)
    {
        api = value.ToLowerInvariant() switch
        {
            "d3d11" => GraphicsApi.Direct3D11,
            "d3d12" => GraphicsApi.Direct3D12,
            "metal" => GraphicsApi.Metal,
            "vulkan" => GraphicsApi.Vulkan,
            "opengl" => GraphicsApi.OpenGL,
            _ => GraphicsApi.Noop
        };
        return api != GraphicsApi.Noop;
    }
}
