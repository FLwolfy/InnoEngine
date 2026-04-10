using System;
using System.Collections.Generic;
using System.Linq;

using Inno.Core.Reflection;

namespace Inno.Core.Serialization;

/// <summary>
/// Global serialization codec discovery and dispatch manager.
/// </summary>
public static class SerializableManager
{
    /// <summary>
    /// Returns true when a codec can be resolved for <paramref name="declaredType"/>.
    /// </summary>
    public static bool HasCodec(Type declaredType)
    {
        ArgumentNullException.ThrowIfNull(declaredType);
        return TryGetCodec(declaredType) is not null;
    }

    internal static bool TrySerialize(
        object value,
        Type declaredType,
        Func<object?, Type, object?> fallbackSerialize,
        out object? node)
    {
        ISerializationCodec? codec = TryGetCodec(declaredType);
        if (codec == null)
        {
            node = null;
            return false;
        }

        node = codec.OnSerialize(new SerializeContext(fallbackSerialize), value);
        return true;
    }

    internal static bool TryDeserialize(
        object? node,
        Type declaredType,
        Func<object?, Type, object?> fallbackDeserialize,
        out object? value)
    {
        ISerializationCodec? codec = TryGetCodec(declaredType);
        if (codec == null)
        {
            value = null;
            return false;
        }

        value = codec.OnDeserialize(new DeserializeContext(fallbackDeserialize), node);
        return true;
    }

    private static ISerializationCodec? TryGetCodec(Type declaredType)
    {
        Type key = Normalize(declaredType);
        ISerializationCodec? splitCodec = TryGetSplitCodec(key);
        if (splitCodec is not null)
            return splitCodec;

        Type[] discoveredTypes = DiscoverLoadedCodecTypes();
        return TryCreateCodecFromTypeRegistrations(discoveredTypes, key);
    }

    private static ISerializationCodec? TryGetSplitCodec(Type declaredType)
    {
        if (PrimitiveSerializer.IsPrimitiveType(declaredType))
            return TryCreateClosedCodec(typeof(SerializablePrimitiveCodec<>), declaredType);

        if (ClassSerializer.IsSerializableClassType(declaredType))
            return TryCreateClosedCodec(typeof(SerializableClassCodec<>), declaredType);

        if (StructSerializer.IsStructPayloadType(declaredType))
            return TryCreateClosedCodec(typeof(SerializableStructCodec<>), declaredType);

        return null;
    }

    private static ISerializationCodec? TryCreateClosedCodec(Type openCodecType, Type targetType)
    {
        try
        {
            Type closed = openCodecType.MakeGenericType(targetType);
            return (ISerializationCodec)Activator.CreateInstance(closed, nonPublic: true)!;
        }
        catch
        {
            return null;
        }
    }

    private static ISerializationCodec? TryCreateCodecFromTypeRegistrations(Type[] registrations, Type targetType)
    {
        ISerializationCodec? bestFallbackCodec = null;
        int bestFallbackScore = int.MaxValue;
        for (int i = 0; i < registrations.Length; i++)
        {
            Type codecType = registrations[i];
            if (!TryCreateCodecForTarget(
                    codecType,
                    targetType,
                    out ISerializationCodec? created,
                    out bool exactMatch,
                    out int fallbackScore))
            {
                continue;
            }

            if (exactMatch)
                return created;

            if (fallbackScore < bestFallbackScore)
            {
                bestFallbackScore = fallbackScore;
                bestFallbackCodec = created;
            }
        }

        return bestFallbackCodec;
    }

