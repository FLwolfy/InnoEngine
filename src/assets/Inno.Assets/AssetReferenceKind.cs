namespace Inno.Assets;

/// <summary>
/// Identifies a reference source known to the asset pipeline.
/// </summary>
public enum AssetReferenceKind
{
    /// <summary>
    /// The reference originates from another asset's runtime dependencies.
    /// </summary>
    AssetDependency,
    /// <summary>
    /// The reference originates from a serialized property.
    /// </summary>
    SerializedProperty,
    /// <summary>
    /// The reference originates from a scene resource.
    /// </summary>
    SceneResource,
    /// <summary>
    /// The reference originates from a prefab source.
    /// </summary>
    PrefabSource,
    /// <summary>
    /// The reference originates from an editor-only view or selection.
    /// </summary>
    Editor,
    /// <summary>
    /// The reference originates from a runtime subsystem.
    /// </summary>
    RuntimeSubsystem
}
