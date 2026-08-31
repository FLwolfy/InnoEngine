using System;

namespace Inno.Core.Assemblies;

/// <summary>
/// Identifies a logical assembly module without retaining its runtime assemblies.
/// </summary>
public readonly record struct AssemblyModuleHandle(Guid id);
