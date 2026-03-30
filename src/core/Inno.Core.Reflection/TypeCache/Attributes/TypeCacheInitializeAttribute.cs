using System;

namespace Inno.Core.Reflection;

/// <summary>
/// Marks a static parameterless method to be invoked when <see cref="TypeCacheManager.Initialize"/> runs.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class TypeCacheInitializeAttribute : Attribute;
