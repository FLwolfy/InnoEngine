using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace Inno.Core.Reflection;

/// <summary>
/// Instance-based registry that maps runtime <see cref="Type"/> values to stable and runtime ids.
/// </summary>
internal sealed class TypeIdentityRegistry
{
    // RFC 4122 DNS namespace UUID, used to build deterministic UUIDv5 for auto stable ids.
    private static readonly Guid C_STABLE_NAMESPACE_GUID = Guid.Parse("6ba7b810-9dad-11d1-80b4-00c04fd430c8");
    private const string C_GENERATED_STABLE_TYPE_METADATA_KEY = "Inno.StableTypeId";
    private static int S_NEXT_RUNTIME_TYPE_ID;

    private readonly Lock m_sync = new();
    private Dictionary<Type, Guid> m_stableByType = [];
    private Dictionary<Guid, Type> m_typeByStable = [];
    private Dictionary<Type, int> m_runtimeByType = [];
    private Dictionary<int, Type> m_typeByRuntime = [];
    private int m_version;

    public int version
    {
        get
        {
            lock (m_sync)
            {
                return m_version;
            }
        }
    }

    public int stableCount
    {
        get
        {
            lock (m_sync)
            {
                return m_stableByType.Count;
            }
        }
    }

    public void Rebuild(IEnumerable<Type> types, TypeIdentityRegistry? previous)
    {
        ArgumentNullException.ThrowIfNull(types);

        Type[] sourceTypes = types
            .Where(static t => t is not null)
            .Distinct()
            .ToArray();

        var stableByType = new Dictionary<Type, Guid>();
        var typeByStable = new Dictionary<Guid, Type>();
        IReadOnlyDictionary<Assembly, IReadOnlyDictionary<string, GeneratedStableTypeMapping>> mappings =
            BuildGeneratedMappings(sourceTypes);

        foreach (Type type in sourceTypes)
        {
            StableTypeIdAttribute? attr = type.GetCustomAttribute<StableTypeIdAttribute>(inherit: false);
            GeneratedStableTypeMapping? mapping = null;
            Guid stableId;
            if (attr is not null)
            {
                if (!Guid.TryParse(attr.id, out stableId))
                {
                    throw new InvalidOperationException(
                        $"Type '{type.FullName}' has invalid StableTypeId '{attr.id}'.");
                }
            }
            else if (mappings.TryGetValue(type.Assembly, out IReadOnlyDictionary<string, GeneratedStableTypeMapping>? assemblyMappings) &&
                     assemblyMappings.TryGetValue(type.FullName ?? type.Name, out mapping))
            {
                if (!Guid.TryParse(mapping.id, out stableId))
                {
                    throw new InvalidOperationException(
                        $"Type '{type.FullName}' has invalid generated StableTypeId '{mapping.id}'.");
                }
            }
            else
            {
                stableId = CreateDeterministicStableId(type);
            }

            if (!typeByStable.TryAdd(stableId, type))
            {
                Type existing = typeByStable[stableId];
                throw new InvalidOperationException(
                    $"StableTypeId '{stableId}' conflicts between '{existing.FullName}' and '{type.FullName}'.");
            }

            stableByType[type] = stableId;
        }

        var orderedTypes = sourceTypes
            .OrderBy(t => t.Assembly.GetName().Name, StringComparer.Ordinal)
            .ThenBy(t => t.FullName, StringComparer.Ordinal)
            .ToArray();

        Dictionary<Type, int> previousRuntimeByType = [];
        if (previous is not null)
        {
            lock (previous.m_sync)
                previousRuntimeByType = new Dictionary<Type, int>(previous.m_runtimeByType);
        }

        var runtimeByType = new Dictionary<Type, int>(orderedTypes.Length);
        var typeByRuntime = new Dictionary<int, Type>(orderedTypes.Length);
        for (int i = 0; i < orderedTypes.Length; i++)
        {
            Type type = orderedTypes[i];
            int runtimeId;
            if (!previousRuntimeByType.TryGetValue(type, out runtimeId))
                runtimeId = AllocateRuntimeTypeId();

            runtimeByType[type] = runtimeId;
            typeByRuntime[runtimeId] = type;
        }

        lock (m_sync)
        {
            m_stableByType = stableByType;
            m_typeByStable = typeByStable;
            m_runtimeByType = runtimeByType;
            m_typeByRuntime = typeByRuntime;
            m_version = previous?.version + 1 ?? 1;
        }
    }

