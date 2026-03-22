using System;
using System.Collections.Generic;
using System.Threading;

using GpuCacheKey = (System.Guid, System.Type, int);

namespace Inno.Engine.Graphics.Resources.GpuResources.Cache;

/// <summary>
/// Reference-counted, in-memory GPU resource cache keyed by:
/// <list type="bullet">
///   <item><description><see cref="Guid"/>: logical resource identity (e.g., asset GUID, embedded GUID, or user-defined key).</description></item>
///   <item><description><see cref="Type"/>: the concrete GPU resource type <c>T</c> being cached.</description></item>
///   <item><description><c>variantKey</c>: an additional discriminant for variants of the same GUID/type (e.g., PSO permutations, layout variants).</description></item>
/// </list>
///
/// <para>
/// This cache is intended for <b>runtime</b> GPU resources whose lifetime is scoped to the current process.
/// It uses reference counting to ensure a cached resource is disposed when no longer in use.
/// </para>
///
/// <para>
/// Thread safety: all cache mutations (<see cref="Acquire{T}"/>, releases via <see cref="Handle{T}.Dispose"/>, and <see cref="Clear"/>)
/// are synchronized via an internal lock.
/// </para>
/// </summary>
public sealed class GpuCache
{
    public static readonly Guid GLOBAL_DOMAIN = new("11111111-1111-1111-1111-111111111111");

    private readonly Lock m_sync = new();
    private readonly Dictionary<GpuCacheKey, Entry> m_cache = new();

    internal GpuCache() { }

    private sealed class Entry
    {
        public required IDisposable resource;
        public int refCount;
    }

    /// <summary>
    /// A lightweight, RAII-style handle that keeps a cached GPU resource alive.
    ///
    /// <para>
    /// Disposing the handle decrements the internal reference count associated with the resource key. When the last
    /// handle is disposed, the resource is removed from the cache and disposed.
    /// </para>
    ///
    /// <para>
    /// Intended usage pattern:
    /// <code>
    /// using var tex = gpuCache.Acquire(() => CreateTexture(...), guid, variantKey);
    /// var texture = tex.value;
    /// </code>
    /// </para>
    ///
    /// <para>
    /// Important: <see cref="Handle{T}"/> is a <c>readonly struct</c>. Avoid copying it unintentionally; if you copy it,
    /// you create multiple disposables for the same key, which can lead to refcount underflow-like behavior (extra releases).
    /// Prefer passing by <c>in</c> when necessary, and keep the handle in a single owner scope.
    /// </para>
    /// </summary>
    /// <typeparam name="T">Concrete GPU resource type.</typeparam>
    public readonly struct Handle<T> : IDisposable where T : class, IDisposable
    {
        private readonly GpuCache? m_resourceCache;
        private readonly GpuCacheKey m_key;

        /// <summary>
        /// The cached GPU resource instance associated with this handle.<br/>
        /// This is the actual object stored in the cache. It remains valid while at least one handle exists.
        /// </summary>
        public T value { get; }

        internal Handle(GpuCache resourceCache, GpuCacheKey key, T value)
        {
            m_resourceCache = resourceCache;
            m_key = key;
            this.value = value;
        }

        /// <summary>
        /// Releases this handle's reference to the cached resource.
        ///
        /// <para>
        /// This decrements the reference count in the owning <see cref="GpuCache"/>. If this was the last active handle,
        /// the cache entry is removed and the resource is disposed.
        /// </para>
        /// </summary>
        public void Dispose() => m_resourceCache?.Release(m_key);
    }

    /// <summary>
    /// Acquires a cached GPU resource of type <typeparamref name="T"/> for the given (guid, variantKey).
    ///
    /// <para>
    /// If an entry already exists in the cache, its reference count is incremented and a handle is returned.
    /// If not, <paramref name="factory"/> is invoked to create the resource, the new entry is stored with refCount=1,
    /// and a handle is returned.
    /// </para>
    ///
    /// <para>
    /// The cache key includes <see cref="Type"/> (<c>typeof(T)</c>), so the same GUID can safely be used across different
    /// GPU resource types without collisions.
    /// </para>
    ///
    /// <para>
    /// <paramref name="variantKey"/> is intended to differentiate multiple GPU instances under the same GUID and type,
    /// such as pipeline/layout permutations, sampler variants, format variants, etc.
    /// </para>
    /// </summary>
    /// <typeparam name="T">Concrete GPU resource type to acquire.</typeparam>
    /// <param name="factory">Factory used to create the resource if it is not present in the cache.</param>
    /// <param name="guid">Logical identifier for the resource (asset GUID, embedded GUID, or another stable ID).</param>
    /// <param name="variantKey">Additional discriminant for variants of the same GUID and type (default: 0).</param>
    /// <returns>A reference-counted handle that keeps the cached resource alive.</returns>
    public Handle<T> Acquire<T>(Func<T> factory, Guid guid, int variantKey = 0)
        where T : class, IDisposable
    {
        var key = new GpuCacheKey(guid, typeof(T), variantKey);

        lock (m_sync)
        {
            if (m_cache.TryGetValue(key, out var e))
            {
                e.refCount++;
                return new Handle<T>(this, key, (T)e.resource);
            }

            var created = factory();
            m_cache[key] = new Entry { resource = created, refCount = 1 };
            return new Handle<T>(this, key, created);
        }
    }

    private void Release(GpuCacheKey key)
    {
        lock (m_sync)
        {
            if (!m_cache.TryGetValue(key, out var e))
                return;

            e.refCount--;
            if (e.refCount > 0) return;

            m_cache.Remove(key);
            e.resource.Dispose();
        }
    }

    /// <summary>
    /// Disposes all cached resources and clears the cache immediately.
    ///
    /// <para>
    /// This forcefully disposes every cached resource regardless of existing handles. After calling <see cref="Clear"/>,
    /// any previously acquired handle's <see cref="Handle{T}.value"/> may still reference a disposed object.
    /// Therefore, <see cref="Clear"/> should be used only at well-defined shutdown/reload boundaries where all users
    /// of cached resources have been quiesced.
    /// </para>
    /// </summary>
    public void Clear()
    {
        lock (m_sync)
        {
            foreach (var entry in m_cache.Values)
                entry.resource.Dispose();

            m_cache.Clear();
        }
    }
}
