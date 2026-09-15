using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Inno.Assets;
using Inno.Assets.Pipeline;
using Inno.Core.Serialization;
using Inno.Rendering.Shaders;

namespace Inno.Rendering.Assets;

[AssetImporter("inno.rendering.shader-function")]
internal sealed class ShaderFunctionImporter : AssetImporter<ShaderFunctionAsset>
{
    /// <inheritdoc />
    public override IReadOnlyList<string> supportedExtensions { get; } = [".ishadersource"];
    /// <inheritdoc />
    public override AssetDeploymentScope deploymentScope => AssetDeploymentScope.AuthoringOnly;
    /// <inheritdoc />
    public override ISerializable CreateImportSettings() => new ShaderSourceImportSettings();

    /// <inheritdoc />
    protected override async ValueTask ImportAsync(AssetImportContext context, AssetImportWriter<ShaderFunctionAsset> output,
        CancellationToken cancellationToken)
    {
        var settings = context.importSettings as ShaderSourceImportSettings
            ?? throw new InvalidOperationException("Shader source requires its standard import settings.");
        ArgumentException.ThrowIfNullOrWhiteSpace(settings.languageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(settings.implementationId);
        string[] exports = (settings.exports ?? []).Where(static value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        if (exports.Length == 0) throw new InvalidDataException("Shader source libraries must export at least one named function.");
        var source = new ShaderSourceFile(context.assetPath.ToString(), context.ReadUtf8Text());
        using var frontends = new ShaderSourceFrontendRegistry(context.types);
        var modules = new Dictionary<string, ShaderSourceModuleAnalysis>(StringComparer.Ordinal);
        foreach (string export in exports)
        {
            var requests = new List<ShaderSourceImplementationRequest>
            { new(settings.implementationId, settings.languageId, new(source, export, new SourceResolver(context))) };
            foreach (ShaderFunctionAsset implementation in settings.implementations ?? [])
            {
                if (implementation is null || implementation.isMissing)
                    throw new InvalidDataException("An alternate shader implementation is missing; its reference must be repaired before compilation.");
                context.DependsOnArtifact(implementation.identity.persistentId);
                using ArtifactLease lease = context.AcquireArtifact(implementation.identity.persistentId, ShaderSourceBundle.outputName);
                requests.AddRange(ShaderSourceBundle.Decode(File.ReadAllBytes(lease.info.absolutePath), export, context.serialization));
            }
            ShaderSourceModuleAnalysis module = frontends.AnalyzeModule(requests);
            if (!module.succeeded)
                throw new InvalidDataException(string.Join("\n", module.diagnostics.Select(static diagnostic =>
                    $"{diagnostic.location.assetPath}:{diagnostic.location.line}:{diagnostic.location.column}: {diagnostic.message}")));
            modules.Add(export, module);
        }
        output.SetAsset(new ShaderFunctionAsset
        {
            languageId = settings.languageId,
            exports = exports,
            implementationId = settings.implementationId,
            catalogPath = NormalizeCatalogPath(settings.catalogPath),
            catalogOrder = settings.catalogOrder
        });
        await output.WriteArtifactAsync(ShaderSourceBundle.outputName, ShaderSourceBundle.Encode(modules, context.serialization),
            cancellationToken, AssetDeploymentScope.AuthoringOnly).ConfigureAwait(false);
    }

    private static string NormalizeCatalogPath(string? value)
        => string.Join('/', (value ?? string.Empty).Split('/',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    private sealed class SourceResolver(AssetImportContext context) : IShaderSourceResolver
    {
        public ShaderSourceFile ReadInclude(string includingFile, string include)
        {
            AssetPath owner = AssetPath.Parse(includingFile);
            string normalized = include.Replace('\\', '/');
            AssetPath path = normalized.Contains("::", StringComparison.Ordinal) ? AssetPath.Parse(normalized)
                : new AssetPath(owner.source, (Path.GetDirectoryName(owner.localPath)?.Replace('\\', '/') is { Length: > 0 } directory
                    ? directory + "/" : "") + normalized);
            return new(path.ToString(), context.ReadSourceUtf8Text(path));
        }
    }
}
