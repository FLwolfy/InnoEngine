using System;

namespace Inno.Core.Serialization;

/// <summary>
/// Defines member participation rules for serialization/deserialization and runtime access.
/// </summary>
/// <remarks>
/// This is a flag enum. Prefer the named presets (<see cref="Show"/>, <see cref="Hide"/>, <see cref="Readonly"/>,
/// <see cref="Transient"/>, <see cref="SerializeOnly"/>, <see cref="DeserializeOnly"/>) for common configurations.
/// </remarks>
[Flags]
public enum PropertyVisibility
{
    /// <summary>
    /// No participation in serialization, deserialization, or runtime access.
    /// </summary>
    None = 0,

    /// <summary>
    /// Participates in serialization (capture/write).
    /// </summary>
    Serialize = 1 << 0,

    /// <summary>
    /// Participates in deserialization (restore/read).
    /// </summary>
    Deserialize = 1 << 1,

    /// <summary>
    /// Allows runtime retrieval through the <see cref="SerializedProperty"/> API.
    /// </summary>
    RuntimeGet = 1 << 2,

    /// <summary>
    /// Allows runtime assignment through the <see cref="SerializedProperty"/> API.
    /// </summary>
    RuntimeSet = 1 << 3,

    /// <summary>
    /// Can serialize, deserialize, and allow runtime get/set.
    /// </summary>
    Show = Serialize | Deserialize | RuntimeGet | RuntimeSet,

    /// <summary>
    /// Can serialize and deserialize, but runtime code cannot get or set through <see cref="SerializedProperty"/>.
    /// </summary>
    Hide = Serialize | Deserialize,

    /// <summary>
    /// Can serialize and deserialize, runtime can get but cannot set.
    /// Runtime set attempts should be rejected (see <see cref="SerializedProperty.SetValue"/> behavior).
    /// </summary>
    Readonly = Serialize | Deserialize | RuntimeGet,

    /// <summary>
    /// Does not participate in serialization or deserialization, but runtime can get/set.
    /// </summary>
    Transient = RuntimeGet | RuntimeSet,

    /// <summary>
    /// Participates in serialization only (no deserialization), runtime can get/set.
    /// </summary>
    SerializeOnly = Serialize | RuntimeGet | RuntimeSet,

    /// <summary>
    /// Participates in deserialization only (no serialization), runtime can get/set.
    /// </summary>
    DeserializeOnly = Deserialize | RuntimeGet | RuntimeSet,
}
