using System;

namespace Inno.Core.Serialization;

/// <summary>
/// Requires a serializable type to use an explicitly discovered converter.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = true)]
public sealed class RequiresSerializationConverterAttribute : Attribute;
