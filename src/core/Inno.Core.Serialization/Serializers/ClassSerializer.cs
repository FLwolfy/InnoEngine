using System;
using System.Collections;
using System.Collections.Generic;

using Inno.Core.Reflection;

namespace Inno.Core.Serialization;

internal static class ClassSerializer
{
    internal static bool IsSerializableClassType(Type type)
    {
        Type t = Nullable.GetUnderlyingType(type) ?? type;
        return t.IsClass && typeof(ISerializable).IsAssignableFrom(t);
    }

    internal static object Serialize(ISerializable value)
    {
        Type runtimeType = value.GetType();
        if (!TypeCache.TryGetRuntimeTypeId(runtimeType, out int runtimeTypeId))
            throw new InvalidOperationException($"Runtime type '{runtimeType.FullName}' is not loaded in TypeCache.");

        return new Dictionary<string, object?>(2, StringComparer.Ordinal)
        {
            ["__runtimeTypeId"] = runtimeTypeId,
            ["__stableTypeId"] = TypeCache.TryGetStableTypeId(runtimeType, out Guid stableTypeId) ? stableTypeId.ToString("D") : null,
            ["data"] = value.CaptureState()
        };
    }

    internal static object Deserialize(object? node, Type declaredType)
    {
        if (!typeof(ISerializable).IsAssignableFrom(declaredType))
            throw new InvalidOperationException($"Type '{declaredType.FullName}' is not assignable to {nameof(ISerializable)}.");

        Dictionary<string, object?> wrapper = CoerceToStringKeyDictionary(node);
        if (!wrapper.TryGetValue("data", out object? dataObj) || dataObj is not SerializingState data)
            throw new InvalidOperationException("Serializable wrapper missing 'data' (SerializingState).");

        Type runtimeType = declaredType;
        if (wrapper.TryGetValue("__stableTypeId", out object? stableTypeObj) &&
            stableTypeObj is string stableTypeText &&
            Guid.TryParse(stableTypeText, out Guid stableTypeId) &&
            TypeCache.TryResolveType(stableTypeId, out Type? stableResolved) &&
            stableResolved is not null &&
            declaredType.IsAssignableFrom(stableResolved))
        {
            runtimeType = stableResolved;
        }
        else if (wrapper.TryGetValue("__runtimeTypeId", out object? runtimeTypeObj) &&
                 TryReadRuntimeTypeId(runtimeTypeObj, out int runtimeTypeId) &&
                 TypeCache.TryResolveType(runtimeTypeId, out Type? runtimeResolved) &&
                 runtimeResolved is not null &&
                 declaredType.IsAssignableFrom(runtimeResolved))
        {
            runtimeType = runtimeResolved;
        }

        ISerializable instance = ISerializable.CreateSerializableInstance(runtimeType);
        instance.RestoreState(data);
        return instance;
    }

    private static Dictionary<string, object?> CoerceToStringKeyDictionary(object? raw)
    {
        if (raw is Dictionary<string, object?> dict)
            return dict;

        if (raw is IDictionary idict)
        {
            var result = new Dictionary<string, object?>(idict.Count, StringComparer.Ordinal);
            foreach (DictionaryEntry entry in idict)
            {
                if (entry.Key is not string key)
                    throw new InvalidOperationException($"Serializable wrapper dict keys must be strings. Got key type: {entry.Key?.GetType().FullName ?? "null"}");

                result[key] = entry.Value;
            }

            return result;
        }

        throw new InvalidOperationException($"Serializable wrapper must be a dictionary. Got: {raw?.GetType().FullName ?? "null"}");
    }

    private static bool TryReadRuntimeTypeId(object? raw, out int runtimeTypeId)
    {
        switch (raw)
        {
            case int i:
                runtimeTypeId = i;
                return true;
            case long l when l is >= int.MinValue and <= int.MaxValue:
                runtimeTypeId = (int)l;
                return true;
            case string s when int.TryParse(s, out int parsed):
                runtimeTypeId = parsed;
                return true;
            default:
                runtimeTypeId = default;
                return false;
        }
    }
}
