using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Inno.Rendering.Assets;

internal static class RenderingJson
{
    private static readonly JsonDocumentOptions S_OPTIONS = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip
    };

    internal static JsonDocument Parse(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        return JsonDocument.Parse(json, S_OPTIONS);
    }

    internal static JsonElement RequireObject(JsonElement element, string path)
    {
        RequireKind(element, JsonValueKind.Object, path);
        return element;
    }

    internal static string RequireString(JsonElement parent, string name, string path)
    {
        if (!parent.TryGetProperty(name, out JsonElement value))
        {
            throw new RenderingAssetFormatException(path, "A required string is missing.");
        }

        return RequireString(value, path);
    }

    internal static string RequireString(JsonElement element, string path)
    {
        if (element.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(element.GetString()))
        {
            throw new RenderingAssetFormatException(path, "A non-empty string is required.");
        }

        return element.GetString()!;
    }

    internal static void RequireKind(JsonElement element, JsonValueKind kind, string path)
    {
        if (element.ValueKind != kind)
        {
            throw new RenderingAssetFormatException(path, $"Expected {kind}, found {element.ValueKind}.");
        }
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

    internal static float RequireFiniteSingle(JsonElement element, string path)
        => element.TryGetSingle(out float result) && float.IsFinite(result)
            ? result
            : throw new RenderingAssetFormatException(path, "A finite floating-point number is required.");

    internal static TEnum ParseEnum<TEnum>(JsonElement element, string path)
        where TEnum : struct, Enum
    {
        string value = RequireString(element, path);
        return Enum.TryParse(value, ignoreCase: true, out TEnum result) && Enum.IsDefined(result)
            ? result
            : throw new RenderingAssetFormatException(
                path,
                $"'{value}' is not a supported {typeof(TEnum).Name} value.");
    }

    internal static void RejectUnknown(
        JsonElement element,
        IReadOnlySet<string> allowed,
        string path)
    {
        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (!allowed.Contains(property.Name))
            {
                throw new RenderingAssetFormatException(
                    $"{path}.{property.Name}",
                    "Unknown properties are not allowed.");
            }
        }
    }
}
