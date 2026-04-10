using System;

namespace Inno.Core.Reflection;

/// <summary>
/// Marks a static parameterless method to be invoked when <see cref="TypeCacheManager.Initialize"/> runs.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class TypeCacheInitializeAttribute : Attribute
{
    /// <summary>
    /// Target assembly name. Null or empty means no assembly restriction.
    /// </summary>
    public string assemblyName { get; }

    public TypeCacheInitializeAttribute(string assemblyName)
    {
        this.assemblyName = assemblyName ?? throw new ArgumentNullException(nameof(assemblyName));
    }
}
