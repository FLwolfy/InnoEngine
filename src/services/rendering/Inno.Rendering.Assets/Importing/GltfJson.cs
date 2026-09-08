using System;
using System.Text.Json;

namespace Inno.Rendering.Assets;

internal static class GltfJson
{
    internal static JsonElement RequireObject(JsonElement element, string path)
    {
        RequireKind(element, JsonValueKind.Object, path);
        return element;
    }

    internal static string RequireString(JsonElement element, string path)
    {
        if (element.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(element.GetString()))
            throw new RenderingAssetFormatException(path, "A non-empty string is required.");
        return element.GetString()!;
    }

    internal static void RequireKind(JsonElement element, JsonValueKind kind, string path)
    {
        if (element.ValueKind != kind)
            throw new RenderingAssetFormatException(path, $"Expected {kind}, found {element.ValueKind}.");
    }

    internal static bool RequireBoolean(JsonElement element, string path)
        => element.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => throw new RenderingAssetFormatException(path, "A boolean is required.")
        };

    internal static int RequireInt32(JsonElement element, string path)
        => element.TryGetInt32(out int result)
            ? result
            : throw new RenderingAssetFormatException(path, "A 32-bit integer is required.");
}