    public bool TryGetStableTypeId(Type type, out Guid stableTypeId)
    {
        ArgumentNullException.ThrowIfNull(type);

        lock (m_sync)
        {
            return m_stableByType.TryGetValue(type, out stableTypeId);
        }
    }

    public bool TryGetTypeRef(Type type, out TypeRef typeRef)
    {
        ArgumentNullException.ThrowIfNull(type);

        lock (m_sync)
        {
            if (m_stableByType.TryGetValue(type, out Guid stableId) &&
                m_runtimeByType.TryGetValue(type, out int runtimeId))
            {
                typeRef = new TypeRef(stableId, runtimeId);
                return true;
            }
        }

        typeRef = default;
        return false;
    }

    public TypeRef GetTypeRef(Type type)
    {
        if (TryGetTypeRef(type, out TypeRef typeRef))
            return typeRef;
        throw new InvalidOperationException($"Type '{type.FullName}' does not belong to this type-cache snapshot.");
    }

    public bool TryResolveType(Guid stableTypeId, out Type? type)
    {
        lock (m_sync)
        {
            if (m_typeByStable.TryGetValue(stableTypeId, out Type? resolved))
            {
                type = resolved;
                return true;
            }
        }

        type = null;
        return false;
    }

    public bool TryGetRuntimeTypeId(Type type, out int runtimeTypeId)
    {
        ArgumentNullException.ThrowIfNull(type);

        lock (m_sync)
        {
            return m_runtimeByType.TryGetValue(type, out runtimeTypeId);
        }
    }

    public int GetOrAddRuntimeTypeId(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);

