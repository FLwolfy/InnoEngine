using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using Inno.Core.Reflection;
using Inno.Core.Serialization.Converters;

namespace Inno.Core.Serialization;

internal static class ConverterRegistry
{
    private static readonly object s_sync = new();
    private static readonly Dictionary<Type, ConverterInvoker?> s_cache = [];
    private static readonly Dictionary<Type, object> s_converterInstances = [];
    private static IReadOnlyList<Type> s_registrations = [];
    private static bool s_isInitialized;
    private static int s_cachedAssemblyCount = -1;

    internal static void Initialize()
    {
        lock (s_sync)
        {
            s_isInitialized = true;
            RefreshInternal();
        }
    }

    internal static void Refresh()
    {
        lock (s_sync)
        {
            if (s_isInitialized)
                RefreshInternal();
        }
    }

    internal static void Shutdown()
    {
        lock (s_sync)
        {
            s_cache.Clear();
            s_converterInstances.Clear();
            s_registrations = [];
            s_cachedAssemblyCount = -1;
            s_isInitialized = false;
        }
    }

    internal static ConverterInvoker? Resolve(Type valueType)
    {
        ArgumentNullException.ThrowIfNull(valueType);
        Type normalizedType = Nullable.GetUnderlyingType(valueType) ?? valueType;
        lock (s_sync)
        {
            if (!s_isInitialized)
                throw new InvalidOperationException("The serialization converter registry is not initialized.");

            int assemblyCount = AppDomain.CurrentDomain.GetAssemblies().Length;
            if (assemblyCount != s_cachedAssemblyCount)
                RefreshInternal();
            if (s_cache.TryGetValue(normalizedType, out ConverterInvoker? cached))
                return cached;

            ConverterInvoker? resolved = ResolveUncached(normalizedType);
            s_cache.Add(normalizedType, resolved);
            return resolved;
        }
    }

    private static void RefreshInternal()
    {
        IReadOnlyList<Type> registrations = TypeCache.GetTypesWithAttribute<SerializationExtensionAttribute>();
        for (int i = 0; i < registrations.Count; i++)
        {
            if (!TryGetConverterTargetPattern(registrations[i], out _))
            {
                throw new InvalidOperationException(
                    $"Serialization extension '{registrations[i].FullName}' must inherit SerializationConverter<T>.");
            }
        }

        s_registrations = registrations;
        s_cache.Clear();
        s_converterInstances.Clear();
        s_cachedAssemblyCount = AppDomain.CurrentDomain.GetAssemblies().Length;
    }

    private static ConverterInvoker? ResolveUncached(Type valueType)
    {
        var candidates = new List<ConverterCandidate>();
        for (int i = 0; i < s_registrations.Count; i++)
        {
            if (TryCreateCandidate(s_registrations[i], valueType, out ConverterCandidate? candidate))
                candidates.Add(candidate!);
        }

        if (candidates.Count == 0)
            return null;

        int bestDistance = candidates.Min(static candidate => candidate.distance);
        ConverterCandidate[] best = candidates
            .Where(candidate => candidate.distance == bestDistance)
            .OrderBy(static candidate => candidate.converterType.FullName, StringComparer.Ordinal)
            .ToArray();
        if (best.Length > 1)
        {
            throw new InvalidOperationException(
                $"Serialization type '{valueType.FullName}' has ambiguous converters " +
                $"'{best[0].converterType.FullName}' and '{best[1].converterType.FullName}' at distance {bestDistance}.");
        }

        return best[0].invoker;
    }

    private static bool TryCreateCandidate(
        Type registeredType,
        Type valueType,
        out ConverterCandidate? candidate)
    {
        candidate = null;
        if (!TryGetConverterTargetPattern(registeredType, out Type targetPattern))
            throw new InvalidOperationException(
                $"Serialization extension '{registeredType.FullName}' must inherit SerializationConverter<T>.");

        Type closedConverterType = registeredType;
        if (registeredType.ContainsGenericParameters)
        {
            if (!registeredType.IsGenericTypeDefinition)
                return false;

            var bindings = new Dictionary<Type, Type>();
            if (!TryUnify(targetPattern, valueType, bindings))
                return false;

            Type[] parameters = registeredType.GetGenericArguments();
            var arguments = new Type[parameters.Length];
            for (int i = 0; i < parameters.Length; i++)
            {
                if (!bindings.TryGetValue(parameters[i], out Type? argument))
                    return false;
                arguments[i] = argument;
            }

            try
            {
                closedConverterType = registeredType.MakeGenericType(arguments);
            }
            catch
            {
                return false;
            }
        }

        if (!TryGetConverterTargetPattern(closedConverterType, out Type targetType) || targetType.ContainsGenericParameters)
            return false;
        if (!targetType.IsAssignableFrom(valueType))
            return false;

        object converter = GetOrCreateConverter(closedConverterType);

        Type invokerType = typeof(ConverterInvoker<>).MakeGenericType(targetType);
        var invoker = (ConverterInvoker)Activator.CreateInstance(
            invokerType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            args: [converter, closedConverterType],
            culture: null)!;
        candidate = new ConverterCandidate(
            closedConverterType,
            GetTypeDistance(valueType, targetType),
            invoker);
        return true;
    }

    private static object GetOrCreateConverter(Type converterType)
    {
        if (s_converterInstances.TryGetValue(converterType, out object? converter))
            return converter;

