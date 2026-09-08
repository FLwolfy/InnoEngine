using System;

namespace Inno.Extensibility.Modules;

/// <summary>
/// Identifies a logical assembly module without retaining its runtime assemblies.
/// </summary>
/// <param name="id">
/// The stable identity used to locate the requested value.
/// </param>
/// <returns>
/// The value produced by this implementation of the contract.
/// </returns>
public readonly record struct AssemblyModuleHandle(Guid id);
