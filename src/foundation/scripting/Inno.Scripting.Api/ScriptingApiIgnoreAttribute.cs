using System;

namespace Inno.Scripting.Api;

/// <summary>
/// Explicitly excludes one otherwise visible member from generated scripting reference assemblies.
/// </summary>
[AttributeUsage(
    AttributeTargets.Constructor |
    AttributeTargets.Method |
    AttributeTargets.Property |
    AttributeTargets.Field |
    AttributeTargets.Event,
    AllowMultiple = false,
    Inherited = false)]
public sealed class ScriptingApiIgnoreAttribute : Attribute
{
}
