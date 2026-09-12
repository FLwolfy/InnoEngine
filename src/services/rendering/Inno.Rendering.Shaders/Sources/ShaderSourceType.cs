using System;
using System.Collections.Generic;
using System.Linq;

namespace Inno.Rendering.Shaders;

/// <summary>Describes a language-independent function value, including aggregate members and fixed arrays.</summary>
public sealed class ShaderSourceType
{
    private ShaderSourceType(string id, ShaderSourceType? element, int count, ShaderSourceField[] fields, ShaderStorageType? storage = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        this.id = id;
        elementType = element;
        elementCount = count;
        this.fields = Array.AsReadOnly(fields);
        this.storage = storage;
    }

    /// <summary>Gets the open semantic type identifier, independent of source-language spelling.</summary>
    public string id { get; }
    /// <summary>Gets the array element type, or null for a non-array value.</summary>
    public ShaderSourceType? elementType { get; }
    /// <summary>Gets the fixed array length, or zero for a non-array value.</summary>
    public int elementCount { get; }
    /// <summary>Gets immutable structure fields in declaration order.</summary>
    public IReadOnlyList<ShaderSourceField> fields { get; }

    /// <summary>Gets a typed storage binding contract, or null for ordinary values and sampled textures.</summary>
    public ShaderStorageType? storage { get; }

    /// <summary>Creates an opaque storage binding value with a complete element, format and access contract.</summary>
    /// <param name="storage">Resource description, independent of any actual GPU object.</param>
    /// <returns>An immutable storage handle type; it cannot be constructed or copied as an aggregate.</returns>
    public static ShaderSourceType Storage(ShaderStorageType storage)
        => new("storage", null, 0, [], storage ?? throw new ArgumentNullException(nameof(storage)));

    /// <summary>Creates an atomic type supplied by a language or graph target.</summary>
    /// <param name="id">Canonical type identity, such as float3, texture2d, or a provider-qualified opaque type.</param>
    /// <returns>An immutable atomic type.</returns>
    public static ShaderSourceType Atomic(string id) => new(id, null, 0, []);

    /// <summary>Creates a fixed-length array without collapsing its element type.</summary>
    /// <param name="element">Element type.</param>
    /// <param name="count">Positive fixed element count.</param>
    /// <returns>An immutable array type.</returns>
    /// <exception cref="ArgumentException">The element is void or an opaque storage binding.</exception>
    public static ShaderSourceType ArrayOf(ShaderSourceType element, int count)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);
        if (element.id == "void" || element.storage is not null) throw new ArgumentException("An array cannot contain void values or opaque storage bindings.", nameof(element));
        return new("array", element, count, []);
    }

    /// <summary>Creates a nominal structure with a validated immutable field layout.</summary>
    /// <param name="id">Public structure identity shared by alternative implementations.</param>
    /// <param name="fields">Unique named fields in declaration order.</param>
    /// <returns>An immutable structure type.</returns>
    /// <exception cref="ArgumentException">The identity is reserved, or fields are empty, null, or duplicated.</exception>
    public static ShaderSourceType Structure(string id, IEnumerable<ShaderSourceField> fields)
    {
        ArgumentNullException.ThrowIfNull(fields);
        if (id == "void") throw new ArgumentException("Void is reserved for the absence of a return value.", nameof(id));
        ShaderSourceField[] snapshot = fields.ToArray();
        if (snapshot.Length == 0 || snapshot.Any(static field => field is null) ||
            snapshot.Select(static field => field.name).Distinct(StringComparer.Ordinal).Count() != snapshot.Length)
            throw new ArgumentException("A structure requires unique named fields.", nameof(fields));
        return new(id, null, 0, snapshot);
    }

    /// <summary>Checks both nominal identity and complete aggregate layout.</summary>
    /// <param name="other">Candidate type from another implementation.</param>
    /// <returns>True only when both descriptors express the same function interface.</returns>
    public bool IsEquivalentTo(ShaderSourceType? other)
    {
        if (other is null || id != other.id || elementCount != other.elementCount || fields.Count != other.fields.Count)
            return false;
        if ((storage is null) != (other.storage is null) || storage is not null && !storage.IsEquivalentTo(other.storage))
            return false;
        if ((elementType is null) != (other.elementType is null) ||
            elementType is not null && !elementType.IsEquivalentTo(other.elementType))
            return false;
        for (int i = 0; i < fields.Count; i++)
            if (fields[i].name != other.fields[i].name || !fields[i].type.IsEquivalentTo(other.fields[i].type))
                return false;
        return true;
    }
}

/// <summary>Describes one immutable named structure field.</summary>
public sealed class ShaderSourceField
{
    /// <summary>Creates a structure field.</summary>
    /// <param name="name">Stable field name.</param>
    /// <param name="type">Complete field type.</param>
    /// <exception cref="ArgumentException">The field has void type or is an opaque storage binding.</exception>
    public ShaderSourceField(string name, ShaderSourceType type)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (type?.id == "void" || type?.storage is not null) throw new ArgumentException("A structure field cannot have void type or contain an opaque storage binding.", nameof(type));
        this.name = name;
        this.type = type ?? throw new ArgumentNullException(nameof(type));
    }
    /// <summary>Gets the public field name.</summary>
    public string name { get; }
    /// <summary>Gets the complete field type.</summary>
    public ShaderSourceType type { get; }
}
