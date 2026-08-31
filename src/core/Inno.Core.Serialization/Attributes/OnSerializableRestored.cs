using System;

namespace Inno.Core.Serialization;

/// <summary>
/// Marks a parameterless instance method to run after a complete restore operation succeeds.
/// </summary>
[AttributeUsage(AttributeTargets.Method, Inherited = true)]
public sealed class OnSerializableRestored : Attribute;