    private static bool TryCreateCodecForTarget(
        Type codecType,
        Type targetType,
        out ISerializationCodec? createdCodec,
        out bool exactMatch,
        out int fallbackScore)
    {
        createdCodec = null;
        exactMatch = false;
        fallbackScore = int.MaxValue;

        if (!codecType.IsGenericTypeDefinition)
        {
            if (codecType.IsAbstract || !typeof(ISerializationCodec).IsAssignableFrom(codecType))
                return false;

            ISerializationCodec codec = (ISerializationCodec)Activator.CreateInstance(codecType, nonPublic: true)!;
            Type normalized = Normalize(codec.targetType);
            if (!codec.CanHandleType(targetType))
                return false;

            createdCodec = codec;
            exactMatch = normalized == targetType;
            fallbackScore = normalized.IsAssignableFrom(targetType)
                ? GetTypeDistance(targetType, normalized)
                : 100_000;
            return true;
        }

        if (!TryGetCodecTargetPattern(codecType, out Type targetPattern))
            return false;

        var map = new Dictionary<Type, Type>();
        if (!TryUnifyTypePattern(targetPattern, targetType, map))
            return false;

        Type[] genericArgs = codecType.GetGenericArguments();
        var closedArgs = new Type[genericArgs.Length];
        for (int i = 0; i < genericArgs.Length; i++)
        {
            if (!map.TryGetValue(genericArgs[i], out Type? arg))
                return false;

            closedArgs[i] = arg;
        }

        Type closedCodecType;
        try
        {
            closedCodecType = codecType.MakeGenericType(closedArgs);
        }
        catch
        {
            return false;
        }

        ISerializationCodec codecInstance = (ISerializationCodec)Activator.CreateInstance(closedCodecType, nonPublic: true)!;
        Type normalizedTarget = Normalize(codecInstance.targetType);
        if (!codecInstance.CanHandleType(targetType))
            return false;

        createdCodec = codecInstance;
        exactMatch = normalizedTarget == targetType;
        fallbackScore = normalizedTarget.IsAssignableFrom(targetType)
            ? GetTypeDistance(targetType, normalizedTarget)
            : 100_000;
        return true;
    }

    private static Type[] DiscoverLoadedCodecTypes()
    {
        static bool IsCandidate(Type t)
        {
            if (t.IsAbstract || t.IsInterface)
                return false;

            if (typeof(ISerializationCodec).IsAssignableFrom(t))
                return true;

            return t.IsGenericTypeDefinition && ImplementsCodecInterface(t);
        }

        Type[] discoveredFromTypeCache = TypeCache.GetTypesImplementing<ISerializationCodec>()
            .Where(IsCandidate)
            .ToArray();

        Type[] discoveredFromAssemblies = AppDomain.CurrentDomain.GetAssemblies()
            .Where(static a => !a.IsDynamic)
            .SelectMany(static a =>
            {
                try { return a.GetTypes(); }
                catch { return Type.EmptyTypes; }
            })
            .Where(IsCandidate)
            .Concat(discoveredFromTypeCache)
            .Distinct()
            .OrderBy(static t => t.FullName, StringComparer.Ordinal)
            .ToArray();

        return discoveredFromAssemblies;
    }

    private static bool TryGetCodecTargetPattern(Type codecType, out Type targetPattern)
    {
        for (Type? t = codecType; t is not null; t = t.BaseType)
        {
            if (!t.IsGenericType)
                continue;

            if (t.GetGenericTypeDefinition() == typeof(SerializationCodec<>))
            {
                targetPattern = t.GetGenericArguments()[0];
                return true;
            }
        }

        targetPattern = null!;
        return false;
    }

    private static bool TryUnifyTypePattern(Type pattern, Type concrete, Dictionary<Type, Type> map)
    {
        if (pattern.IsGenericParameter)
        {
            if (map.TryGetValue(pattern, out Type? existing))
                return existing == concrete;

            map[pattern] = concrete;
            return true;
        }

        if (pattern.IsArray)
        {
            if (!concrete.IsArray || pattern.GetArrayRank() != concrete.GetArrayRank())
                return false;

            return TryUnifyTypePattern(pattern.GetElementType()!, concrete.GetElementType()!, map);
        }

        if (pattern.IsGenericType)
        {
            if (!concrete.IsGenericType || concrete.GetGenericTypeDefinition() != pattern.GetGenericTypeDefinition())
                return false;

            Type[] pArgs = pattern.GetGenericArguments();
            Type[] cArgs = concrete.GetGenericArguments();
            for (int i = 0; i < pArgs.Length; i++)
            {
                if (!TryUnifyTypePattern(pArgs[i], cArgs[i], map))
                    return false;
            }

            return true;
        }

        return pattern == concrete;
    }

    private static bool ImplementsCodecInterface(Type codecType)
    {
        Type[] interfaces = codecType.GetInterfaces();
        for (int i = 0; i < interfaces.Length; i++)
        {
            if (interfaces[i] == typeof(ISerializationCodec))
                return true;
        }

        return false;
    }

    private static Type Normalize(Type type)
        => Nullable.GetUnderlyingType(type) ?? type;

    private static int GetTypeDistance(Type derived, Type baseType)
    {
        if (derived == baseType)
            return 0;

        int distance = 0;
        for (Type? current = derived; current is not null; current = current.BaseType)
        {
            if (current == baseType)
                return distance;

            distance++;
        }

        if (baseType.IsInterface && baseType.IsAssignableFrom(derived))
            return 1_000;

        return 10_000;
    }
}
