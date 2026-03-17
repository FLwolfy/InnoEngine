namespace Inno.Core.Storage;

/// <summary>
/// Controls when cached values are recomputed.
/// </summary>
/// <remarks>
/// This only affects APIs that read/write cached values. When Disabled, the graph still tracks
/// dependencies but does not store or reuse TValue.
/// </remarks>
public enum DependencyCacheMode
{
    /// <summary>
    /// Disable caching entirely.
    /// </summary>
    /// <remarks>
    /// GetOrUpdate returns factory(key) and does not store it. TryGet always returns false.
    /// UpdateDirty does nothing.
    /// </remarks>
    Disabled,
    /// <summary>
    /// Lazy update.
    /// </summary>
    /// <remarks>
    /// Values are recomputed only when requested via GetOrUpdate. UpdateDirty can still be
    /// called manually to batch refresh.
    /// </remarks>
    Lazy,
    /// <summary>
    /// Eager update.
    /// </summary>
    /// <remarks>
    /// Each GetOrUpdate triggers UpdateDirty first, so all dirty nodes are refreshed
    /// in dependency order before returning a value.
    /// </remarks>
    Eager,
    /// <summary>
    /// Hybrid.
    /// </summary>
    /// <remarks>
    /// Default mode: lazy on demand, with optional batch UpdateDirty for editor-like workflows.
    /// </remarks>
    Hybrid
}