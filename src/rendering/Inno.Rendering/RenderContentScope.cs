using System;
using System.Collections.Generic;
using System.Linq;

namespace Inno.Rendering;

/// <summary>
/// Identifies one host-owned content root within a frame-scoped render operation.
/// </summary>
public readonly record struct RenderContentId
{
    /// <summary>
    /// Creates a stable content identity.
    /// </summary>
    /// <param name="value">
    /// Persistent identity of the host-owned content root.
    /// </param>
    public RenderContentId(Guid value)
    {
        if (value == Guid.Empty)
            throw new ArgumentException("A render content ID cannot be empty.", nameof(value));
        this.value = value;
    }

    /// <summary>
    /// Gets the persistent content identity.
    /// </summary>
    public Guid value { get; }

    /// <summary>
    /// Gets whether this identifier contains a usable value.
    /// </summary>
    public bool isValid => value != Guid.Empty;
}

/// <summary>
/// Associates a stable content identity with one frame-scoped host object.
/// </summary>
public readonly record struct RenderContentReference
{
    /// <summary>
    /// Creates a frame-scoped content reference.
    /// </summary>
    /// <param name="id">
    /// Stable identity of the referenced content root.
    /// </param>
    /// <param name="value">
    /// Current-generation host object; callers must not retain it beyond the frame.
    /// </param>
    public RenderContentReference(RenderContentId id, object value)
    {
        if (!id.isValid)
            throw new ArgumentException("A valid render content ID is required.", nameof(id));
        this.id = id;
        this.value = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>
    /// Gets the stable content identity.
    /// </summary>
    public RenderContentId id { get; }

    /// <summary>
    /// Gets the frame-scoped host object.
    /// </summary>
    public object value { get; }
}

/// <summary>
/// Carries an ordered, rendering-model-neutral set of host content roots into request providers.
/// </summary>
/// <remarks>
/// The scope is immutable, but contained objects remain owned by the host and are valid only for the
/// current frame and extension generation. Rendering code must copy any required state into its own
/// immutable frame snapshot before submission.
/// </remarks>
public sealed class RenderContentScope
{
    private readonly RenderContentReference[] m_contents;

    /// <summary>
    /// Creates an immutable content scope.
    /// </summary>
    /// <param name="contents">
    /// Ordered host-owned content roots visible to the operation.
    /// </param>
    /// <param name="activeContent">
    /// Optional identity selected as the primary content root.
    /// </param>
    /// <exception cref="ArgumentException">
    /// Thrown for duplicate identities or an unknown active identity.
    /// </exception>
    public RenderContentScope(
        IEnumerable<RenderContentReference> contents,
        RenderContentId? activeContent = null)
    {
        ArgumentNullException.ThrowIfNull(contents);
        m_contents = contents.ToArray();
        if (m_contents.Select(static item => item.id).Distinct().Count() != m_contents.Length)
            throw new ArgumentException("Render content identities must be unique within one scope.", nameof(contents));
        if (activeContent is RenderContentId active
            && (!active.isValid || !m_contents.Any(item => item.id == active)))
        {
            throw new ArgumentException("The active render content must exist in the scope.", nameof(activeContent));
        }
        this.activeContent = activeContent;
    }

    /// <summary>
    /// Gets a shared empty content scope.
    /// </summary>
    public static RenderContentScope empty { get; } = new([]);

    /// <summary>
    /// Gets the ordered immutable content references.
    /// </summary>
    public IReadOnlyList<RenderContentReference> contents => m_contents;

    /// <summary>
    /// Gets the optional active content identity.
    /// </summary>
    public RenderContentId? activeContent { get; }

    /// <summary>
    /// Returns all content values assignable to the requested type in scope order.
    /// </summary>
    /// <typeparam name="TValue">
    /// Host content type understood by the caller.
    /// </typeparam>
    /// <returns>
    /// A current-frame array that may be empty.
    /// </returns>
    public IReadOnlyList<TValue> GetValues<TValue>()
        where TValue : class
        => m_contents
            .Select(static item => item.value)
            .OfType<TValue>()
            .ToArray();

    /// <summary>
    /// Tries to resolve one typed value by stable content identity.
    /// </summary>
    /// <typeparam name="TValue">
    /// Expected host content type.
    /// </typeparam>
    /// <param name="id">
    /// Stable identity to resolve.
    /// </param>
    /// <param name="value">
    /// Receives the current-frame value when identity and type match.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the requested typed value exists.
    /// </returns>
    public bool TryGetValue<TValue>(RenderContentId id, out TValue? value)
        where TValue : class
    {
        if (!id.isValid)
        {
            value = null;
            return false;
        }
        foreach (RenderContentReference content in m_contents)
        {
            if (content.id == id && content.value is TValue typed)
            {
                value = typed;
                return true;
            }
        }
        value = null;
        return false;
    }
}
