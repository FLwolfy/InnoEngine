using System;

namespace Inno.Core.Serialization.Converters;

/// <summary>
/// Registers a stateless serialization converter for automatic TypeCache discovery.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class SerializationExtensionAttribute : Attribute;
