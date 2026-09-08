using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Inno.Assets;
using Inno.Assets.Pipeline;
using Inno.Rendering;
using Inno.Rendering.Assets;

namespace Inno.Rendering.Assets;

[AssetImporterExtension]
internal sealed partial class ShaderSourceAssetImporter : AssetImporter<ShaderSourceAsset>
{
    /// <summary>
    /// Gets the stable importer identity used in artifact fingerprints.
    /// </summary>
    public override string importerId => "inno.rendering.shader-source";

    /// <summary>
    /// Gets the normalized source extensions accepted by this importer.
    /// </summary>
    public override IReadOnlyList<string> supportedExtensions { get; } = [".sc"];

    /// <summary>
    /// Imports source content into a validated runtime asset and artifact set.
    /// </summary>
    /// <param name="context">
    /// The operation scope that provides state, services, and ownership boundaries.
    /// </param>
    /// <param name="output">
    /// The import output writer that receives runtime data and dependency declarations.
    /// </param>
    /// <param name="cancellationToken">
    /// The token that cancels the operation before it commits.
    /// </param>
    /// <returns>
    /// An asynchronous operation that completes after all requested work has finished.
    /// </returns>
    protected override async ValueTask ImportAsync(
        AssetImportContext context,
        AssetImportWriter<ShaderSourceAsset> output,
        CancellationToken cancellationToken)
    {
        AssetPath root = context.assetPath;
        var visiting = new HashSet<AssetPath>();
        string content = Expand(root, context.ReadUtf8Text(), context, visiting);
        output.SetAsset(new ShaderSourceAsset(content));
        await output.WriteArtifactAsync("runtime", Encoding.UTF8.GetBytes(content), cancellationToken)
            .ConfigureAwait(false);
        await output.WriteArtifactAsync("source", Encoding.UTF8.GetBytes(content), cancellationToken)
            .ConfigureAwait(false);
    }

    private static string Expand(
        AssetPath source,
        string content,
        AssetImportContext context,
        HashSet<AssetPath> visiting)
    {
        if (!visiting.Add(source))
            throw new InvalidDataException($"Shader include cycle contains '{source}'.");
        try
        {
            return IncludePattern().Replace(content, match =>
            {
                string include = match.Groups["path"].Value;
                bool isQuotedAsset = match.Groups["open"].Value == "\"";
                if (!isQuotedAsset && !include.Contains("::", StringComparison.Ordinal))
                    return match.Value;
                AssetPath dependency = ResolveInclude(source, include);
                string dependencyContent = context.ReadSourceUtf8Text(dependency);
                return $"\n// begin include {dependency}\n" +
                       Expand(dependency, dependencyContent, context, visiting) +
                       $"\n// end include {dependency}\n";
            });
        }
        finally
        {
            visiting.Remove(source);
        }
    }

    private static AssetPath ResolveInclude(AssetPath owner, string include)
    {
        string value = include.Replace('\\', '/');
        if (value.Contains("::", StringComparison.Ordinal))
            return AssetPath.Parse(value);
        string directory = Path.GetDirectoryName(owner.localPath)?.Replace('\\', '/') ?? string.Empty;
        string combined = string.IsNullOrEmpty(directory) ? value : directory + "/" + value;
        return new AssetPath(owner.source, combined);
    }

    [GeneratedRegex(
        "^\\s*#include\\s+(?<open>[\"<])(?<path>[^\">]+)[\">]",
        RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex IncludePattern();
}
