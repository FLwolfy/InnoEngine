using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;

using Inno.Extensibility.Types;
using Inno.Core.Serialization.Converters;

namespace Inno.Core.Serialization;

internal sealed class ConverterRegistry : IDisposable
{
    private readonly ConverterTypeRegistry m_registry;
    private bool m_disposed;

    internal ConverterRegistry(TypeCatalog types)
    {
        ArgumentNullException.ThrowIfNull(types);
        m_registry = new ConverterTypeRegistry(types);
        m_registry.Refresh();
    }

    internal void Refresh() => RequireRegistry().Refresh();

    /// <summary>
    /// Releases the resources owned by this instance.
    /// </summary>
    public void Dispose()
    {
        if (m_disposed)
            return;
        m_disposed = true;
        m_registry.Dispose();
    }

    internal ConverterInvoker? Resolve(Type valueType)
    {
        using ConverterRegistryLease lease = Capture();
        return lease.Resolve(valueType);
    }

    internal ConverterRegistryLease Capture() => RequireRegistry().Capture();

    internal static ConverterInvoker? ResolveSnapshot(ConverterRegistrySnapshot snapshot, Type valueType)
    {
        var candidates = new List<ConverterCandidate>();
        for (int i = 0; i < snapshot.registrations.Count; i++)
        {
            if (TryCreateCandidate(snapshot, snapshot.registrations[i], valueType, out ConverterCandidate? candidate))
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
        ConverterRegistrySnapshot snapshot,
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

        object converter = GetOrCreateConverter(snapshot, closedConverterType);

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

    private static object GetOrCreateConverter(ConverterRegistrySnapshot snapshot, Type converterType)
    {
        if (snapshot.converterInstances.TryGetValue(converterType, out object? converter))
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

        snapshot.converterInstances.Add(converterType, converter);
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

    private sealed class ConverterTypeRegistry : TypeRegistry<ConverterRegistrySnapshot>
    {
        internal ConverterTypeRegistry(TypeCatalog types)
            : base(types)
        {
        }

        internal ConverterRegistryLease Capture()
        {
            while (true)
            {
                ConverterRegistrySnapshot snapshot = current;
                if (snapshot.TryAcquire())
                    return new ConverterRegistryLease(snapshot);
            }
        }

        internal ConverterInvoker? Resolve(Type valueType)
        {
            ConverterRegistrySnapshot snapshot = current;
            lock (snapshot.sync)
            {
                if (snapshot.cache.TryGetValue(valueType, out ConverterInvoker? cached))
                    return cached;
                ConverterInvoker? resolved = ResolveSnapshot(snapshot, valueType);
                snapshot.cache.Add(valueType, resolved);
                return resolved;
            }
        }

        /// <summary>
        /// Builds a validated result from the current immutable input snapshot.
        /// </summary>
        /// <param name="types">
        /// The active type catalog generation used for extension resolution.
        /// </param>
        /// <returns>
        /// The validated converter registry snapshot that represents the completed operation.
        /// </returns>
        protected override ConverterRegistrySnapshot Build(TypeCacheSnapshot types)
        {
            IReadOnlyList<Type> registrations = types
                .GetTypesWithAttribute<SerializationExtensionAttribute>()
                .Select(typeRef => typeRef.Resolve(types))
                .OrderBy(static type => type.FullName, StringComparer.Ordinal)
                .ToArray();
            for (int i = 0; i < registrations.Count; i++)
            {
                if (!TryGetConverterTargetPattern(registrations[i], out _))
                {
                    throw new InvalidOperationException(
                        $"Serialization extension '{registrations[i].FullName}' must inherit SerializationConverter<T>.");
                }
            }

            return new ConverterRegistrySnapshot(registrations);
        }

        /// <summary>
        /// Releases the generation lease retained by an immutable registry snapshot.
        /// </summary>
        /// <param name="snapshot">
        /// The immutable state snapshot consumed by this operation.
        /// </param>
        protected override void DisposeSnapshot(ConverterRegistrySnapshot snapshot)
            => snapshot.Release();
    }

    private ConverterTypeRegistry RequireRegistry()
        => !m_disposed
            ? m_registry
            : throw new ObjectDisposedException(nameof(ConverterRegistry));

    internal sealed class ConverterRegistrySnapshot(IReadOnlyList<Type> registrations)
    {
        internal readonly object sync = new();
        internal readonly Dictionary<Type, ConverterInvoker?> cache = [];
        internal readonly Dictionary<Type, object> converterInstances = [];
        internal IReadOnlyList<Type> registrations = registrations;
        private int m_referenceCount = 1;

        internal bool TryAcquire()
        {
            lock (sync)
            {
                if (m_referenceCount == 0)
                    return false;
                m_referenceCount++;
                return true;
            }
        }

        internal void Release()
        {
            bool dispose;
            lock (sync)
            {
                if (m_referenceCount <= 0)
                    throw new InvalidOperationException("The serialization converter generation was released twice.");
                m_referenceCount--;
                dispose = m_referenceCount == 0;
            }
            if (!dispose)
                return;

            foreach (object converter in converterInstances.Values.Distinct(ReferenceEqualityComparer.Instance))
            {
                if (converter is IDisposable disposable)
                {
                    try
                    {
                        disposable.Dispose();
                    }
                    catch (Exception exception)
                    {
                        Trace.TraceError(
                            "Serialization converter '{0}' failed while being disposed: {1}",
                            converter.GetType().FullName,
                            exception);
                    }
                }
            }
            lock (sync)
            {
                cache.Clear();
                converterInstances.Clear();
                registrations = Array.Empty<Type>();
            }
        }
    }
}

internal sealed class ConverterRegistryLease : IDisposable
{
    private ConverterRegistry.ConverterRegistrySnapshot? m_snapshot;

    internal ConverterRegistryLease(ConverterRegistry.ConverterRegistrySnapshot snapshot)
    {
        m_snapshot = snapshot;
    }

    internal ConverterInvoker? Resolve(Type valueType)
    {
        ArgumentNullException.ThrowIfNull(valueType);
        ConverterRegistry.ConverterRegistrySnapshot snapshot = m_snapshot
            ?? throw new ObjectDisposedException(nameof(ConverterRegistryLease));
        Type normalizedType = Nullable.GetUnderlyingType(valueType) ?? valueType;
        lock (snapshot.sync)
        {
            if (snapshot.cache.TryGetValue(normalizedType, out ConverterInvoker? cached))
                return cached;
            ConverterInvoker? resolved = ConverterRegistry.ResolveSnapshot(snapshot, normalizedType);
            snapshot.cache.Add(normalizedType, resolved);
            return resolved;
        }
    }

    /// <summary>
    /// Releases the resources owned by this instance.
    /// </summary>
    public void Dispose()
    {
        ConverterRegistry.ConverterRegistrySnapshot? snapshot = Interlocked.Exchange(ref m_snapshot, null);
        snapshot?.Release();
    }

}

internal abstract class ConverterInvoker
{
    /// <summary>
    /// Creates a validated converter invoker instance.
    /// </summary>
    /// <param name="targetType">
    /// The target type consumed by converter invoker; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <param name="converterType">
    /// The converter type consumed by converter invoker; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
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
