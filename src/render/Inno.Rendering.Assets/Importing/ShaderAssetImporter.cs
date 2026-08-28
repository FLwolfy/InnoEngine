using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Inno.Assets.Loader;

namespace Inno.Rendering.Assets;

[AssetImporterExtension]
internal sealed class ShaderAssetImporter : AssetImporter<ShaderAsset>
{
    public override string importerId => "inno.rendering.shader";

    public override IReadOnlyList<string> supportedExtensions { get; } = [".ishader"];

    protected override async ValueTask ImportAsync(
        AssetImportContext context,
        AssetImportWriter<ShaderAsset> output,
        CancellationToken cancellationToken)
    {
        string assetRoot = ResolveAssetRoot(context);
        string ReadSource(string relativePath)
        {
            string fullPath = Path.GetFullPath(Path.Combine(
                assetRoot,
                relativePath.Replace('/', Path.DirectorySeparatorChar)));
            string requiredPrefix = assetRoot.TrimEnd(Path.DirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            if (!fullPath.StartsWith(requiredPrefix, StringComparison.OrdinalIgnoreCase))
            {
                throw new RenderingAssetFormatException(
                    relativePath,
                    "Shader sources must remain inside the project Assets directory.");
            }

            return File.ReadAllText(fullPath);
        }

        HandwrittenShaderParseResult parsed = HandwrittenShaderParser.Parse(
            context.relativePath,
            context.ReadUtf8Text(),
            ReadSource);
        foreach (string dependency in parsed.dependencies)
        {
            output.DependsOnSource(dependency);
        }

        var asset = new ShaderAsset();
        asset.SetDefinition(parsed.module.definition);
        output.SetAsset(asset);
        byte[] artifact = ShaderIRArtifactCodec.Encode(parsed.module);
        await output.WriteArtifactAsync("runtime", artifact, cancellationToken).ConfigureAwait(false);
        await output.WriteArtifactAsync("shader-ir", artifact, cancellationToken).ConfigureAwait(false);
    }

    private static string ResolveAssetRoot(AssetImportContext context)
    {
        string normalizedRelative = context.relativePath.Replace('/', Path.DirectorySeparatorChar);
        if (!context.absolutePath.EndsWith(normalizedRelative, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Source '{context.absolutePath}' does not end with relative path '{context.relativePath}'.");
        }

        return context.absolutePath[..^normalizedRelative.Length].TrimEnd(Path.DirectorySeparatorChar);
    }
}
