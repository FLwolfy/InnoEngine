using System;
using Inno.Adapter;
using Inno.Adapter.Default;
using Inno.Rendering;

namespace Inno.Player;

internal static class Program
{
    private static int Main(string[] arguments)
    {
        try
        {
            ParseOptions(arguments, out int? smokeFrameLimit, out GraphicsApi? graphicsApi);
            var adapterCatalog = new DefaultAdapterCatalog();
            using var host = GamePlayerHost.Create(
                adapterCatalog,
                AdapterSelection.defaultValue,
                graphicsApi);
            return host.Run(smokeFrameLimit);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static void ParseOptions(
        string[] arguments,
        out int? smokeFrameLimit,
        out GraphicsApi? graphicsApi)
    {
        smokeFrameLimit = null;
        graphicsApi = null;
        for (int index = 0; index < arguments.Length; index += 2)
        {
            if (index + 1 >= arguments.Length)
                throw Usage();
            if (string.Equals(arguments[index], "--smoke-frames", StringComparison.Ordinal))
            {
                if (smokeFrameLimit is not null
                    || !int.TryParse(arguments[index + 1], out int frameCount)
                    || frameCount <= 0)
                {
                    throw Usage();
                }
                smokeFrameLimit = frameCount;
                continue;
            }
            if (string.Equals(arguments[index], "--graphics-api", StringComparison.Ordinal))
            {
                if (graphicsApi is not null || !TryParseGraphicsApi(arguments[index + 1], out GraphicsApi api))
                    throw Usage();
                graphicsApi = api;
                continue;
            }
            throw Usage();
        }
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

    private static ArgumentException Usage()
        => new("Usage: Inno.Player [--graphics-api <d3d11|d3d12|metal|vulkan|opengl>] [--smoke-frames <positive-count>].");
}
