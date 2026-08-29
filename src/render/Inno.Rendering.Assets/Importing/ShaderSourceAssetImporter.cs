using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Inno.Assets.Core;
using Inno.Assets.Loader;

namespace Inno.Rendering.Assets;

[AssetImporterExtension]
internal sealed partial class ShaderSourceAssetImporter : AssetImporter<ShaderSourceAsset>
{
    public override string importerId => "inno.rendering.shader-source";

    public override IReadOnlyList<string> supportedExtensions { get; } = [".sc"];

    protected override async ValueTask ImportAsync(
        AssetImportContext context,
        AssetImportWriter<ShaderSourceAsset> output,
        CancellationToken cancellationToken)
    {
        AssetPath root = AssetPath.Parse(context.relativePath);
        var visiting = new HashSet<AssetPath>();
        string content = Expand(root, context.ReadUtf8Text(), context, visiting);
        output.SetAsset(new ShaderSourceAsset { content = content });
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
                string dependencyContent = context.ReadSourceUtf8Text(dependency.ToString());
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
