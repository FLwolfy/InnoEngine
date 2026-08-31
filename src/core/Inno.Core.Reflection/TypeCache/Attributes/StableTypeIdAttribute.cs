using System;

namespace Inno.Core.Reflection;

/// <summary>
/// Defines a stable identity for a type, used by persistence and hot-reload remapping.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface, Inherited = false)]
public sealed class StableTypeIdAttribute(string id) : Attribute
{
    /// <summary>
    /// Stable type id as Guid string.
    /// </summary>
    public string id { get; } = id;
}
