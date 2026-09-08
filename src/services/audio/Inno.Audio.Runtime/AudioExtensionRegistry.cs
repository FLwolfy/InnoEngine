using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Inno.Core.Execution;
using Inno.Extensibility.Types;

namespace Inno.Audio.Runtime;

internal sealed class AudioExtensionRegistry : TypeRegistry<AudioExtensionRegistry.Snapshot>
{
    private Exception? m_retirementFailure;

    internal AudioExtensionRegistry(TypeCatalog types)
        : base(types)
    {
    }

    internal Snapshot extensions => current;

    internal void RetireBackend(Action retire)
    {
        try { RetireResource("audio device generation", retire); }
        catch (Exception exception)
        {
            Fault(exception);
            throw;
        }
    }

    internal void Fault(Exception exception)
    {
        m_retirementFailure ??= exception;
        ReportRetirementFailure(exception);
    }

    internal void EnsureHealthy()
    {
        if (m_retirementFailure is not null)
            throw new InvalidOperationException("The audio generation owner is Faulted and requires a host restart.", m_retirementFailure);
    }

    /// <summary>
    /// Revokes audio admission when candidate or active registry cleanup faults the shared generation.
    /// </summary>
    /// <param name="phase">
    /// The lifecycle phase that could not retire safely.
    /// </param>
    /// <param name="exception">
    /// The terminal cleanup failure already reported to the shared gate.
    /// </param>
    protected override void OnCleanupFailed(string phase, Exception exception)
    {
        m_retirementFailure ??= exception;
    }

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
            Discover<AudioContentProviderExtensionAttribute, AudioContentProvider>(types, static value => value.id, "content provider"),
            CreateExtension<AudioContentProvider>);
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

    internal sealed class Snapshot : IDisposable
    {
        private readonly IReadOnlyDictionary<string, Type> m_features;
        private readonly IReadOnlyDictionary<string, Type> m_mixers;
        private readonly IReadOnlyDictionary<string, Type> m_providers;

        internal Snapshot(
            long typeCacheVersion,
            IReadOnlyDictionary<string, Type> mixers,
            IReadOnlyDictionary<string, Type> features,
            IReadOnlyDictionary<string, Type> providers,
            Func<Type, AudioContentProvider> createProvider)
        {
            this.typeCacheVersion = typeCacheVersion;
            m_mixers = mixers;
            m_features = features;
            m_providers = providers;
            this.providers = CreateProviders(createProvider);
        }

        internal long typeCacheVersion { get; }

        internal ProviderGeneration providers { get; }

        /// <summary>
        /// Retires the provider instances with their owning type-catalog snapshot.
        /// </summary>
        public void Dispose() => providers.Dispose();

        private ProviderGeneration CreateProviders(Func<Type, AudioContentProvider> createProvider)
        {
            var entries = new List<ProviderEntry>(m_providers.Count);
            foreach ((string id, Type type) in m_providers)
            {
                AudioContentProviderExtensionAttribute attribute =
                    type.GetCustomAttribute<AudioContentProviderExtensionAttribute>(inherit: false)!;
                entries.Add(new ProviderEntry(id, attribute.priority, createProvider(type)));
            }
            entries.Sort(static (left, right) =>
            {
                int priority = left.priority.CompareTo(right.priority);
                return priority != 0 ? priority : string.CompareOrdinal(left.id, right.id);
            });
            return new ProviderGeneration(typeCacheVersion, entries);
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
        private readonly LifetimeScope m_lifetime = new();

        internal ProviderGeneration(long typeCacheVersion, IReadOnlyList<ProviderEntry> providers)
        {
            this.typeCacheVersion = typeCacheVersion;
            this.providers = Array.AsReadOnly(providers.ToArray());
            foreach (ProviderEntry entry in providers)
                m_lifetime.Own(entry.provider);
        }

        internal long typeCacheVersion { get; }

        internal IReadOnlyList<ProviderEntry> providers { get; private set; }

        /// <summary>
        /// Releases every provider instance retained by this candidate generation.
        /// </summary>
        public void Dispose()
        {
            if (m_disposed)
                return;
            try { m_lifetime.Dispose(); }
            catch (Exception pendingRetirement) when (RetirementPendingException.Find(pendingRetirement) is not null) { throw; }
            catch
            {
                providers = Array.Empty<ProviderEntry>();
                m_disposed = true;
                throw;
            }
            providers = Array.Empty<ProviderEntry>();
            m_disposed = true;
        }
    }

    internal sealed record ProviderEntry(string id, int priority, AudioContentProvider provider);
}