        lock (m_sync)
        {
            if (m_runtimeByType.TryGetValue(type, out int existing))
            {
                return existing;
            }

            int runtimeId = AllocateRuntimeTypeId();
            m_runtimeByType[type] = runtimeId;
            m_typeByRuntime[runtimeId] = type;
            return runtimeId;
        }
    }

    public bool TryResolveRuntimeType(int runtimeTypeId, out Type? type)
    {
        lock (m_sync)
        {
            if (m_typeByRuntime.TryGetValue(runtimeTypeId, out Type? resolved))
            {
                type = resolved;
                return true;
            }
        }

        type = null;
        return false;
    }

    public bool TryResolveType(TypeRef typeRef, out Type? type)
    {
        lock (m_sync)
        {
            if (typeRef.stableId == Guid.Empty)
            {
                type = null;
                return false;
            }
            if (typeRef.runtimeId > 0 &&
                m_typeByRuntime.TryGetValue(typeRef.runtimeId, out Type? runtimeType) &&
                m_stableByType.TryGetValue(runtimeType, out Guid runtimeStableId) &&
                runtimeStableId == typeRef.stableId)
            {
                type = runtimeType;
                return true;
            }
            if (m_typeByStable.TryGetValue(typeRef.stableId, out Type? stableType))
            {
                type = stableType;
                return true;
            }
        }

        type = null;
        return false;
    }

    private static int AllocateRuntimeTypeId()
    {
        int runtimeId = Interlocked.Increment(ref S_NEXT_RUNTIME_TYPE_ID);
        if (runtimeId <= 0)
            throw new InvalidOperationException("The process-wide runtime type identity space is exhausted.");
        return runtimeId;
    }

    public IReadOnlyDictionary<string, Guid> GetStableTypeMapSnapshot()
    {
        lock (m_sync)
        {
            var snapshot = new SortedDictionary<string, Guid>(StringComparer.Ordinal);
            foreach ((Type type, Guid stableId) in m_stableByType)
            {
                string typeKey = GetTypeLockKey(type);
                snapshot[typeKey] = stableId;
            }

            return snapshot;
        }
    }

    public static string GetTypeLockKey(Type type)
    {
        string assemblyName = type.Assembly.GetName().Name ?? "UnknownAssembly";
        string typeName = type.FullName ?? type.Name;
        return $"{assemblyName}:{typeName}";
    }

    private static Guid CreateDeterministicStableId(Type type)
    {
        string key = GetTypeLockKey(type);
        return CreateGuidV5(C_STABLE_NAMESPACE_GUID, key);
    }

    private static IReadOnlyDictionary<Assembly, IReadOnlyDictionary<string, GeneratedStableTypeMapping>>
        BuildGeneratedMappings(IReadOnlyList<Type> sourceTypes)
    {
        var result = new Dictionary<Assembly, IReadOnlyDictionary<string, GeneratedStableTypeMapping>>();
        foreach (IGrouping<Assembly, Type> group in sourceTypes.GroupBy(static type => type.Assembly))
        {
            AssemblyMetadataAttribute[] attributes = group.Key
                .GetCustomAttributes<AssemblyMetadataAttribute>()
                .Where(static attribute => string.Equals(
                    attribute.Key,
                    C_GENERATED_STABLE_TYPE_METADATA_KEY,
                    StringComparison.Ordinal))
                .ToArray();
            if (attributes.Length == 0)
                continue;

            var availableTypes = group.ToDictionary(
                static type => type.FullName ?? type.Name,
                StringComparer.Ordinal);
            var mappings = new Dictionary<string, GeneratedStableTypeMapping>(StringComparer.Ordinal);
            foreach (AssemblyMetadataAttribute attribute in attributes)
            {
                GeneratedStableTypeMapping mapping = ParseGeneratedMapping(
                    group.Key.GetName().Name ?? string.Empty,
                    attribute.Value);
                if (string.IsNullOrWhiteSpace(mapping.typeName))
                    throw new InvalidOperationException("A generated StableTypeId mapping requires a type name.");
                if (!availableTypes.ContainsKey(mapping.typeName))
                {
                    throw new InvalidOperationException(
                        $"Generated StableTypeId mapping refers to missing type '{mapping.typeName}' " +
                        $"in assembly '{group.Key.GetName().Name}'.");
                }
                if (!mappings.TryAdd(mapping.typeName, mapping))
                {
                    throw new InvalidOperationException(
                        $"Type '{mapping.typeName}' has more than one generated StableTypeId mapping.");
                }
            }
            result.Add(group.Key, mappings);
        }
        return result;
    }

    private static GeneratedStableTypeMapping ParseGeneratedMapping(
        string assemblyName,
        string? value)
    {
        string[] parts = (value ?? string.Empty).Split('|', 2, StringSplitOptions.None);
        if (parts.Length != 2)
        {
            throw new InvalidOperationException(
                $"Assembly '{assemblyName}' has invalid generated StableTypeId metadata '{value}'.");
        }
        return new GeneratedStableTypeMapping(parts[1], parts[0]);
    }

    private static Guid CreateGuidV5(Guid namespaceId, string name)
    {
        byte[] ns = namespaceId.ToByteArray();
        SwapGuidByteOrder(ns);

        byte[] nameBytes = Encoding.UTF8.GetBytes(name);
        byte[] data = new byte[ns.Length + nameBytes.Length];
        Buffer.BlockCopy(ns, 0, data, 0, ns.Length);
        Buffer.BlockCopy(nameBytes, 0, data, ns.Length, nameBytes.Length);

        byte[] hash = SHA1.HashData(data);
        Span<byte> bytes = stackalloc byte[16];
        hash.AsSpan(0, 16).CopyTo(bytes);

        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x50);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);

        byte[] guidBytes = bytes.ToArray();
        SwapGuidByteOrder(guidBytes);
        return new Guid(guidBytes);
    }

    private static void SwapGuidByteOrder(byte[] guidBytes)
    {
        (guidBytes[0], guidBytes[3]) = (guidBytes[3], guidBytes[0]);
        (guidBytes[1], guidBytes[2]) = (guidBytes[2], guidBytes[1]);
        (guidBytes[4], guidBytes[5]) = (guidBytes[5], guidBytes[4]);
        (guidBytes[6], guidBytes[7]) = (guidBytes[7], guidBytes[6]);
    }

    private sealed record GeneratedStableTypeMapping(
        string typeName,
        string id);
}
