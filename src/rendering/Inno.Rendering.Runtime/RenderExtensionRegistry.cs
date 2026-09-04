using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Inno.Extensibility.Types;

namespace Inno.Rendering.Runtime;

internal sealed class RenderExtensionRegistry : TypeRegistry<RenderExtensionRegistry.Snapshot>
{
    internal RenderExtensionRegistry(TypeCatalog types)
        : base(types)
    {
    }

    internal Snapshot extensions => current;

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
        return new Snapshot(types.version, pipelines, features, requestProviders);
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

    internal static void DisposeFeatures(IEnumerable<RenderPipelineFeature> features)
    {
        foreach (RenderPipelineFeature feature in features)
        {
            if (feature is IDisposable disposable)
                disposable.Dispose();
        }
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

    internal sealed class Snapshot
    {
        private readonly IReadOnlyDictionary<string, Type> m_pipelines;
        private readonly IReadOnlyDictionary<string, Type> m_features;
        private readonly IReadOnlyDictionary<string, Type> m_requestProviders;

        internal Snapshot(
            long typeCacheVersion,
            IReadOnlyDictionary<string, Type> pipelines,
            IReadOnlyDictionary<string, Type> features,
            IReadOnlyDictionary<string, Type> requestProviders)
        {
            this.typeCacheVersion = typeCacheVersion;
            m_pipelines = pipelines;
            m_features = features;
            m_requestProviders = requestProviders;
        }

        internal long typeCacheVersion { get; }

        internal RequestProviderGeneration CreateRequestProviders()
        {
            var providers = new List<RequestProviderEntry>(m_requestProviders.Count);
            try
            {
                foreach ((string id, Type type) in m_requestProviders)
                {
                    RenderRequestProviderExtensionAttribute attribute =
                        type.GetCustomAttribute<RenderRequestProviderExtensionAttribute>(inherit: false)!;
                    providers.Add(new RequestProviderEntry(
                        id,
                        attribute.priority,
                        Create<RenderRequestProvider>(type)));
                }

                providers.Sort(static (left, right) =>
                {
                    int priority = left.priority.CompareTo(right.priority);
                    return priority != 0
                        ? priority
                        : string.CompareOrdinal(left.id, right.id);
                });
                return new RequestProviderGeneration(typeCacheVersion, providers);
            }
            catch
            {
                foreach (RequestProviderEntry entry in providers)
                    entry.provider.Dispose();
                throw;
            }
        }

        internal bool TryCreateGeneration(
            RenderPipelineAsset asset,
            out Generation? generation)
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

            RenderPipeline pipeline = Create<RenderPipeline>(pipelineType);
            var features = new Dictionary<string, RenderPipelineFeature>(featureTypes.Count, StringComparer.Ordinal);
            try
            {
                pipeline.Configure(asset.pipelineState);
                foreach (RenderFeatureConfiguration configuration in asset.features.Where(
                             static value => value.enabled))
                {
                    RenderPipelineFeature feature = Create<RenderPipelineFeature>(
                        featureTypes[configuration.featureTypeId]);
                    feature.Configure(configuration);
                    features.Add(configuration.featureTypeId, feature);
                }

                generation = new Generation(pipeline, features);
                return true;
            }
            catch
            {
                pipeline.Dispose();
                DisposeFeatures(features.Values);
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
        private readonly IReadOnlyList<RequestProviderEntry> m_providers;

        internal RequestProviderGeneration(
            long typeCacheVersion,
            IReadOnlyList<RequestProviderEntry> providers)
        {
            this.typeCacheVersion = typeCacheVersion;
            m_providers = providers;
        }

        internal long typeCacheVersion { get; }

        internal IReadOnlyList<RequestProviderEntry> providers => m_providers;

        /// <summary>
        /// Releases the resources owned by this instance.
        /// </summary>
        public void Dispose()
        {
            foreach (RequestProviderEntry entry in m_providers)
                entry.provider.Dispose();
        }
    }

    internal sealed record RequestProviderEntry(
        string id,
        int priority,
        RenderRequestProvider provider);

    internal sealed class Generation : IDisposable
    {
        private bool m_transferred;

        internal Generation(
            RenderPipeline pipeline,
            IReadOnlyDictionary<string, RenderPipelineFeature> features)
        {
            this.pipeline = pipeline;
            this.features = features;
        }

        internal RenderPipeline pipeline { get; }

        internal IReadOnlyDictionary<string, RenderPipelineFeature> features { get; }

        internal void TransferOwnership() => m_transferred = true;

        /// <summary>
        /// Releases the resources owned by this instance.
        /// </summary>
        public void Dispose()
        {
            if (m_transferred)
                return;
            pipeline.Dispose();
            DisposeFeatures(features.Values);
        }
    }
}
