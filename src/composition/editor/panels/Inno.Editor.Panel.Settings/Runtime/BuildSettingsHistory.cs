using System;
using System.Buffers.Binary;
using System.IO;

using Inno.Build;
using Inno.Editor.Interactions;

namespace Inno.Editor.Panel.Settings;

internal static class BuildSettingsHistory
{
    internal const string C_KIND = "build.settings.apply";

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
            throw new InvalidDataException("The Build Settings history payload is truncated.");
        int beforeLength = BinaryPrimitives.ReadInt32LittleEndian(payload);
        if (beforeLength < 0 || beforeLength > payload.Length - sizeof(int))
            throw new InvalidDataException("The Build Settings history payload has an invalid boundary.");
        return (
            payload.AsMemory(sizeof(int), beforeLength),
            payload.AsMemory(sizeof(int) + beforeLength));
    }
}

[EditorHistoryHandler(BuildSettingsHistory.C_KIND)]
internal sealed class BuildSettingsHistoryHandler(BuildSettingsStore settings) : EditorHistoryHandler
{
    /// <summary>
    /// Evaluates whether the requested change can be applied to the current generation.
    /// </summary>
    /// <param name="context">
    /// The operation scope that provides state, services, and ownership boundaries.
    /// </param>
    /// <param name="change">
    /// The neutral change payload to query or apply.
    /// </param>
    /// <param name="direction">
    /// The history direction that determines which state is applied.
    /// </param>
    /// <returns>
    /// The validated editor history availability that represents the completed operation.
    /// </returns>
    protected override EditorHistoryAvailability Query(
        EditorHistoryContext context,
        EditorHistoryChange change,
        EditorHistoryDirection direction)
    {
        try
        {
            (ReadOnlyMemory<byte> before, ReadOnlyMemory<byte> after) = BuildSettingsHistory.Read(change);
            settings.ValidateDocument(before.Span);
            settings.ValidateDocument(after.Span);
            return EditorHistoryAvailability.Available();
        }
        catch (Exception exception) when (
            exception is InvalidDataException or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            return EditorHistoryAvailability.Unavailable(exception.Message);
        }
    }

    /// <summary>
    /// Applies a validated change atomically at the caller-controlled commit point.
    /// </summary>
    /// <param name="context">
    /// The operation scope that provides state, services, and ownership boundaries.
    /// </param>
    /// <param name="change">
    /// The neutral change payload to query or apply.
    /// </param>
    /// <param name="direction">
    /// The history direction that determines which state is applied.
    /// </param>
    /// <returns>
    /// The validated editor history result that represents the completed operation.
    /// </returns>
    protected override EditorHistoryResult Apply(
        EditorHistoryContext context,
        EditorHistoryChange change,
        EditorHistoryDirection direction)
    {
        byte[] original = settings.CaptureDocument();
        try
        {
            (ReadOnlyMemory<byte> before, ReadOnlyMemory<byte> after) = BuildSettingsHistory.Read(change);
            settings.RestoreDocument(
                direction == EditorHistoryDirection.Undo ? before.Span : after.Span);
            return EditorHistoryResult.Success();
        }
        catch (Exception exception) when (
            exception is InvalidDataException or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            try
            {
                settings.RestoreDocument(original);
            }
            catch (Exception rollbackException)
            {
                return StateIntegrityFailure(
                    $"Build Settings transition failed: {exception.Message} " +
                    $"Rollback failed: {rollbackException.Message}");
            }
            return EditorHistoryResult.Failure(exception.Message);
        }
    }
}
