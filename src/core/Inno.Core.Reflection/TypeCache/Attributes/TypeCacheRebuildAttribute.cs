using System;

namespace Inno.Core.Reflection;

/// <summary>
/// Marks a static parameterless method to be invoked after each successful <see cref="TypeCacheManager.Rebuild"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class TypeCacheRebuildAttribute : Attribute
{
    /// <summary>
    /// Target assembly name. Null or empty means no assembly restriction.
    /// </summary>
    public string assemblyName { get; }

    public TypeCacheRebuildAttribute(string assemblyName)
    {
        this.assemblyName = assemblyName ?? throw new ArgumentNullException(nameof(assemblyName));
    }
}
