using System;
using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.Text;
using Inno.Core.Graphs;
using Inno.Editor.Interactions;

namespace Inno.Editor.Graph;

internal static class GraphHistoryDocumentCodec
{
    public static byte[] Encode(GraphDocument document) => GraphDocumentCodec.Encode(document);

    public static GraphDocument Decode(ReadOnlySpan<byte> bytes) => GraphDocumentCodec.Decode(bytes);
}

internal sealed record GraphHistoryData(
    string documentId,
    byte[] before,
    byte[] after,
    long timestamp)
{
    public const string C_KIND = "editor.graph.document";
    private static readonly UTF8Encoding S_UTF8 = new(false, true);

    public static EditorHistoryChange CreateChange(
        string documentId,
        ReadOnlySpan<byte> before,
        ReadOnlySpan<byte> after,
        string? mergeKey)
    {
        var data = new GraphHistoryData(documentId, before.ToArray(), after.ToArray(), Stopwatch.GetTimestamp());
        return new EditorHistoryChange(C_KIND, EditorHistoryPayload.FromBytes(data.Encode()), mergeKey);
    }

    public byte[] Encode()
    {
        byte[] id = S_UTF8.GetBytes(documentId);
        byte[] result = new byte[checked((sizeof(int) * 3) + sizeof(long) + id.Length + before.Length + after.Length)];
        Span<byte> span = result;
        BinaryPrimitives.WriteInt32LittleEndian(span, id.Length);
        id.CopyTo(span[sizeof(int)..]);
        int offset = sizeof(int) + id.Length;
        BinaryPrimitives.WriteInt32LittleEndian(span[offset..], before.Length);
        offset += sizeof(int);
        before.CopyTo(span[offset..]);
        offset += before.Length;
        BinaryPrimitives.WriteInt32LittleEndian(span[offset..], after.Length);
        offset += sizeof(int);
        after.CopyTo(span[offset..]);
        offset += after.Length;
        BinaryPrimitives.WriteInt64LittleEndian(span[offset..], timestamp);
        return result;
    }

    public static GraphHistoryData Decode(ReadOnlySpan<byte> payload)
    {
        int offset = 0;
        int idLength = ReadLength(payload, ref offset);
        string id = S_UTF8.GetString(ReadSlice(payload, ref offset, idLength));
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        int beforeLength = ReadLength(payload, ref offset);
        byte[] before = ReadSlice(payload, ref offset, beforeLength).ToArray();
        int afterLength = ReadLength(payload, ref offset);
        byte[] after = ReadSlice(payload, ref offset, afterLength).ToArray();
        if (payload.Length - offset != sizeof(long))
        {
            throw new InvalidDataException("Graph History payload has an invalid timestamp boundary.");
        }

        long timestamp = BinaryPrimitives.ReadInt64LittleEndian(payload[offset..]);
        _ = GraphHistoryDocumentCodec.Decode(before);
        _ = GraphHistoryDocumentCodec.Decode(after);
        return new GraphHistoryData(id, before, after, timestamp);
    }

    private static int ReadLength(ReadOnlySpan<byte> payload, ref int offset)
    {
        if (payload.Length - offset < sizeof(int))
        {
            throw new InvalidDataException("Graph History payload is truncated.");
        }

        int length = BinaryPrimitives.ReadInt32LittleEndian(payload[offset..]);
        offset += sizeof(int);
        if (length < 0 || length > payload.Length - offset)
        {
            throw new InvalidDataException("Graph History payload contains an invalid length.");
        }

        return length;
    }

    private static ReadOnlySpan<byte> ReadSlice(ReadOnlySpan<byte> payload, ref int offset, int length)
    {
        ReadOnlySpan<byte> result = payload.Slice(offset, length);
        offset += length;
        return result;
    }
}

internal readonly record struct GraphHistoryTransitionResult(
    EditorHistoryResult result,
    bool stateIntegrityLost);

