using System;
using Inno.Extensibility.Types;
using Inno.References;

namespace Inno.Scene;

internal sealed class SceneElementReferenceResolver(SceneWorld world, TypeCacheSnapshot types) : IReferenceResolver
{
    internal static ReferenceKindId id { get; } = new("inno.reference.scene-element");

    ReferenceKindId IReferenceResolver.kindId => id;

    ReferenceResolution IReferenceResolver.Resolve(ReferenceDescriptor descriptor)
    {
        EngineObject? target = world.Find<EngineObject>(descriptor.targetPersistentId);
        if (target is null || target.isDestroyed || target is MissingGameComponent or MissingGameSystem)
            return new ReferenceResolution(descriptor, ReferenceResolutionState.Missing,
                diagnostic: $"Scene element '{descriptor.targetPersistentId:D}' or its type is unavailable.");
        if (descriptor.expectedStableTypeId != Guid.Empty &&
            types.GetTypeRef(target.GetType()).stableId != descriptor.expectedStableTypeId)
            return new ReferenceResolution(descriptor, ReferenceResolutionState.TypeMismatch,
                diagnostic: $"Scene element '{descriptor.targetPersistentId:D}' does not implement stable type '{descriptor.expectedStableTypeId:D}'.");
        return new ReferenceResolution(descriptor, ReferenceResolutionState.Resolved, target.identity.runtimeIdentity);
    }
}
