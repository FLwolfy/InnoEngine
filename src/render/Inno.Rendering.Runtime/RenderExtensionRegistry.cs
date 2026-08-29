using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Inno.Core.Reflection;

namespace Inno.Rendering.Runtime;

internal sealed class RenderExtensionRegistry : TypeRegistry<RenderExtensionRegistry.Snapshot>
{
    internal Snapshot extensions => current;

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
        return new Snapshot(types.version, pipelines, features);
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

        internal Snapshot(
            long typeCacheVersion,
            IReadOnlyDictionary<string, Type> pipelines,
            IReadOnlyDictionary<string, Type> features)
        {
            this.typeCacheVersion = typeCacheVersion;
            m_pipelines = pipelines;
            m_features = features;
        }

        internal long typeCacheVersion { get; }

        internal Generation CreateGeneration(RenderPipelineAsset asset)
        {
            ArgumentNullException.ThrowIfNull(asset);
            if (string.IsNullOrWhiteSpace(asset.pipelineTypeId))
                throw new InvalidOperationException("A render pipeline asset requires a stable pipeline extension ID.");
            if (!m_pipelines.TryGetValue(asset.pipelineTypeId, out Type? pipelineType))
            {
                throw new InvalidOperationException(
                    $"Render pipeline extension '{asset.pipelineTypeId}' is not available in the active generation.");
            }

            RenderPipeline pipeline = Create<RenderPipeline>(pipelineType);
            var features = new Dictionary<string, RenderPipelineFeature>(StringComparer.Ordinal);
            try
            {
                pipeline.Configure(asset.pipelineState);
                foreach (RenderFeatureConfiguration configuration in asset.features)
                {
                    if (!configuration.enabled)
                        continue;
                    if (string.IsNullOrWhiteSpace(configuration.featureTypeId))
                        throw new InvalidOperationException("An enabled render feature requires a stable extension ID.");
                    if (features.ContainsKey(configuration.featureTypeId))
                    {
                        throw new InvalidOperationException(
                            $"Render feature '{configuration.featureTypeId}' is configured more than once.");
                    }
                    if (!m_features.TryGetValue(configuration.featureTypeId, out Type? featureType))
                    {
                        throw new InvalidOperationException(
                            $"Render feature extension '{configuration.featureTypeId}' is unavailable.");
                    }

                    RenderPipelineFeature feature = Create<RenderPipelineFeature>(featureType);
                    feature.Configure(configuration);
                    features.Add(configuration.featureTypeId, feature);
                }

                return new Generation(pipeline, features);
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

        public void Dispose()
        {
            if (m_transferred)
                return;
            pipeline.Dispose();
            DisposeFeatures(features.Values);
        }
    }
}
