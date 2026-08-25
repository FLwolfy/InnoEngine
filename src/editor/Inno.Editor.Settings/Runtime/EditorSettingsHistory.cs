using System;
using System.Buffers.Binary;
using System.IO;

using Inno.Editor.Interactions;

namespace Inno.Editor.Settings;

internal static class EditorSettingsHistory
{
    internal const string C_KIND = "editor.settings.apply";

    internal static EditorHistoryChange CreateChange(ReadOnlySpan<byte> before, ReadOnlySpan<byte> after)
    {
        byte[] payload = new byte[sizeof(int) + before.Length + after.Length];
        BinaryPrimitives.WriteInt32LittleEndian(payload, before.Length);
        before.CopyTo(payload.AsSpan(sizeof(int), before.Length));
        after.CopyTo(payload.AsSpan(sizeof(int) + before.Length));
        return new EditorHistoryChange(C_KIND, EditorHistoryPayload.FromBytes(payload));
    }

    internal static (ReadOnlyMemory<byte> Before, ReadOnlyMemory<byte> After) Read(
        EditorHistoryChange change)
    {
        byte[] payload = change.payload.ReadBytes();
        if (payload.Length < sizeof(int))
            throw new InvalidDataException("The Settings history payload is truncated.");
        int beforeLength = BinaryPrimitives.ReadInt32LittleEndian(payload);
        if (beforeLength < 0 || beforeLength > payload.Length - sizeof(int))
            throw new InvalidDataException("The Settings history payload has an invalid boundary.");
        ReadOnlyMemory<byte> before = payload.AsMemory(sizeof(int), beforeLength);
        ReadOnlyMemory<byte> after = payload.AsMemory(sizeof(int) + beforeLength);
        EditorSettingsStore.ValidateDocument(before.Span);
        EditorSettingsStore.ValidateDocument(after.Span);
        return (before, after);
    }
}

[EditorHistoryHandler(EditorSettingsHistory.C_KIND)]
internal sealed class EditorSettingsHistoryHandler(EditorSettings settings) : EditorHistoryHandler
{
    protected override EditorHistoryAvailability Query(
        EditorHistoryContext context,
        EditorHistoryChange change,
        EditorHistoryDirection direction)
    {
        try
        {
            _ = EditorSettingsHistory.Read(change);
            return EditorHistoryAvailability.Available();
        }
        catch (Exception exception) when (
            exception is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            return EditorHistoryAvailability.Unavailable(exception.Message);
        }
    }

    protected override EditorHistoryResult Apply(
        EditorHistoryContext context,
        EditorHistoryChange change,
        EditorHistoryDirection direction)
    {
        byte[] original = settings.CaptureDocument();
        try
        {
            (ReadOnlyMemory<byte> before, ReadOnlyMemory<byte> after) = EditorSettingsHistory.Read(change);
            settings.RestoreFromHistory(
                direction == EditorHistoryDirection.Undo ? before.Span : after.Span);
            settings.NotifyChanged();
            return EditorHistoryResult.Success();
        }
        catch (Exception exception) when (
            exception is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            try
            {
                settings.RestoreFromHistory(original);
            }
            catch (Exception rollbackException)
            {
                return StateIntegrityFailure(
                    $"Settings transition failed: {exception.Message} Rollback failed: {rollbackException.Message}");
            }
            return EditorHistoryResult.Failure(exception.Message);
        }
    }
}
