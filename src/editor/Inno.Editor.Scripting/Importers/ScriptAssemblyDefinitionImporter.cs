using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Inno.Assets.Core;
using Inno.Assets.Loader;
using Inno.Assets.Serialization;

namespace Inno.Editor.Scripting;

[AssetImporterExtension]
internal sealed class ScriptAssemblyDefinitionImporter : AssetImporter<ScriptAssemblyDefinitionAsset>
{
    public override string importerId => "inno.editor.script-assembly-definition";
    public override IReadOnlyList<string> supportedExtensions { get; } = [".iasmdef"];

    protected override async ValueTask ImportAsync(
        AssetImportContext context,
        AssetImportWriter<ScriptAssemblyDefinitionAsset> output,
        CancellationToken cancellationToken)
    {
        ScriptAssemblyDefinitionAsset asset = NativeAssetSourceSerialization.Import<ScriptAssemblyDefinitionAsset>(
            context.sourceBytes.Span,
            out IReadOnlyList<AssetDependency> dependencies);
        foreach (AssetDependency dependency in dependencies)
            output.DependsOnAsset(dependency);
        if (string.IsNullOrWhiteSpace(asset.assemblyName))
            throw new InvalidOperationException("Assembly definition name is required.");
        if (!Enum.IsDefined(asset.scope))
            throw new InvalidOperationException("Assembly definition scope must be Runtime or Editor.");
        output.SetAsset(asset);
        await output.WriteArtifactAsync("source", context.sourceBytes, cancellationToken)
            .ConfigureAwait(false);
    }

    protected override ValueTask<ReadOnlyMemory<byte>?> ExportAsync(
        ScriptAssemblyDefinitionAsset asset,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(asset.assemblyName))
            throw new InvalidOperationException("Assembly definition name is required.");
        return ValueTask.FromResult<ReadOnlyMemory<byte>?>(NativeAssetSourceSerialization.Export(asset));
    }
}
