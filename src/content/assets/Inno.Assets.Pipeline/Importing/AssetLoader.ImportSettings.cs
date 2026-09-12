using System;
using System.IO;
using System.Linq;

using Inno.Assets;
using Inno.Core.Execution;
using Inno.Core.Serialization;

namespace Inno.Assets.Pipeline;

public sealed partial class AssetLoader
{
    /// <summary>Reads a detached importer settings value without changing source metadata.</summary>
    /// <param name="path">The isolated source path, not its sidecar path.</param>
    /// <returns>The current settings and the metadata fingerprint required for a subsequent save.</returns>
    /// <exception cref="NotSupportedException">The source has no registered importer.</exception>
    /// <exception cref="InvalidDataException">The persisted settings cannot be restored by the current importer.</exception>
    public AssetImportSettingsSnapshot GetImportSettings(AssetPath path)
        => Execute(() =>
        {
            string normalized = NormalizeAssetPath(path);
            AssetImporter importer = RequireSettingsImporterLocked(normalized);
            byte[]? metadata = ReadMetadata(GetMetaPath(normalized));
            byte[] settings = ReadImportSettingsBytes(importer, metadata);
            return new AssetImportSettingsSnapshot(
                RestoreImportSettingsLocked(importer, settings, null, normalized),
                ComputeSha256Hex(metadata ?? []));
        });

