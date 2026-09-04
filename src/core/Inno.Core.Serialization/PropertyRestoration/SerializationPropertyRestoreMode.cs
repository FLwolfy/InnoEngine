namespace Inno.Core.Serialization;

/// <summary>
/// Defines how independently captured serialized properties handle restore failures.
/// </summary>
public enum SerializationPropertyRestoreMode
{
    /// <summary>
    /// Stops at the first property that cannot be restored into the current object.
    /// </summary>
    Strict,

    /// <summary>
    /// Continues restoring independent properties and reports every rejected value.
    /// </summary>
    CollectFailures
}
