using System;
using System.Collections.Generic;
using System.Linq;
using Inno.Core.Identity;

namespace Inno.References;

/// <summary>
/// Exposes ordered content roots through weak, generation-checked identity snapshots for one control-thread operation.
/// </summary>
/// <remarks>
/// Disposing the scope revokes lookup. Copy values into domain snapshots; never retain resolved objects across frames or reloads.
/// </remarks>
public sealed class ContentReadScope : IDisposable
{
    private readonly IReadOnlyList<Identity> m_contents;
    private readonly int m_ownerThread = Environment.CurrentManagedThreadId;
    private bool m_disposed;

    /// <summary>
    /// Captures immutable root identities without retaining their live objects or registry.
    /// </summary>
    /// <param name="contents">
    /// Registered identities in deterministic root order.
    /// </param>
    /// <param name="activeContent">
    /// The optional persistent identity of the primary root.
    /// </param>
    /// <exception cref="ArgumentException">
    /// A root is detached, duplicated, or the active root is not present.
    /// </exception>
    public ContentReadScope(IEnumerable<Identity> contents, Guid? activeContent = null)
    {
        ArgumentNullException.ThrowIfNull(contents);
        Identity[] snapshot = contents.ToArray();
        if (snapshot.Any(static identity => identity.runtimeIdentity is null))
            throw new ArgumentException("Content roots must belong to live identity domains.", nameof(contents));
        if (snapshot.Select(static identity => identity.persistentId).Distinct().Count() != snapshot.Length)
            throw new ArgumentException("Content root persistent identities must be unique.", nameof(contents));
        if (activeContent is Guid active && !snapshot.Any(identity => identity.persistentId == active))
            throw new ArgumentException("The active content must belong to this scope.", nameof(activeContent));
        m_contents = Array.AsReadOnly(snapshot);
        this.activeContent = activeContent;
    }

    /// <summary>
    /// Gets a new empty operation scope owned by the caller.
    /// </summary>
    public static ContentReadScope empty => new([]);

    /// <summary>
    /// Gets the ordered weak identity snapshots; no live root object is retained.
    /// </summary>
    public IReadOnlyList<Identity> contents => m_contents;

    /// <summary>
    /// Gets the optional persistent identity selected as primary content.
    /// </summary>
    public Guid? activeContent { get; }

    /// <summary>
    /// Resolves all live roots matching the requested content contract.
    /// </summary>
    /// <typeparam name="TValue">
    /// The identity-backed content type understood by the consumer.
    /// </typeparam>
    /// <returns>
    /// A read-only operation snapshot; stale or incompatible roots are omitted.
    /// </returns>
    /// <exception cref="ObjectDisposedException">
    /// The operation scope has ended.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Lookup occurs off the owning control thread.
    /// </exception>
    public IReadOnlyList<TValue> GetValues<TValue>() where TValue : IdentityObject
    {
        EnsureActive();
        return Array.AsReadOnly(m_contents.Select(static identity => identity.Resolve<TValue>())
            .OfType<TValue>().ToArray());
    }

    /// <summary>
    /// Resolves a selected root without rebinding a stale snapshot to a replacement generation.
    /// </summary>
    /// <typeparam name="TValue">
    /// The required identity-backed root type.
    /// </typeparam>
    /// <param name="id">
    /// The persistent identity selecting a root already captured by this scope.
    /// </param>
    /// <param name="value">
    /// The live compatible object, or null when unavailable.
    /// </param>
    /// <returns>
    /// True only when the captured runtime identity is still live and compatible.
    /// </returns>
    /// <exception cref="ObjectDisposedException">
    /// The operation scope has ended.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Lookup occurs off the owning control thread.
    /// </exception>
    public bool TryGetValue<TValue>(Guid id, out TValue? value) where TValue : IdentityObject
    {
        EnsureActive();
        foreach (Identity identity in m_contents)
        {
            if (identity.persistentId != id)
                continue;
            value = identity.Resolve<TValue>();
            return value is not null;
        }
        value = null;
        return false;
    }

    /// <summary>
    /// Revokes all future root resolution without affecting the owner objects.
    /// </summary>
    public void Dispose() => m_disposed = true;

    private void EnsureActive()
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
        if (Environment.CurrentManagedThreadId != m_ownerThread)
            throw new InvalidOperationException("Content lookup requires its owning control thread.");
    }
}
