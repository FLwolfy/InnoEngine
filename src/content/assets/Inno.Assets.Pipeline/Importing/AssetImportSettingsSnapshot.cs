using Inno.Core.Serialization;

namespace Inno.Assets.Pipeline;

/// <summary>
/// Provides a detached settings value and an optimistic concurrency token for its source sidecar.
/// </summary>
/// <remarks>
/// The value belongs to the current importer generation. Editors must retain neutral serialized state,
/// not this instance, across extension reloads. Reading this snapshot never edits source metadata.
/// </remarks>
public sealed class AssetImportSettingsSnapshot
{
    internal AssetImportSettingsSnapshot(ISerializable? value, string fingerprint)
    {
        this.value = value;
        this.fingerprint = fingerprint;
    }

    /// <summary>Gets the editable settings copy, or null for an importer without settings.</summary>
    public ISerializable? value { get; }

    /// <summary>Gets the source metadata fingerprint required when saving this copy.</summary>
    public string fingerprint { get; }
}
