using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Inno.Extensibility.Types;

namespace Inno.Audio.Runtime;

internal sealed class AudioExtensionRegistry : TypeRegistry<AudioExtensionRegistry.Snapshot>
{
    internal AudioExtensionRegistry(TypeCatalog types)
        : base(types)
    {
    }

    internal Snapshot extensions => current;

    /// <summary>
    /// Builds a complete validated audio extension snapshot from one type generation.
    /// </summary>
    /// <param name="types">
    /// Candidate type-cache generation.
    /// </param>
    /// <returns>
    /// A validated audio extension registry snapshot.
    /// </returns>
    protected override Snapshot Build(TypeCacheSnapshot types)
    {
        ArgumentNullException.ThrowIfNull(types);
        return new Snapshot(
            types.version,
            Discover<AudioMixerExtensionAttribute, AudioMixerExtension>(types, static value => value.id, "mixer"),
            Discover<AudioMixerFeatureExtensionAttribute, AudioMixerFeature>(types, static value => value.id, "mixer feature"),
            Discover<AudioContentProviderExtensionAttribute, AudioContentProvider>(types, static value => value.id, "content provider"));
    }

    private static Dictionary<string, Type> Discover<TAttribute, TContract>(
        TypeCacheSnapshot types,
        Func<TAttribute, string> getId,
        string kind)
        where TAttribute : Attribute
    {
        var result = new Dictionary<string, Type>(StringComparer.Ordinal);
        foreach (TypeRef typeRef in types.GetTypesWithAttribute<TAttribute>())
        {
            Type type = typeRef.Resolve(types);
            if (type.IsAbstract || !typeof(TContract).IsAssignableFrom(type))
            {
                throw new InvalidOperationException(
                    $"Audio {kind} '{type.FullName}' must be a non-abstract {typeof(TContract).FullName}.");
            }
            if (type.GetConstructor(
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    binder: null,
                    Type.EmptyTypes,
                    modifiers: null) is null)
            {
                throw new InvalidOperationException($"Audio {kind} '{type.FullName}' requires a parameterless constructor.");
            }
            TAttribute attribute = type.GetCustomAttribute<TAttribute>(inherit: false)!;
            string id = getId(attribute);
            if (!result.TryAdd(id, type))
            {
                throw new InvalidOperationException(
                    $"Audio {kind} ID '{id}' is declared by both '{result[id].FullName}' and '{type.FullName}'.");
            }
        }
        return result;
    }

    internal sealed class Snapshot
    {
        private readonly IReadOnlyDictionary<string, Type> m_features;
        private readonly IReadOnlyDictionary<string, Type> m_mixers;
        private readonly IReadOnlyDictionary<string, Type> m_providers;

        internal Snapshot(
            long typeCacheVersion,
            IReadOnlyDictionary<string, Type> mixers,
            IReadOnlyDictionary<string, Type> features,
            IReadOnlyDictionary<string, Type> providers)
        {
            this.typeCacheVersion = typeCacheVersion;
            m_mixers = mixers;
            m_features = features;
            m_providers = providers;
        }

        internal long typeCacheVersion { get; }

        internal ProviderGeneration CreateProviders()
        {
            var entries = new List<ProviderEntry>(m_providers.Count);
            try
            {
                foreach ((string id, Type type) in m_providers)
                {
                    AudioContentProviderExtensionAttribute attribute =
                        type.GetCustomAttribute<AudioContentProviderExtensionAttribute>(inherit: false)!;
                    entries.Add(new ProviderEntry(id, attribute.priority, Create<AudioContentProvider>(type)));
                }
                entries.Sort(static (left, right) =>
                {
                    int priority = left.priority.CompareTo(right.priority);
                    return priority != 0 ? priority : string.CompareOrdinal(left.id, right.id);
                });
                return new ProviderGeneration(typeCacheVersion, entries);
            }
            catch
            {
                foreach (ProviderEntry entry in entries)
                    entry.provider.Dispose();
                throw;
            }
        }

        internal bool TryBuildMixer(AudioMixerAsset asset, out AudioMixer? mixer)
        {
            ArgumentNullException.ThrowIfNull(asset);
            var builder = new AudioMixerBuilder();
            if (!string.IsNullOrWhiteSpace(asset.mixerTypeId))
            {
                if (!m_mixers.TryGetValue(asset.mixerTypeId, out Type? mixerType))
                {
                    mixer = null;
                    return false;
                }
                Create<AudioMixerExtension>(mixerType).Build(builder, asset.mixerState);
            }

            var configured = new HashSet<string>(StringComparer.Ordinal);
            foreach (AudioMixerFeatureConfiguration configuration in asset.features)
            {
                if (!configuration.enabled)
                    continue;
                if (string.IsNullOrWhiteSpace(configuration.featureTypeId) || !configured.Add(configuration.featureTypeId))
                    throw new InvalidOperationException("Enabled audio mixer features require unique stable identifiers.");
                if (!m_features.TryGetValue(configuration.featureTypeId, out Type? featureType))
                {
                    mixer = null;
                    return false;
                }
                Create<AudioMixerFeature>(featureType).Build(builder, configuration.state);
            }
            mixer = builder.Build();
            return true;
        }

        private static TContract Create<TContract>(Type type) where TContract : class
        {
            try
            {
                return (TContract)(Activator.CreateInstance(type, nonPublic: true)
                    ?? throw new InvalidOperationException("Activator returned null."));
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException($"Audio extension '{type.FullName}' could not be activated.", exception);
            }
        }
    }

    internal sealed class ProviderGeneration : IDisposable
    {
        private bool m_disposed;

        internal ProviderGeneration(long typeCacheVersion, IReadOnlyList<ProviderEntry> providers)
        {
            this.typeCacheVersion = typeCacheVersion;
            this.providers = providers;
        }

        internal long typeCacheVersion { get; }

        internal IReadOnlyList<ProviderEntry> providers { get; }

        /// <summary>
        /// Releases every provider instance retained by this candidate generation.
        /// </summary>
        public void Dispose()
        {
            if (m_disposed)
                return;
            m_disposed = true;
            foreach (ProviderEntry entry in providers)
                entry.provider.Dispose();
        }
    }

    internal sealed record ProviderEntry(string id, int priority, AudioContentProvider provider);
}
