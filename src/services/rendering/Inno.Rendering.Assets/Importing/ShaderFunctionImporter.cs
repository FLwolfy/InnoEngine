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

[AssetImporterExtension]
internal sealed class ShaderFunctionImporter : AssetImporter<ShaderFunctionAsset>
{
    /// <inheritdoc />
    public override string importerId => "inno.rendering.shader-function";
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
        var request = new ShaderSourceRequest(new(context.assetPath.ToString(), context.ReadUtf8Text()), settings.entryPoint,
            new SourceResolver(context));
        using var frontends = new ShaderSourceFrontendRegistry(context.types);
        var requests = new List<ShaderSourceImplementationRequest> { new(settings.implementationId, settings.languageId, request) };
        foreach (ShaderFunctionAsset implementation in settings.implementations)
        {
            if (implementation is null || implementation.isMissing)
                throw new InvalidDataException("An alternate shader implementation is missing; its reference must be repaired before compilation.");
            context.DependsOnArtifact(implementation.identity.persistentId);
            using ArtifactLease lease = context.AcquireArtifact(implementation.identity.persistentId, ShaderSourceBundle.outputName);
            requests.AddRange(ShaderSourceBundle.Decode(File.ReadAllBytes(lease.info.absolutePath), context.serialization));
        }
        ShaderSourceModuleAnalysis module = frontends.AnalyzeModule(requests);
        if (!module.succeeded)
            throw new InvalidDataException(string.Join("\n", module.diagnostics.Select(static diagnostic =>
                $"{diagnostic.location.assetPath}:{diagnostic.location.line}:{diagnostic.location.column}: {diagnostic.message}")));
        output.SetAsset(new ShaderFunctionAsset
        {
            languageId = settings.languageId,
            entryPoint = settings.entryPoint,
            implementationId = settings.implementationId
        });
        await output.WriteArtifactAsync(ShaderSourceBundle.outputName, ShaderSourceBundle.Encode(module, context.serialization),
            cancellationToken, AssetDeploymentScope.AuthoringOnly).ConfigureAwait(false);
    }

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
