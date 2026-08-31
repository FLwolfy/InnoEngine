namespace Inno.Core.Serialization;

/// <summary>
/// Defines how independently captured serialized properties handle restore failures.
/// </summary>
public enum SerializationPropertyRestoreMode
{
    /// <summary>Stops at the first incompatible or invalid property.</summary>
    Strict,

    /// <summary>Skips incompatible properties and reports each failure.</summary>
    Compatible
}
