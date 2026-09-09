using System;
using System.Collections.Generic;
using System.IO;
using Inno.Assets;
using Inno.References;

namespace Inno.Assets.Pipeline;

internal sealed class AssetSourceMountRecovery(AssetPipeline owner, AssetSourceMountTransaction transaction)
    : IReferenceRecoveryParticipant
{
    /// <summary>
    /// Keeps the previous loader and its canonical instances intact until provisional publication.
    /// </summary>
    public void PrepareForActivation() { }

    /// <summary>
    /// Publishes the candidate identity domain before shared reference slots are resolved.
    /// </summary>
    public void Apply() => owner.ActivatePreparedSourceMounts(transaction);

    /// <summary>
    /// Rejects corrupted canonical recovery while allowing genuinely unavailable assets to remain missing.
    /// </summary>
    /// <param name="changes">
    /// The candidate resolutions of previously live or missing canonical slots.
    /// </param>
    public void Validate(IReadOnlyList<ReferenceRecoveryChange> changes)
    {
        foreach (ReferenceRecoveryChange change in changes)
        {
            ReferenceResolution result = change.resolution;
            if (result.state is ReferenceResolutionState.Invalid or ReferenceResolutionState.TypeMismatch)
                throw new InvalidDataException(result.diagnostic ?? "Asset recovery has an invalid identity.");
            if (result.state == ReferenceResolutionState.Missing &&
                transaction.candidateLoader.TryGetInfo(result.descriptor.targetPersistentId, out AssetInfo? info) &&
                info!.status == AssetImportStatus.Imported &&
                !transaction.candidateLoader.IsSourceBackedTypeUnavailable(
                    result.descriptor.targetPersistentId))
                throw new InvalidDataException($"Imported asset '{info.assetPath}' could not recover its canonical state: {result.diagnostic}");
        }
    }

    /// <summary>
    /// Commits the candidate catalog and retires the previous source owners.
    /// </summary>
    public void Complete() => owner.CompletePreparedSourceMounts(transaction);

    /// <summary>
    /// Restores the untouched previous loader and identity registrations before converter rollback.
    /// </summary>
    public void RollbackStructure() => owner.RollbackPreparedSourceMounts(transaction);

    /// <summary>
    /// Finishes compensation without decoding old objects through candidate serializers.
    /// </summary>
    public void RestorePreviousState() { }
}
