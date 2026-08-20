using System;

namespace Inno.Editor.Core.Panels;

/// <summary>Registers a centered editor modal for automatic discovery.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class EditorModalAttribute : Attribute
{
    /// <summary>
    /// Creates a centered modal registration with a stable identity and draw order.
    /// </summary>
    /// <param name="id">The stable identity used to retain modal transition state across refreshes.</param>
    /// <param name="title">The visible title rendered by the modal host.</param>
    /// <param name="order">The stable ordering value when multiple modals are visible.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="id"/> or <paramref name="title"/> is empty.</exception>
    public EditorModalAttribute(string id, string title, int order = 0)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("An editor modal identifier is required.", nameof(id));
        if (string.IsNullOrWhiteSpace(title))
            throw new ArgumentException("An editor modal title is required.", nameof(title));
        this.id = id;
        this.title = title;
        this.order = order;
    }

    /// <summary>Gets the stable modal identifier.</summary>
    public string id { get; }

    /// <summary>Gets the visible modal title.</summary>
    public string title { get; }

    /// <summary>Gets the stable draw order.</summary>
    public int order { get; }
}
