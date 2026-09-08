using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Inno.Core.Execution;
using Inno.Extensibility.Types;

namespace Inno.Rendering.Runtime;

internal sealed class RenderExtensionRegistry : TypeRegistry<RenderExtensionRegistry.Snapshot>
{
    private Exception? m_retirementFailure;

    internal RenderExtensionRegistry(TypeCatalog types)
        : base(types)
    {
    }

    internal Snapshot extensions => current;

    internal void Retire(IDisposable? resource)
    {
        try
        {
            if (resource is not null)
                RetireResource("render generation", resource.Dispose);
        }
        catch (Exception exception)
        {
            m_retirementFailure ??= exception;
            ReportRetirementFailure(exception);
            throw;
        }
    }

    internal void EnsureHealthy()
    {
        if (m_retirementFailure is not null)
            throw new InvalidOperationException("The rendering generation owner is Faulted and requires a host restart.", m_retirementFailure);
    }

    /// <summary>
    /// Revokes rendering admission after the shared registry reports a terminal cleanup failure.
    /// </summary>
    /// <param name="phase">
    /// The registry lifecycle phase that failed.
    /// </param>
    /// <param name="exception">
    /// The failure already recorded by the shared generation gate.
    /// </param>
    protected override void OnCleanupFailed(string phase, Exception exception)
    {
        m_retirementFailure ??= exception;
    }

    /// <summary>
    /// Builds a validated result from the current immutable input snapshot.
    /// </summary>
    /// <param name="types">
    /// The active type catalog generation used for extension resolution.
    /// </param>
    /// <returns>
    /// The validated snapshot that represents the completed operation.
    /// </returns>
    protected override Snapshot Build(TypeCacheSnapshot types)
    {
        ArgumentNullException.ThrowIfNull(types);
        Dictionary<string, Type> pipelines = Discover<RenderPipelineExtensionAttribute, RenderPipeline>(
            types,
            static attribute => attribute.id,
            "render pipeline");
        Dictionary<string, Type> features = Discover<RenderFeatureExtensionAttribute, RenderPipelineFeature>(
            types,
            static attribute => attribute.id,
            "render feature");
        Dictionary<string, Type> requestProviders = Discover<
            RenderRequestProviderExtensionAttribute,
            RenderRequestProvider>(
            types,
            static attribute => attribute.id,
            "render request provider");
        return new Snapshot(types.version, pipelines, features, requestProviders, CreateExtension<RenderRequestProvider>, Retire);
    }

    internal static string GetConfigurationFingerprint(RenderPipelineAsset asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        using var stream = new System.IO.MemoryStream();
        using var writer = new System.IO.BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(asset.pipelineTypeId ?? string.Empty);
        WriteState(writer, asset.pipelineState);
        foreach (RenderFeatureConfiguration feature in asset.features)
        {
            writer.Write(feature.featureTypeId ?? string.Empty);
            writer.Write(feature.enabled);
            WriteState(writer, feature.state);
        }
        writer.Flush();
        return Convert.ToHexString(SHA256.HashData(stream.GetBuffer().AsSpan(0, checked((int)stream.Length))));
    }