    /// <summary>
    /// Atomically saves importer settings and reimports the source. Failed imports retain both the saved settings
    /// and the previous successful artifact; callers must inspect the returned import status separately from saving.
    /// </summary>
    /// <param name="path">The writable isolated source path.</param>
    /// <param name="settings">A value of the current importer's settings type, or null to reset to defaults.</param>
    /// <param name="expectedFingerprint">The fingerprint returned when the settings were read.</param>
    /// <returns>True when reimport succeeds; false when settings were saved but reimport failed.</returns>
    /// <exception cref="IOException">The sidecar changed externally or could not be written.</exception>
    /// <exception cref="InvalidOperationException">The source is read-only or belongs to an isolated candidate.</exception>
    /// <exception cref="ArgumentException">The settings have a different type from the current importer.</exception>
    public bool SaveImportSettings(AssetPath path, ISerializable? settings, string expectedFingerprint)
        => Execute(() =>
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(expectedFingerprint);
            string normalized = NormalizeAssetPath(path);
            if (m_sourceMetadataStage is not null || GetMount(normalized).isReadOnly)
                throw new InvalidOperationException("Import settings can only be edited in the active writable source mount.");
            AssetImporter importer = RequireSettingsImporterLocked(normalized);
            ISerializable? defaults = importer.CreateImportSettings();
            if (settings is not null && (defaults is null || settings.GetType() != defaults.GetType()))
                throw new ArgumentException($"Settings do not match importer '{importer.importerId}'.", nameof(settings));
            byte[] encoded = settings is null ? [] : CaptureImportSettingsLocked(settings, normalized, null);
            string metaPath = GetMetaPath(normalized);
            byte[]? metadata = ReadMetadata(metaPath);
            if (!string.Equals(expectedFingerprint, ComputeSha256Hex(metadata ?? []), StringComparison.Ordinal))
                throw new IOException($"Import settings for '{normalized}' changed externally. Reload before saving.");
            AssetSourceMeta source = metadata is null
                ? new AssetSourceMeta
                {
                    persistentId = FindRecordLocked(normalized)?.persistentId ?? Guid.NewGuid(),
                    importerId = importer.importerId
                }
                : m_serialization.Deserialize<AssetSourceMeta>(metadata);
            if (source.persistentId == Guid.Empty ||
                !string.Equals(source.importerId, importer.importerId, StringComparison.Ordinal))
                throw new InvalidDataException($"Source metadata for '{normalized}' does not match its importer.");
            source.importerSettingsBytes = encoded;
            WriteAtomic(metaPath, m_serialization.Serialize(source));
            try { return ImportLocked(normalized); }
            catch (Exception failure) when (RetirementPendingException.Find(failure) is null)
            {
                // A reported failed import is a saved authoring state. An exceptional transaction is not:
                // restore the exact sidecar and refresh its old publication before the history caller retries.
                try
                {
                    if (metadata is null) System.IO.File.Delete(metaPath);
                    else WriteAtomic(metaPath, metadata);
                    _ = ImportLocked(normalized);
                }
                catch (Exception rollback) { throw new AggregateException("Import settings rollback failed.", failure, rollback); }
                throw;
            }
        });

    private AssetImporter RequireSettingsImporterLocked(string path)
    {
        if (!System.IO.File.Exists(GetSourcePath(path)))
            throw new FileNotFoundException($"Import settings source '{path}' does not exist.");
        return m_importers.FindByPath(path)
            ?? throw new NotSupportedException($"No importer is registered for '{path}'.");
    }

    private byte[] ReadImportSettingsBytesLocked(string path, AssetImporter importer)
        => ReadImportSettingsBytes(importer, ReadMetadata(GetMetaPath(path)));

    private byte[] ReadImportSettingsBytes(AssetImporter importer, byte[]? metadata)
    {
        if (metadata is null) return [];
        AssetSourceMeta source = m_serialization.Deserialize<AssetSourceMeta>(metadata);
        if (source.importerSettingsBytes.Length > 0 &&
            !string.Equals(source.importerId, importer.importerId, StringComparison.Ordinal))
            throw new InvalidDataException($"Stored import settings belong to '{source.importerId}', not '{importer.importerId}'.");
        return source.importerSettingsBytes;
    }

    private ISerializable? RestoreImportSettingsLocked(
        AssetImporter importer, byte[] bytes, AssetImportContext? context, string? ownerPath = null)
    {
        ISerializable? value = importer.CreateImportSettings();
        ownerPath ??= context!.assetPath.ToString();
        if (value is null)
        {
            if (bytes.Length != 0)
                throw new InvalidDataException($"Importer '{importer.importerId}' no longer declares the persisted settings type.");
            return null;
        }
        Guid typeId = m_types.GetTypeRef(value.GetType()).stableId;
        if (bytes.Length > 0)
        {
            SerializedImportSettings saved = m_serialization.Deserialize<SerializedImportSettings>(bytes);
            if (saved.stableTypeId != typeId)
                throw new InvalidDataException($"Import settings type '{saved.stableTypeId}' does not match '{typeId}'.");
            DeclareSettingsDependenciesLocked(saved.dependencies, ownerPath, null);
            m_serialization.Decode(saved.properties, reader =>
            {
                reader.RestoreProperties(value);
                return true;
            }, m_serializationContext);
        }
        // Defaults can also contain references; capture them through the same owner context.
        _ = CaptureImportSettingsLocked(value, ownerPath, context);
        return value;
    }

    private byte[] CaptureImportSettingsLocked(ISerializable settings, string ownerPath, AssetImportContext? context)
    {
        var dependencies = new AssetDependencyCollection();
        byte[] properties = m_serialization.Encode(writer => writer.WriteProperties(settings),
            AssetSerializationContext.Create(this, dependencies));
        AssetDependency[] descriptors = dependencies.dependencies.ToArray();
        DeclareSettingsDependenciesLocked(descriptors, ownerPath, context);
        return m_serialization.Serialize(new SerializedImportSettings
        {
            stableTypeId = m_types.GetTypeRef(settings.GetType()).stableId,
            properties = properties,
            dependencies = descriptors
        });
    }

    private void DeclareSettingsDependenciesLocked(AssetDependency[] dependencies, string ownerPath, AssetImportContext? context)
    {
        foreach (AssetDependency dependency in dependencies)
        {
            AssetRecord? record = FindRecordByIdWithoutLoading(dependency.persistentId);
            string path = record?.relativePath ?? dependency.lastKnownPath;
            if (!string.IsNullOrWhiteSpace(path))
            {
                ValidateSourceReferenceLocked(ownerPath, NormalizeRelativePath(path));
                context?.DependsOnSource(AssetPath.Parse(path));
            }
            context?.DependsOnArtifact(dependency.persistentId);
        }
    }

    private bool AreImportSettingsStaleLocked(AssetRecord record, AssetImporter importer)
    {
        string path = GetMetaPath(record.relativePath);
        bool exists = AssetSourceFileStamp.TryCapture(path, out AssetSourceFileStamp stamp);
        if (m_sourceMetadataStage is null && exists && record.settingsStamp.Equals(stamp))
            return false;
        string hash;
        try { hash = ComputeSha256Hex(ReadImportSettingsBytesLocked(record.relativePath, importer)); }
        catch (Exception exception) when (
            exception is IOException or ArgumentException or InvalidOperationException or FormatException &&
            RetirementPendingException.Find(exception) is null)
        {
            // Reimport owns the diagnostic and last-good policy; a read check never repairs source bytes.
            return true;
        }
        if (!string.Equals(hash, record.meta.importerSettingsHash, StringComparison.Ordinal))
            return true;
        record.settingsStamp = stamp;
        return false;
    }

    private sealed class SerializedImportSettings : ISerializable
    {
        [SerializableProperty] internal Guid stableTypeId { get; set; }
        [SerializableProperty] internal byte[] properties { get; set; } = [];
        [SerializableProperty] internal AssetDependency[] dependencies { get; set; } = [];
    }
}
