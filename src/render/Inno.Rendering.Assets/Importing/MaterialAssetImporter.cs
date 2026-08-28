using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Inno.Assets.Loader;
using Inno.Core.Mathematics;

namespace Inno.Rendering.Assets;

[AssetImporterExtension]
internal sealed class MaterialAssetImporter : AssetImporter<MaterialAsset>
{
    public override string importerId => "inno.rendering.material";

    public override IReadOnlyList<string> supportedExtensions { get; } = [".imaterial"];

    protected override async ValueTask ImportAsync(
        AssetImportContext context,
        AssetImportWriter<MaterialAsset> output,
        CancellationToken cancellationToken)
    {
        using JsonDocument document = RenderingJson.Parse(context.ReadUtf8Text());
        JsonElement root = RenderingJson.RequireObject(document.RootElement, "$");
        RenderingJson.RejectUnknown(root, new HashSet<string>(
            ["shader", "properties", "keywords", "renderQueue"],
            StringComparer.Ordinal), "$");
        string shaderPath = RenderingJson.RequireString(root, "shader", "$.shader");
        ShaderAsset shader = context.ResolveDependency<ShaderAsset>(shaderPath);
        ShaderDefinition definition = shader.definition
            ?? throw new RenderingAssetFormatException(
                "$.shader",
                $"Shader '{shaderPath}' has no committed definition.");
        var material = new MaterialAsset { shader = shader };
        ParseProperties(context, root, definition, material);
        ParseKeywords(root, definition, material);
        if (root.TryGetProperty("renderQueue", out JsonElement renderQueue))
        {
            if (renderQueue.ValueKind == JsonValueKind.Null)
            {
                material.renderQueue = null;
            }
            else if (renderQueue.TryGetInt32(out int queue))
            {
                material.renderQueue = queue;
            }
            else
            {
                throw new RenderingAssetFormatException("$.renderQueue", "An integer or null is required.");
            }
        }

        output.SetAsset(material);
        await output.WriteArtifactAsync(
            "runtime",
            Encoding.UTF8.GetBytes(document.RootElement.GetRawText()),
            cancellationToken).ConfigureAwait(false);
    }

    private static void ParseProperties(
        AssetImportContext context,
        JsonElement root,
        ShaderDefinition shader,
        MaterialAsset material)
    {
        if (!root.TryGetProperty("properties", out JsonElement propertyObject))
        {
            return;
        }

        RenderingJson.RequireObject(propertyObject, "$.properties");
        Dictionary<string, ShaderPropertyDefinition> definitions = shader.properties
            .ToDictionary(static value => value.id.value, StringComparer.Ordinal);
        foreach (JsonProperty property in propertyObject.EnumerateObject())
        {
            if (!definitions.TryGetValue(property.Name, out ShaderPropertyDefinition? definition))
            {
                throw new RenderingAssetFormatException(
                    $"$.properties.{property.Name}",
                    "The shader does not declare this property ID.");
            }

            string path = $"$.properties.{property.Name}";
            MaterialValue value = definition.type switch
            {
                ShaderPropertyType.Float => MaterialValue.FromFloat(RequireNumber(property.Value, path)),
                ShaderPropertyType.Vector2 => MaterialValue.FromVector(RequireVector(property.Value, 2, path)),
                ShaderPropertyType.Vector3 => MaterialValue.FromVector(RequireVector(property.Value, 3, path)),
                ShaderPropertyType.Vector4 => MaterialValue.FromVector(RequireVector(property.Value, 4, path)),
                ShaderPropertyType.Color => FromColor(property.Value, path),
                ShaderPropertyType.Matrix4x4 => MaterialValue.FromMatrix(RequireMatrix(property.Value, path)),
                ShaderPropertyType.Texture2D
                    or ShaderPropertyType.Texture2DArray
                    or ShaderPropertyType.TextureCube => MaterialValue.FromTexture(
                    context.ResolveDependency<TextureAsset>(RenderingJson.RequireString(property.Value, path))),
                ShaderPropertyType.Sampler or ShaderPropertyType.Buffer => throw new RenderingAssetFormatException(
                    path,
                    $"Material source cannot persist '{definition.type}' runtime bindings."),
                _ => throw new RenderingAssetFormatException(path, $"Unsupported property type '{definition.type}'.")
            };
            material.Set(definition.id, value);
        }
    }

    private static void ParseKeywords(
        JsonElement root,
        ShaderDefinition shader,
        MaterialAsset material)
    {
        if (!root.TryGetProperty("keywords", out JsonElement keywordArray))
        {
            return;
        }

        RenderingJson.RequireKind(keywordArray, JsonValueKind.Array, "$.keywords");
        Dictionary<string, string> optionOwners = shader.keywords
            .SelectMany(keyword => keyword.options.Select(option => (option, keyword.id)))
            .ToDictionary(static value => value.option, static value => value.id, StringComparer.Ordinal);
        var selectedOwners = new HashSet<string>(StringComparer.Ordinal);
        int index = 0;
        foreach (JsonElement element in keywordArray.EnumerateArray())
        {
            string option = RenderingJson.RequireString(element, $"$.keywords[{index}]");
            if (!optionOwners.TryGetValue(option, out string? owner))
            {
                throw new RenderingAssetFormatException(
                    $"$.keywords[{index}]",
                    $"Shader does not declare keyword option '{option}'.");
            }

            if (!selectedOwners.Add(owner))
            {
                throw new RenderingAssetFormatException(
                    $"$.keywords[{index}]",
                    $"More than one option was selected for keyword '{owner}'.");
            }

            material.SetKeyword(option, enabled: true);
            index++;
        }
    }

    private static float RequireNumber(JsonElement element, string path)
        => element.TryGetSingle(out float result) && float.IsFinite(result)
            ? result
            : throw new RenderingAssetFormatException(path, "A finite floating-point number is required.");

    private static Vector4 RequireVector(JsonElement element, int count, string path)
    {
        RenderingJson.RequireKind(element, JsonValueKind.Array, path);
        float[] values = element.EnumerateArray()
            .Select((value, index) => RequireNumber(value, $"{path}[{index}]"))
            .ToArray();
        if (values.Length != count)
        {
            throw new RenderingAssetFormatException(path, $"Exactly {count} components are required.");
        }

        return new Vector4(
            values[0],
            count > 1 ? values[1] : 0f,
            count > 2 ? values[2] : 0f,
            count > 3 ? values[3] : 0f);
    }

    private static MaterialValue FromColor(JsonElement element, string path)
    {
        Vector4 vector = RequireVector(element, 4, path);
        return MaterialValue.FromColor(new Color(vector.x, vector.y, vector.z, vector.w));
    }

    private static Matrix RequireMatrix(JsonElement element, string path)
    {
        RenderingJson.RequireKind(element, JsonValueKind.Array, path);
        float[] values = element.EnumerateArray()
            .Select((value, index) => RequireNumber(value, $"{path}[{index}]"))
            .ToArray();
        if (values.Length != 16)
        {
            throw new RenderingAssetFormatException(path, "Exactly 16 row-major components are required.");
        }

        return new Matrix(
            values[0], values[1], values[2], values[3],
            values[4], values[5], values[6], values[7],
            values[8], values[9], values[10], values[11],
            values[12], values[13], values[14], values[15]);
    }
}