    private static void WriteState(System.IO.BinaryWriter writer, SerializedRenderExtensionState state)
    {
        writer.Write(state.stableTypeId.ToByteArray());
        byte[] data = state.propertyData ?? [];
        writer.Write(data.Length);
        writer.Write(data);
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
                    $"Reloadable {kind} '{type.FullName}' must be a non-abstract {typeof(TContract).FullName}.");
            }

            if (type.GetConstructor(
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    binder: null,
                    Type.EmptyTypes,
                    modifiers: null) is null)
            {
                throw new InvalidOperationException(
                    $"Reloadable {kind} '{type.FullName}' requires a parameterless constructor.");
            }

            TAttribute attribute = type.GetCustomAttribute<TAttribute>(inherit: false)!;
            string id = getId(attribute);
            if (!result.TryAdd(id, type))
            {
                throw new InvalidOperationException(
                    $"Reloadable {kind} ID '{id}' is declared by both " +
                    $"'{result[id].FullName}' and '{type.FullName}'.");
            }
        }

        return result;
    }

    internal sealed class Snapshot : IDisposable
    {
        private readonly IReadOnlyDictionary<string, Type> m_pipelines;
        private readonly IReadOnlyDictionary<string, Type> m_features;
        private readonly IReadOnlyDictionary<string, Type> m_requestProviders;
        private readonly Action<IDisposable> m_retire;

        internal Snapshot(
            long typeCacheVersion,
            IReadOnlyDictionary<string, Type> pipelines,
            IReadOnlyDictionary<string, Type> features,
            IReadOnlyDictionary<string, Type> requestProviders,
            Func<Type, RenderRequestProvider> createProvider,
            Action<IDisposable> retire)
        {
            this.typeCacheVersion = typeCacheVersion;
            m_pipelines = pipelines;
            m_features = features;
            m_requestProviders = requestProviders;
            m_retire = retire;
            providers = CreateRequestProviders(createProvider);
        }

        internal long typeCacheVersion { get; }

        internal RequestProviderGeneration providers { get; }

        /// <summary>
        /// Releases providers when their owning type snapshot retires.
        /// </summary>
        public void Dispose() => providers.Dispose();

        private RequestProviderGeneration CreateRequestProviders(Func<Type, RenderRequestProvider> createProvider)
        {
            var providers = new List<RequestProviderEntry>(m_requestProviders.Count);
            foreach ((string id, Type type) in m_requestProviders)
            {
                RenderRequestProviderExtensionAttribute attribute =
                    type.GetCustomAttribute<RenderRequestProviderExtensionAttribute>(inherit: false)!;
                providers.Add(new RequestProviderEntry(id, attribute.priority, createProvider(type)));
            }
            providers.Sort(static (left, right) =>
            {
                int priority = left.priority.CompareTo(right.priority);
                return priority != 0 ? priority : string.CompareOrdinal(left.id, right.id);
            });
            return new RequestProviderGeneration(typeCacheVersion, providers);
        }

        internal bool TryCreateGeneration(
            RenderPipelineAsset asset,
            out RenderPipelineGeneration? generation)
        {
            ArgumentNullException.ThrowIfNull(asset);
            if (string.IsNullOrWhiteSpace(asset.pipelineTypeId))
                throw new InvalidOperationException("A render pipeline asset requires a stable pipeline extension ID.");
            if (!m_pipelines.TryGetValue(asset.pipelineTypeId, out Type? pipelineType))
            {
                generation = null;
                return false;
            }

            var featureTypes = new Dictionary<string, Type>(StringComparer.Ordinal);
            foreach (RenderFeatureConfiguration configuration in asset.features)
            {
                if (!configuration.enabled)
                    continue;
                if (string.IsNullOrWhiteSpace(configuration.featureTypeId))
                    throw new InvalidOperationException("An enabled render feature requires a stable extension ID.");
                if (featureTypes.ContainsKey(configuration.featureTypeId))
                {
                    throw new InvalidOperationException(
                        $"Render feature '{configuration.featureTypeId}' is configured more than once.");
                }
                if (!m_features.TryGetValue(configuration.featureTypeId, out Type? featureType))
                {
                    generation = null;
                    return false;
                }
                featureTypes.Add(configuration.featureTypeId, featureType);
            }

            var candidate = new RenderPipelineGeneration(typeCacheVersion, Create<RenderPipeline>(pipelineType));
            try
            {
                candidate.pipeline.Configure(asset.pipelineState);
                foreach (RenderFeatureConfiguration configuration in asset.features.Where(
                             static value => value.enabled))
                {
                    RenderPipelineFeature feature = Create<RenderPipelineFeature>(
                        featureTypes[configuration.featureTypeId]);
                    candidate.AddFeature(configuration.featureTypeId, feature);
                    feature.Configure(configuration);
                }

                generation = candidate;
                return true;
            }
            catch (Exception failure)
            {
                try { m_retire(candidate); }
                catch (Exception pendingRetirement) when (RetirementPendingException.Find(pendingRetirement) is not null) { throw; }
                catch (Exception cleanup)
                {
                    throw new AggregateException("Render generation preparation and retirement failed.", failure, cleanup);
                }
                throw;
            }
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
                throw new InvalidOperationException(
                    $"Rendering extension '{type.FullName}' could not be activated.",
                    exception);
            }
        }
    }

    internal sealed class RequestProviderGeneration : IDisposable
    {
        private IReadOnlyList<RequestProviderEntry> m_providers;
        private readonly LifetimeScope m_lifetime = new();

        internal RequestProviderGeneration(
            long typeCacheVersion,
            IReadOnlyList<RequestProviderEntry> providers)
        {
            this.typeCacheVersion = typeCacheVersion;
            m_providers = Array.AsReadOnly(providers.ToArray());
            foreach (RequestProviderEntry entry in m_providers)
                m_lifetime.Own(entry.provider);
        }

        internal long typeCacheVersion { get; }

        internal IReadOnlyList<RequestProviderEntry> providers => m_providers;

        /// <summary>
        /// Releases the resources owned by this instance.
        /// </summary>
        public void Dispose()
        {
            try { m_lifetime.Dispose(); }
            catch (Exception pendingRetirement) when (RetirementPendingException.Find(pendingRetirement) is not null) { throw; }
            catch
            {
                m_providers = Array.Empty<RequestProviderEntry>();
                throw;
            }
            m_providers = Array.Empty<RequestProviderEntry>();
        }
    }

    internal sealed record RequestProviderEntry(
        string id,
        int priority,
        RenderRequestProvider provider);

}
