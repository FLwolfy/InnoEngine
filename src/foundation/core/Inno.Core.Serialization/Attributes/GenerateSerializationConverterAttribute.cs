using System;

namespace Inno.Core.Serialization;

/// <summary>
/// Requests a compile-time serialization converter for a closed data-transfer type.
/// </summary>
/// <remarks>
/// The annotated type must implement <see cref="ISerializable"/>, expose a constructor that generated
/// code can invoke, and use <see cref="SerializablePropertyAttribute"/> on every persisted member.
/// Types with polymorphic identity, reference graphs, or custom restoration invariants must use an
/// explicit converter instead.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class GenerateSerializationConverterAttribute : Attribute;
