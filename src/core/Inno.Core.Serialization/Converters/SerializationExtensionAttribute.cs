using System;

namespace Inno.Core.Serialization.Converters;

/// <summary>
/// Registers a stateless serialization converter for automatic TypeCacheManager discovery.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class SerializationExtensionAttribute : Attribute;
