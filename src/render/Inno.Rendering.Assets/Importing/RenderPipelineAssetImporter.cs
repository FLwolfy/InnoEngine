using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Inno.Assets.Loader;

namespace Inno.Rendering.Assets;

[AssetImporterExtension]
internal sealed class RenderPipelineAssetImporter : AssetImporter<RenderPipelineAsset>
{
    public override string importerId => "inno.rendering.pipeline";

    public override IReadOnlyList<string> supportedExtensions { get; } = [".irenderpipeline"];

    protected override async ValueTask ImportAsync(
        AssetImportContext context,
        AssetImportWriter<RenderPipelineAsset> output,
        CancellationToken cancellationToken)
    {
        using JsonDocument document = RenderingJson.Parse(context.ReadUtf8Text());
        JsonElement root = RenderingJson.RequireObject(document.RootElement, "$");
        RenderingJson.RejectUnknown(root, new HashSet<string>(
            ["pipeline", "renderPath", "quality", "features"],
            StringComparer.Ordinal), "$");
        var asset = new RenderPipelineAsset
        {
            pipelineTypeId = RenderingJson.RequireString(root, "pipeline", "$.pipeline")
        };
        if (root.TryGetProperty("renderPath", out JsonElement renderPath))
        {
            asset.defaultRenderPath = RenderingJson.ParseEnum<RenderPath>(renderPath, "$.renderPath");
        }

        if (root.TryGetProperty("quality", out JsonElement quality))
        {
            ParseQuality(quality, asset.quality);
        }

        if (root.TryGetProperty("features", out JsonElement features))
        {
            asset.SetFeatures(ParseFeatures(features));
        }

        output.SetAsset(asset);
        await output.WriteArtifactAsync(
            "runtime",
            Encoding.UTF8.GetBytes(document.RootElement.GetRawText()),
            cancellationToken).ConfigureAwait(false);
    }

    private static void ParseQuality(JsonElement element, RenderQualitySettings quality)
    {
        RenderingJson.RequireObject(element, "$.quality");
        RenderingJson.RejectUnknown(element, new HashSet<string>(
            ["hdr", "bloom", "exposure", "directionalShadowCascades", "shadowResolution"],
            StringComparer.Ordinal), "$.quality");
        if (element.TryGetProperty("hdr", out JsonElement hdr))
        {
            quality.hdr = RenderingJson.RequireBoolean(hdr, "$.quality.hdr");
        }

        if (element.TryGetProperty("bloom", out JsonElement bloom))
        {
            quality.bloom = RenderingJson.RequireBoolean(bloom, "$.quality.bloom");
        }

        if (element.TryGetProperty("exposure", out JsonElement exposure))
        {
            quality.exposure = RenderingJson.RequireFiniteSingle(exposure, "$.quality.exposure");
        }

        if (element.TryGetProperty("directionalShadowCascades", out JsonElement cascades))
        {
            quality.directionalShadowCascades = RenderingJson.RequireInt32(
                cascades,
                "$.quality.directionalShadowCascades");
        }

        if (element.TryGetProperty("shadowResolution", out JsonElement resolution))
        {
            quality.shadowResolution = RenderingJson.RequireInt32(
                resolution,
                "$.quality.shadowResolution");
        }
    }

    private static IReadOnlyList<RenderFeatureConfiguration> ParseFeatures(JsonElement element)
    {
        RenderingJson.RequireKind(element, JsonValueKind.Array, "$.features");
        var result = new List<RenderFeatureConfiguration>();
        int index = 0;
        foreach (JsonElement feature in element.EnumerateArray())
        {
            string path = $"$.features[{index++}]";
            RenderingJson.RequireObject(feature, path);
            RenderingJson.RejectUnknown(feature, new HashSet<string>(
                ["type", "enabled", "settings"],
                StringComparer.Ordinal), path);
            string type = RenderingJson.RequireString(feature, "type", $"{path}.type");
            bool enabled = !feature.TryGetProperty("enabled", out JsonElement enabledElement)
                || RenderingJson.RequireBoolean(enabledElement, $"{path}.enabled");
            string settings = feature.TryGetProperty("settings", out JsonElement settingsElement)
                ? RenderingJson.RequireObject(settingsElement, $"{path}.settings").GetRawText()
                : "{}";
            result.Add(new RenderFeatureConfiguration(type, settings, enabled));
        }

        return result;
    }
}
