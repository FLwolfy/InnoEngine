using System;
using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.Text;
using Inno.Core.Graphs;
using Inno.Core.Serialization;
using Inno.Editor.Interactions;

namespace Inno.Editor.Graph;

internal static class GraphHistoryDocumentCodec
{
    /// <summary>
    /// Encodes the supplied value into deterministic structured data.
    /// </summary>
    /// <param name="document">
    /// The document consumed by encode; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <param name="serialization">
    /// The serialization consumed by encode; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <returns>
    /// An immutable snapshot of the values selected by the operation.
    /// </returns>
    public static byte[] Encode(
        GraphDocument document,
        SerializationRegistry serialization)
        => GraphDocumentCodec.Encode(document, serialization);

    /// <summary>
    /// Decodes deterministic structured data into a validated value.
    /// </summary>
    /// <param name="bytes">
    /// The complete immutable byte payload consumed by this operation.
    /// </param>
    /// <param name="serialization">
    /// The serialization consumed by decode; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <returns>
    /// The validated graph document that represents the completed operation.
    /// </returns>
    public static GraphDocument Decode(
        ReadOnlySpan<byte> bytes,
        SerializationRegistry serialization)
        => GraphDocumentCodec.Decode(bytes, serialization);
}

internal sealed record GraphHistoryData(
    string documentId,
    byte[] before,
    byte[] after,
    long timestamp)
{
    /// <summary>
    /// The c kind value used as part of this type's public representation.
    /// </summary>
    public const string C_KIND = "editor.graph.document";
    private static readonly UTF8Encoding S_UTF8 = new(false, true);

    /// <summary>
    /// Creates and validates a caller-owned change value.
    /// </summary>
    /// <param name="documentId">
    /// The document id text validated by the create change operation.
    /// </param>
    /// <param name="before">
    /// The before consumed by create change; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <param name="after">
    /// The after consumed by create change; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <param name="mergeKey">
    /// The merge key text validated by the create change operation.
    /// </param>
    /// <returns>
    /// The validated editor history change that represents the completed operation.
    /// </returns>
    public static EditorHistoryChange CreateChange(
        string documentId,
        ReadOnlySpan<byte> before,
        ReadOnlySpan<byte> after,
        string? mergeKey)
    {
        var data = new GraphHistoryData(documentId, before.ToArray(), after.ToArray(), Stopwatch.GetTimestamp());
        return new EditorHistoryChange(C_KIND, EditorHistoryPayload.FromBytes(data.Encode()), mergeKey);
    }

    /// <summary>
    /// Encodes the supplied value into deterministic structured data.
    /// </summary>
    /// <returns>
    /// An immutable snapshot of the values selected by the operation.
    /// </returns>
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

    /// <summary>
    /// Decodes deterministic structured data into a validated value.
    /// </summary>
    /// <param name="payload">
    /// The payload consumed by decode; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <param name="serialization">
    /// The serialization consumed by decode; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <returns>
    /// The validated graph history data that represents the completed operation.
    /// </returns>
    public static GraphHistoryData Decode(
        ReadOnlySpan<byte> payload,
        SerializationRegistry serialization)
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
        _ = GraphHistoryDocumentCodec.Decode(before, serialization);
        _ = GraphHistoryDocumentCodec.Decode(after, serialization);
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
    /// <summary>
    /// Applies a validated change atomically at the caller-controlled commit point.
    /// </summary>
    /// <param name="module">
    /// The module consumed by apply; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <param name="change">
    /// The neutral change payload to query or apply.
    /// </param>
    /// <param name="direction">
    /// The history direction that determines which state is applied.
    /// </param>
    /// <returns>
    /// The validated graph history transition result that represents the completed operation.
    /// </returns>
    public static GraphHistoryTransitionResult Apply(
        GraphEditorModule module,
        EditorHistoryChange change,
        EditorHistoryDirection direction)
    {
        try
        {
            GraphHistoryData data = GraphHistoryData.Decode(
                change.payload.ReadBytes(),
                module.serialization);
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
                    direction == EditorHistoryDirection.Undo ? data.before : data.after,
                    module.serialization));
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
        _ = context;
        _ = direction;
        try
        {
            GraphHistoryData data = GraphHistoryData.Decode(
                change.payload.ReadBytes(),
                module.serialization);
            return module.TryResolve(data.documentId, out _)
                ? EditorHistoryAvailability.Available()
                : EditorHistoryAvailability.Unavailable($"Graph document '{data.documentId}' is not open.");
        }
        catch (Exception exception)
        {
            return EditorHistoryAvailability.Unavailable($"Graph History payload is invalid: {exception.Message}");
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
        _ = context;
        GraphHistoryTransitionResult transition = GraphHistoryTransition.Apply(module, change, direction);
        return transition.stateIntegrityLost
            ? StateIntegrityFailure(transition.result.message)
            : transition.result;
    }

    /// <summary>
    /// Attempts to merge without changing state when the operation cannot complete.
    /// </summary>
    /// <param name="older">
    /// The earlier history payload considered for coalescing.
    /// </param>
    /// <param name="newer">
    /// The later history payload considered for coalescing.
    /// </param>
    /// <param name="merged">
    /// Receives the neutral coalesced payload when merging succeeds.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the requested condition is satisfied; otherwise, <see langword="false"/>.
    /// </returns>
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
            GraphHistoryData previous = GraphHistoryData.Decode(
                older.payload.ReadBytes(),
                module.serialization);
            GraphHistoryData current = GraphHistoryData.Decode(
                newer.payload.ReadBytes(),
                module.serialization);
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
