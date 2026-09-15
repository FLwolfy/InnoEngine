using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Inno.Assets;
using Inno.Assets.Pipeline;
using Inno.Extensibility.Types;

namespace Inno.Assets.Pipeline.DuplicateImporterFixture;

[StableTypeId("27a0c796-5e7b-4e65-b9e5-2a0ced76675c")]
public sealed class DuplicateImporterAsset : AssetObject;

[AssetImporter("inno.tests.duplicate-importer")]
public sealed class DuplicateImporterA : AssetImporter<DuplicateImporterAsset>
{
    public override IReadOnlyList<string> supportedExtensions { get; } = [".duplicateid"];

    protected override ValueTask ImportAsync(
        AssetImportContext context,
        AssetImportWriter<DuplicateImporterAsset> output,
        CancellationToken cancellationToken)
    {
        output.SetAsset(new DuplicateImporterAsset());
        return output.WriteArtifactAsync("runtime", context.sourceBytes, cancellationToken);
    }
}

[AssetImporter("inno.tests.duplicate-importer")]
public sealed class DuplicateImporterB : AssetImporter<DuplicateImporterAsset>
{
    public override IReadOnlyList<string> supportedExtensions { get; } = [".duplicateid-b"];

    protected override ValueTask ImportAsync(
        AssetImportContext context,
        AssetImportWriter<DuplicateImporterAsset> output,
        CancellationToken cancellationToken)
    {
        output.SetAsset(new DuplicateImporterAsset());
        return output.WriteArtifactAsync("runtime", context.sourceBytes, cancellationToken);
    }
}