        try
        {
            converter = Activator.CreateInstance(converterType, nonPublic: true)
                ?? throw new InvalidOperationException("Activator returned null.");
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(
                $"Serialization converter '{converterType.FullName}' must have a parameterless constructor.",
                exception);
        }

        s_converterInstances.Add(converterType, converter);
        return converter;
    }

    private static bool TryGetConverterTargetPattern(Type converterType, out Type targetPattern)
    {
        for (Type? current = converterType; current is not null; current = current.BaseType)
        {
            if (!current.IsGenericType || current.GetGenericTypeDefinition() != typeof(SerializationConverter<>))
                continue;

            targetPattern = current.GetGenericArguments()[0];
            return true;
        }

        targetPattern = null!;
        return false;
    }

    private static bool TryUnify(Type pattern, Type concrete, Dictionary<Type, Type> bindings)
    {
        if (pattern.IsGenericParameter)
        {
            if (bindings.TryGetValue(pattern, out Type? existing))
                return existing == concrete;
            bindings.Add(pattern, concrete);
            return true;
        }

        if (pattern.IsArray)
        {
            return concrete.IsArray &&
                   pattern.GetArrayRank() == concrete.GetArrayRank() &&
                   TryUnify(pattern.GetElementType()!, concrete.GetElementType()!, bindings);
        }

        if (!pattern.IsGenericType)
            return pattern == concrete;
        if (!concrete.IsGenericType || pattern.GetGenericTypeDefinition() != concrete.GetGenericTypeDefinition())
            return false;

        Type[] patternArguments = pattern.GetGenericArguments();
        Type[] concreteArguments = concrete.GetGenericArguments();
        for (int i = 0; i < patternArguments.Length; i++)
        {
            if (!TryUnify(patternArguments[i], concreteArguments[i], bindings))
                return false;
        }

        return true;
    }

    private static int GetTypeDistance(Type derivedType, Type targetType)
    {
        if (derivedType == targetType)
            return 0;

        var visited = new HashSet<Type> { derivedType };
        var queue = new Queue<(Type type, int distance)>();
        queue.Enqueue((derivedType, 0));
        while (queue.Count > 0)
        {
            (Type current, int distance) = queue.Dequeue();
            IEnumerable<Type> nextTypes = current.BaseType is Type baseType
                ? current.GetInterfaces().Append(baseType)
                : current.GetInterfaces();
            foreach (Type next in nextTypes)
            {
                if (!visited.Add(next))
                    continue;
                if (next == targetType)
                    return distance + 1;
                queue.Enqueue((next, distance + 1));
            }
        }

        return int.MaxValue;
    }

    private sealed record ConverterCandidate(
        Type converterType,
        int distance,
        ConverterInvoker invoker);
}

internal abstract class ConverterInvoker
{
    protected ConverterInvoker(Type targetType, Type converterType)
    {
        this.targetType = targetType;
        this.converterType = converterType;
    }

    internal Type targetType { get; }

    internal Type converterType { get; }

    internal abstract SerializationNode Write(
        SerializationOperation operation,
        string path,
        Type valueType,
        object value);

    internal abstract object Read(
        SerializationOperation operation,
        string path,
        Type valueType,
        ObjectSerializationNode node);

    internal abstract void Restore(
        SerializationOperation operation,
        string path,
        Type valueType,
        ObjectSerializationNode node,
        object target);
}

internal sealed class ConverterInvoker<T> : ConverterInvoker
{
    private readonly SerializationConverter<T> m_converter;

    internal ConverterInvoker(object converter, Type converterType)
        : base(typeof(T), converterType)
    {
        m_converter = (SerializationConverter<T>)converter;
    }

    internal override SerializationNode Write(
        SerializationOperation operation,
        string path,
        Type valueType,
        object value)
    {
        if (value is not T typed)
        {
            throw new InvalidOperationException(
                $"Converter '{converterType.FullName}' cannot write runtime value '{value.GetType().FullName}' at '{path}'.");
        }

        var node = new ObjectSerializationNode();
        m_converter.Write(new SerializationWriter(operation, node, path, valueType), typed);
        return node;
    }

    internal override object Read(
        SerializationOperation operation,
        string path,
        Type valueType,
        ObjectSerializationNode node)
    {
        T result = m_converter.Read(new SerializationReader(operation, node, path, valueType));
        if (result is null)
            throw new InvalidOperationException($"Converter '{converterType.FullName}' returned null at '{path}'.");
        if (!valueType.IsInstanceOfType(result))
        {
            throw new InvalidOperationException(
                $"Converter '{converterType.FullName}' returned '{result.GetType().FullName}', which is incompatible with '{valueType.FullName}' at '{path}'.");
        }
        if (result is ISerializable serializable)
            operation.ScheduleRestoredObject(serializable);
        return result;
    }

    internal override void Restore(
        SerializationOperation operation,
        string path,
        Type valueType,
        ObjectSerializationNode node,
        object target)
    {
        if (target is not T typed)
        {
            throw new InvalidOperationException(
                $"Converter '{converterType.FullName}' cannot restore target '{target.GetType().FullName}' at '{path}'.");
        }

        m_converter.Restore(new SerializationReader(operation, node, path, valueType), typed);
        if (target is ISerializable serializable)
            operation.ScheduleRestoredObject(serializable);
    }
}
