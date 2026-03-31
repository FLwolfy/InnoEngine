using System;

namespace Inno.Core.Reflection;

/// <summary>
/// Marks a static parameterless method to be invoked after each successful <see cref="TypeCacheManager.Rebuild"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class TypeCacheRebuildAttribute : Attribute;