internal static class GraphHistoryTransition
{
    public static GraphHistoryTransitionResult Apply(
        GraphEditorModule module,
        EditorHistoryChange change,
        EditorHistoryDirection direction)
    {
        try
        {
            GraphHistoryData data = GraphHistoryData.Decode(change.payload.ReadBytes());
            if (!module.TryResolve(data.documentId, out GraphDocumentSession? session) || session is null)
            {
                return new GraphHistoryTransitionResult(
                    EditorHistoryResult.Failure($"Graph document '{data.documentId}' is not open."),
                    false);
            }

            GraphDocument original = session.document.Clone();
            try
            {
                session.document.ReplaceContents(GraphHistoryDocumentCodec.Decode(
                    direction == EditorHistoryDirection.Undo ? data.before : data.after));
                session.revision++;
                session.isDirty = true;
                return new GraphHistoryTransitionResult(EditorHistoryResult.Success(), false);
            }
            catch (Exception exception)
            {
                try
                {
                    session.document.ReplaceContents(original);
                }
                catch (Exception rollbackException)
                {
                    return new GraphHistoryTransitionResult(
                        EditorHistoryResult.Failure(
                            $"Graph transition failed: {exception.Message} Rollback failed: {rollbackException.Message}"),
                        true);
                }

                return new GraphHistoryTransitionResult(EditorHistoryResult.Failure(exception.Message), false);
            }
        }
        catch (Exception exception)
        {
            return new GraphHistoryTransitionResult(
                EditorHistoryResult.Failure($"Graph History payload is invalid: {exception.Message}"),
                false);
        }
    }
}

[EditorHistoryHandler(GraphHistoryData.C_KIND)]
internal sealed class GraphHistoryHandler(GraphEditorModule module) : EditorHistoryHandler
{
    protected override EditorHistoryAvailability Query(
        EditorHistoryContext context,
        EditorHistoryChange change,
        EditorHistoryDirection direction)
    {
        _ = context;
        _ = direction;
        try
        {
            GraphHistoryData data = GraphHistoryData.Decode(change.payload.ReadBytes());
            return module.TryResolve(data.documentId, out _)
                ? EditorHistoryAvailability.Available()
                : EditorHistoryAvailability.Unavailable($"Graph document '{data.documentId}' is not open.");
        }
        catch (Exception exception)
        {
            return EditorHistoryAvailability.Unavailable($"Graph History payload is invalid: {exception.Message}");
        }
    }

    protected override EditorHistoryResult Apply(
        EditorHistoryContext context,
        EditorHistoryChange change,
        EditorHistoryDirection direction)
    {
        _ = context;
        GraphHistoryTransitionResult transition = GraphHistoryTransition.Apply(module, change, direction);
        return transition.stateIntegrityLost
            ? StateIntegrityFailure(transition.result.message)
            : transition.result;
    }

    protected override bool TryMerge(
        EditorHistoryChange older,
        EditorHistoryChange newer,
        out EditorHistoryChange? merged)
    {
        merged = null;
        if (older.mergeKey is null || !StringComparer.Ordinal.Equals(older.mergeKey, newer.mergeKey))
        {
            return false;
        }

        try
        {
            GraphHistoryData previous = GraphHistoryData.Decode(older.payload.ReadBytes());
            GraphHistoryData current = GraphHistoryData.Decode(newer.payload.ReadBytes());
            if (!StringComparer.Ordinal.Equals(previous.documentId, current.documentId)
                || Stopwatch.GetElapsedTime(previous.timestamp, current.timestamp).TotalSeconds > 1.0)
            {
                return false;
            }

            var combined = new GraphHistoryData(
                previous.documentId,
                previous.before,
                current.after,
                current.timestamp);
            merged = new EditorHistoryChange(
                GraphHistoryData.C_KIND,
                EditorHistoryPayload.FromBytes(combined.Encode()),
                older.mergeKey);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
