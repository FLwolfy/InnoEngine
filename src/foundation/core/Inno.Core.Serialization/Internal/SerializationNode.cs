using System;
using System.Collections.Generic;

namespace Inno.Core.Serialization;

internal abstract record SerializationNode;

internal sealed record NullSerializationNode : SerializationNode
{
    internal static NullSerializationNode instance { get; } = new();

    private NullSerializationNode()
    {
    }
}

internal sealed record ScalarSerializationNode(object value) : SerializationNode;

internal sealed record BinarySerializationNode(byte[] value) : SerializationNode;

internal sealed record ObjectSerializationNode : SerializationNode
{
    internal Dictionary<string, SerializationNode> values { get; } = new(StringComparer.Ordinal);
}

internal sealed record ArraySerializationNode : SerializationNode
{
    internal List<SerializationNode> values { get; } = [];
}

internal sealed record MapSerializationNode : SerializationNode
{
    internal List<KeyValuePair<SerializationNode, SerializationNode>> values { get; } = [];
}
