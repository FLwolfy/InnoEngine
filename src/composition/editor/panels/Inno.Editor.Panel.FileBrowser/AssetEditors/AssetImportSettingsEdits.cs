using System;
using System.IO;
using Inno.Assets;
using Inno.Assets.Pipeline;
using Inno.Core.Serialization;
using Inno.Editor.Core;
using Inno.Editor.Interactions;
using Inno.Extensibility.Types;

namespace Inno.Editor.Panel.FileBrowser;

/// <summary>Applies importer configuration through the common sidecar pipeline and stable-identity Editor history.</summary>
[EditorModule("assets.import-settings-edits", order: 60)]
public sealed class AssetImportSettingsEdits : EditorModule
{
    internal const string C_HISTORY = "inno.assets/import-settings";
    private readonly AssetPipeline m_assets;
    private readonly SerializationRegistry m_serialization;
    private readonly TypeCatalog m_types;
    private readonly EditorInteractions m_interactions;
    /// <summary>Uses the authoring owners responsible for source identity, converter generations and shared undo.</summary>
    /// <param name="assets">Authoritative asset pipeline.</param>
    /// <param name="serialization">Current owner converter registry.</param>
    /// <param name="types">Current type generation owner.</param>
    /// <param name="interactions">Shared Editor history owner.</param>
    [Inno.Scripting.Api.ScriptingApiIgnore]
    public AssetImportSettingsEdits(AssetPipeline assets, SerializationRegistry serialization, TypeCatalog types, EditorInteractions interactions)
    {
        m_assets = assets ?? throw new ArgumentNullException(nameof(assets));
        m_serialization = serialization ?? throw new ArgumentNullException(nameof(serialization));
        m_types = types ?? throw new ArgumentNullException(nameof(types));
        m_interactions = interactions ?? throw new ArgumentNullException(nameof(interactions));
    }

    /// <summary>Saves one settings gesture, recording neutral before/after properties even when reimport reports an error.</summary>
    /// <param name="path">Writable source asset path.</param>
    /// <param name="settings">Detached current-generation settings value.</param>
    /// <param name="expectedFingerprint">Sidecar fingerprint captured when editing began.</param>
    /// <returns>Whether the saved configuration reimported successfully; false does not mean the settings were discarded.</returns>
    public bool Apply(AssetPath path, ISerializable settings, string expectedFingerprint)
    {
        ArgumentNullException.ThrowIfNull(settings);
        AssetImportSettingsSnapshot before = m_assets.GetImportSettings(path);
        if (before.fingerprint != expectedFingerprint) throw new IOException("Import settings changed externally. Reload before applying.");
        if (before.value is null || before.value.GetType() != settings.GetType()) throw new InvalidOperationException("The importer settings type changed. Reload the editor.");
        if (!m_assets.TryGetPersistentId(path, out Guid id)) throw new InvalidOperationException("The source has no persistent identity.");
        var data = new ChangeData { assetId = id, typeId = m_types.GetTypeRef(settings.GetType()).stableId,
            before = Capture(before.value), after = Capture(settings) };
        if (data.before.AsSpan().SequenceEqual(data.after)) return m_assets.TryGetInfo(id, out AssetInfo? info) && info?.status == AssetImportStatus.Imported;
        bool imported = m_assets.SaveImportSettings(path, settings, expectedFingerprint);
        var change = new EditorHistoryChange(C_HISTORY, EditorHistoryPayload.FromBytes(m_serialization.Serialize(data)));
        try { m_interactions.history.RecordApplied("Change Import Settings", change); }
        catch (Exception failure) when (Inno.Core.Execution.RetirementPendingException.Find(failure) is null)
        {
            change.Dispose();
            try { m_assets.SaveImportSettings(path, before.value, m_assets.GetImportSettings(path).fingerprint); }
            catch (Exception rollback) { throw new AggregateException(failure, rollback); }
            throw;
        }
        return imported;
    }

    internal void Validate(EditorHistoryChange change, EditorHistoryDirection direction)
    {
        ChangeData data = m_serialization.Deserialize<ChangeData>(change.payload.ReadBytes());
        _ = ReadCurrent(data, direction);
    }

    internal void ApplyHistory(EditorHistoryChange change, EditorHistoryDirection direction)
    {
        ChangeData data = m_serialization.Deserialize<ChangeData>(change.payload.ReadBytes());
        (AssetPath path, AssetImportSettingsSnapshot current) = ReadCurrent(data, direction);
        byte[] next = direction == EditorHistoryDirection.Undo ? data.before : data.after;
        ISerializable settings = current.value!;
        m_serialization.Decode(next, reader => { reader.RestoreProperties(settings); return true; }, AssetSerializationContext.Create(m_assets));
        // Import failure is a valid persisted authoring state, not a failed history transaction.
        m_assets.SaveImportSettings(path, settings, current.fingerprint);
    }

    private (AssetPath, AssetImportSettingsSnapshot) ReadCurrent(ChangeData data, EditorHistoryDirection direction)
    {
        if (!m_assets.TryGetInfo(data.assetId, out AssetInfo? info) || info is null || info.status == AssetImportStatus.Missing)
            throw new IOException("The import-settings source is missing. History is retained until it returns.");
        if (!m_assets.TryGetFileSystemEntry(info.assetPath, out AssetFileEntry entry) || entry.isReadOnly)
            throw new IOException("The import-settings source is not writable.");
        AssetImportSettingsSnapshot current = m_assets.GetImportSettings(info.assetPath);
        if (current.value is null || m_types.GetTypeRef(current.value.GetType()).stableId != data.typeId)
            throw new InvalidOperationException("The importer settings extension is unavailable or changed type.");
        byte[] expected = direction == EditorHistoryDirection.Undo ? data.after : data.before;
        if (!Capture(current.value).AsSpan().SequenceEqual(expected)) throw new IOException("Import settings changed outside this history operation.");
        return (info.assetPath, current);
    }

    private byte[] Capture(ISerializable value)
        => m_serialization.Encode(writer => writer.WriteProperties(value), AssetSerializationContext.Create(m_assets));

    private sealed class ChangeData : ISerializable
    {
        [SerializableProperty] internal Guid assetId { get; set; }
        [SerializableProperty] internal Guid typeId { get; set; }
        [SerializableProperty] internal byte[] before { get; set; } = [];
        [SerializableProperty] internal byte[] after { get; set; } = [];
    }
}

[EditorHistoryHandler(AssetImportSettingsEdits.C_HISTORY)]
internal sealed class AssetImportSettingsHistory(AssetImportSettingsEdits edits) : EditorHistoryHandler
{
    protected override EditorHistoryAvailability Query(EditorHistoryContext context, EditorHistoryChange change, EditorHistoryDirection direction)
    {
        try { edits.Validate(change, direction); return EditorHistoryAvailability.Available(); }
        catch (Exception failure) when ((failure is IOException or InvalidOperationException or ArgumentException or FormatException) && Inno.Core.Execution.RetirementPendingException.Find(failure) is null)
        { return EditorHistoryAvailability.Unavailable(failure.Message); }
    }
    protected override EditorHistoryResult Apply(EditorHistoryContext context, EditorHistoryChange change, EditorHistoryDirection direction)
    {
        try { edits.ApplyHistory(change, direction); return EditorHistoryResult.Success(); }
        catch (Exception failure) when ((failure is IOException or InvalidOperationException or ArgumentException or FormatException) && Inno.Core.Execution.RetirementPendingException.Find(failure) is null)
        { return EditorHistoryResult.Failure(failure.Message); }
    }
}
