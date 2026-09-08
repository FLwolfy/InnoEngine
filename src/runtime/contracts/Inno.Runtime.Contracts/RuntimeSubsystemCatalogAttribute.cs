using System;

namespace Inno.Runtime.Contracts;

/// <summary>
/// Requests a generated partial method that gathers subsystem declarations with the same typed composition parameter.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class RuntimeSubsystemCatalogAttribute : Attribute;
