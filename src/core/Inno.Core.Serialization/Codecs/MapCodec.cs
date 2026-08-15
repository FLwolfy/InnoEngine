using System;
using System.Collections;
using System.Collections.Generic;

namespace Inno.Core.Serialization;

internal sealed class MapCodec<TMap> : SerializationCodec<TMap>
{
    public override bool CanHandleType(Type declaredType)
        => CollectionTypeUtility.TryGetMapTypes(declaredType, out _, out _);

    public override object? OnSerialize(in SerializeContext context, TMap value)
    {
        if (!CollectionTypeUtility.TryEnumerateMap(
                value,
                typeof(TMap),
                out List<KeyValuePair<object?, object?>> entries))
        {
            throw new InvalidOperationException($"Map-like type '{typeof(TMap).FullName}' cannot be enumerated.");
        }

        if (!CollectionTypeUtility.TryGetMapTypes(typeof(TMap), out Type keyType, out Type valueType))
        {
            throw new InvalidOperationException($"Type '{typeof(TMap).FullName}' is not a supported map type.");
        }

        var map = new Dictionary<object, object?>(entries.Count);
        for (int i = 0; i < entries.Count; i++)
        {
            KeyValuePair<object?, object?> entry = entries[i];
            map[context.Serialize(entry.Key, keyType)!] = context.Serialize(entry.Value, valueType);
        }

        return map;
    }

    public override TMap OnDeserialize(in DeserializeContext context, object? node)
    {
        if (!TryReadEntries(node, out List<KeyValuePair<object?, object?>> entries))
        {
            throw new InvalidOperationException(
                $"Map node must be IDictionary or key-value sequence. Got: {node?.GetType().FullName ?? "null"}");
        }

        if (!CollectionTypeUtility.TryGetMapTypes(typeof(TMap), out Type keyType, out Type valueType))
        {
            throw new InvalidOperationException($"Type '{typeof(TMap).FullName}' is not a map-like type.");
        }

        var values = new KeyValuePair<object?, object?>[entries.Count];
        for (int i = 0; i < entries.Count; i++)
        {
            values[i] = new KeyValuePair<object?, object?>(
                context.Deserialize(entries[i].Key, keyType),
                context.Deserialize(entries[i].Value, valueType));
        }

        return (TMap)CollectionTypeUtility.BuildMap(
            typeof(TMap),
            keyType,
            valueType,
            values);
    }

    private static bool TryReadEntries(
        object? raw,
        out List<KeyValuePair<object?, object?>> entries)
    {
        if (raw is IDictionary dictionary)
        {
            entries = new List<KeyValuePair<object?, object?>>(dictionary.Count);
            foreach (DictionaryEntry entry in dictionary)
            {
                entries.Add(new KeyValuePair<object?, object?>(entry.Key, entry.Value));
            }

            return true;
        }

        if (raw is not IEnumerable enumerable)
        {
            entries = [];
            return false;
        }

        entries = [];
        foreach (object? item in enumerable)
        {
            if (item is null)
            {
                return false;
            }

            Type itemType = item.GetType();
            if (!itemType.IsGenericType || itemType.GetGenericTypeDefinition() != typeof(KeyValuePair<,>))
            {
                return false;
            }

            entries.Add(new KeyValuePair<object?, object?>(
                itemType.GetProperty("Key")!.GetValue(item),
                itemType.GetProperty("Value")!.GetValue(item)));
        }

        return true;
    }
}
