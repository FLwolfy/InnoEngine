using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Inno.Core.Serialization;

/// <summary>
/// Rewrites selected scalar identities in a serialized value and its nested serialized payloads.
/// </summary>
public static class SerializedIdentityRemapper
{
    private static readonly byte[] s_valueMagic = Encoding.UTF8.GetBytes("INNO-BINARY-CURRENT");
    private static readonly byte[] s_propertyMagic = Encoding.UTF8.GetBytes("INNO-PROPERTY-SNAPSHOT-CURRENT");

    /// <summary>
    /// Replaces exact Guid and string values while retaining the binary serialization structure.
    /// </summary>
    /// <param name="source">
    /// A complete serialized value or property snapshot.
    /// </param>
    /// <param name="identities">
    /// Old to new Guid values.
    /// </param>
    /// <param name="paths">
    /// Optional exact old to new string values.
    /// </param>
    /// <returns>
    /// A detached rewritten payload; unknown binary subvalues remain unchanged.
    /// </returns>
    /// <exception cref="System.IO.InvalidDataException">
    /// The recognized serialized format is malformed.
    /// </exception>
    public static byte[] Rewrite(ReadOnlySpan<byte> source,
        IReadOnlyDictionary<Guid, Guid> identities,
        IReadOnlyDictionary<string, string>? paths = null)
    {
        ArgumentNullException.ThrowIfNull(identities);
        if (HasMagic(source, s_valueMagic))
            return BinarySerializationFormat.Encode(RewriteNode(BinarySerializationFormat.Decode(source), identities, paths));
        if (HasMagic(source, s_propertyMagic))
        {
            IReadOnlyList<SerializationPropertySnapshot> snapshots = PropertySnapshotBinaryFormat.Decode(source);
            return PropertySnapshotBinaryFormat.Encode(snapshots.Select(snapshot =>
                new SerializationPropertySnapshot(snapshot.name, snapshot.propertyType,
                    Rewrite(snapshot.dataSpan, identities, paths))).ToArray());
        }
        return source.ToArray();
    }

    private static SerializationNode RewriteNode(SerializationNode node,
        IReadOnlyDictionary<Guid, Guid> identities,
        IReadOnlyDictionary<string, string>? paths)
    {
        switch (node)
        {
            case ScalarSerializationNode { value: Guid id } when identities.TryGetValue(id, out Guid replacement):
                return new ScalarSerializationNode(replacement);
            case ScalarSerializationNode { value: string path } when paths is not null
                && paths.TryGetValue(path, out string? replacement):
                return new ScalarSerializationNode(replacement);
            case BinarySerializationNode binary:
                return new BinarySerializationNode(Rewrite(binary.value, identities, paths));
            case ObjectSerializationNode value:
            {
                var rewritten = new ObjectSerializationNode();
                foreach ((string name, SerializationNode child) in value.values)
                    rewritten.values.Add(name, RewriteNode(child, identities, paths));
                return rewritten;
            }
            case ArraySerializationNode value:
            {
                var rewritten = new ArraySerializationNode();
                foreach (SerializationNode child in value.values)
                    rewritten.values.Add(RewriteNode(child, identities, paths));
                return rewritten;
            }
            case MapSerializationNode value:
            {
                var rewritten = new MapSerializationNode();
                foreach (KeyValuePair<SerializationNode, SerializationNode> entry in value.values)
                    rewritten.values.Add(new KeyValuePair<SerializationNode, SerializationNode>(
                        RewriteNode(entry.Key, identities, paths),
                        RewriteNode(entry.Value, identities, paths)));
                return rewritten;
            }
            default:
                return node;
        }
    }

    private static bool HasMagic(ReadOnlySpan<byte> source, ReadOnlySpan<byte> magic)
        => source.Length > magic.Length && source[0] == magic.Length
            && source.Slice(1, magic.Length).SequenceEqual(magic);
}
