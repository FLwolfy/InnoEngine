using System;

using Inno.Assets;
using Inno.Core.Identity;
using Inno.Core.Execution;
using Inno.References;

namespace Inno.Assets;

/// <summary>
/// Resolves persistent asset references against one isolated asset database generation.
/// </summary>
public interface IAssetReferenceResolver : IReferenceResolver
{
    /// <summary>
    /// Gets the stable open protocol identifier for persistent asset references.
    /// </summary>
    ReferenceKindId IReferenceResolver.kindId => AssetReferenceProtocol.id;

    /// <summary>
    /// Resolves a backend-neutral reference descriptor through this asset generation.
    /// </summary>
    /// <param name="descriptor">
    /// Persistent target intent whose kind must be <see cref="AssetReferenceProtocol.id"/>.
    /// </param>
    /// <returns>
    /// A resolved runtime identity, an unassigned result, or a preserved missing result.
    /// </returns>
    /// <exception cref="RetirementPendingException">
    /// The asset owner is not quiescent; this barrier must propagate instead of becoming a Missing reference.
    /// </exception>
    ReferenceResolution IReferenceResolver.Resolve(ReferenceDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        if (descriptor.kindId != AssetReferenceProtocol.id)
        {
            return new ReferenceResolution(
                descriptor,
                ReferenceResolutionState.Invalid,
                diagnostic: $"Asset resolver cannot process reference kind '{descriptor.kindId}'.");
        }
        if (descriptor.isUnassigned)
            return new ReferenceResolution(descriptor, ReferenceResolutionState.Unassigned);
        try
        {
            AssetObject asset = Resolve(
                descriptor.targetPersistentId,
                descriptor.expectedStableTypeId,
                descriptor.lastKnownPath ?? string.Empty,
                typeof(AssetObject),
                "$reference");
            if (asset.identity.persistentId != descriptor.targetPersistentId)
            {
                return new ReferenceResolution(descriptor, ReferenceResolutionState.Invalid,
                    diagnostic: "The asset resolver returned a different persistent identity.");
            }
            if (asset.isMissing)
            {
                return new ReferenceResolution(
                    descriptor,
                    ReferenceResolutionState.Missing,
                    diagnostic: $"Asset '{descriptor.targetPersistentId:D}' is unavailable in the current generation.");
            }
            if (asset.identity.runtimeIdentity is not RuntimeIdentity runtimeIdentity)
            {
                return new ReferenceResolution(
                    descriptor,
                    ReferenceResolutionState.Invalid,
                    diagnostic: $"Asset '{descriptor.targetPersistentId:D}' is not registered in a runtime identity domain.");
            }
            return new ReferenceResolution(
                descriptor,
                ReferenceResolutionState.Resolved,
                runtimeIdentity);
        }
        catch (Exception pendingRetirement) when (RetirementPendingException.Find(pendingRetirement) is not null) { throw; }
        catch (Exception exception) when (exception is InvalidOperationException or System.IO.InvalidDataException)
        {
            return new ReferenceResolution(
                descriptor,
                ReferenceResolutionState.Missing,
                diagnostic: exception.Message);
        }
    }

    /// <summary>
    /// Resolves one serialized asset reference to the canonical object owned by this resolver.
    /// </summary>
    /// <param name="persistentId">
    /// The persistent asset identity encoded in the reference.
    /// </param>
    /// <param name="stableTypeId">
    /// The stable concrete asset type identity encoded in the reference.
    /// </param>
    /// <param name="lastKnownPath">
    /// The optional logical path retained for diagnostics and authoring recovery.
    /// </param>
    /// <param name="expectedType">
    /// The declared property type that the resolved object must satisfy.
    /// </param>
    /// <param name="propertyPath">
    /// The serialization property path used when reporting resolution failures.
    /// </param>
    /// <returns>
    /// The canonical compatible asset owned by this resolver's generation.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the reference cannot be resolved to a compatible asset.
    /// </exception>
    AssetObject Resolve(
        Guid persistentId,
        Guid stableTypeId,
        string lastKnownPath,
        Type expectedType,
        string propertyPath);
}
