using System;

namespace Inno.Core.Serialization;

/// <summary>
/// Describes one property skipped during a compatible property restore.
/// </summary>
public sealed class SerializationPropertyRestoreFailure
{
    internal SerializationPropertyRestoreFailure(
        string name,
        Type previousPropertyType,
        Type currentPropertyType,
        string message)
    {
        this.name = name;
        this.previousPropertyType = previousPropertyType;
        this.currentPropertyType = currentPropertyType;
        this.message = message;
    }

    /// <summary>Gets the serialized member key.</summary>
    public string name { get; }

    /// <summary>Gets the type used to capture the previous value.</summary>
    public Type previousPropertyType { get; }

    /// <summary>Gets the current target member type.</summary>
    public Type currentPropertyType { get; }

    /// <summary>Gets the underlying compatibility failure message.</summary>
    public string message { get; }
}
