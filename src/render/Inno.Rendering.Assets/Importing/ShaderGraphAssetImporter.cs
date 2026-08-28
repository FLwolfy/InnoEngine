using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Inno.Assets.Loader;
using Inno.Rendering.ShaderGraph;

namespace Inno.Rendering.Assets;

[AssetImporterExtension]
internal sealed class ShaderGraphAssetImporter : AssetImporter<ShaderGraphAsset>
{
    public override string importerId => "inno.rendering.shader-graph";

    public override IReadOnlyList<string> supportedExtensions { get; } = [".ishadergraph"];

    protected override async ValueTask ImportAsync(
        AssetImportContext context,
        AssetImportWriter<ShaderGraphAsset> output,
        CancellationToken cancellationToken)
    {
        ShaderGraphDocumentData decoded = ShaderGraphDocumentCodec.Decode(context.ReadUtf8Text());
        var asset = new ShaderGraphAsset();
        asset.SetDocument(decoded.target, decoded.document);
        using var registry = new ShaderNodeRegistry();
        ShaderGraphCompileResult compilation = ShaderGraphCompiler.Compile(asset, registry);
        if (!compilation.succeeded || compilation.module is null)
        {
            string diagnostics = string.Join(
                "; ",
                compilation.diagnostics
                    .Where(static diagnostic => diagnostic.severity == ShaderDiagnosticSeverity.Error)
                    .Select(static diagnostic => $"{diagnostic.code}: {diagnostic.message}"));
            throw new RenderingAssetFormatException(
                context.relativePath,
                string.IsNullOrWhiteSpace(diagnostics)
                    ? "Shader Graph compilation did not produce shared Shader IR."
                    : diagnostics);
        }

        output.SetAsset(asset);
        byte[] artifact = ShaderIRArtifactCodec.Encode(compilation.module);
        await output.WriteArtifactAsync(
            "runtime",
            artifact,
            cancellationToken).ConfigureAwait(false);
        await output.WriteArtifactAsync("shader-ir", artifact, cancellationToken).ConfigureAwait(false);
    }
}
