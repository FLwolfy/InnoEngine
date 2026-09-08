using System;
using System.IO;

using Inno.Core.Serialization;
using Inno.Core.Serialization.Converters;
using Inno.Scene;

namespace Inno.Scene;

[SerializationExtension]
internal sealed class GameSystemReferenceConverter : SerializationConverter<GameSystem>
{
    /// <summary>
    /// Writes the supplied value through the owning subsystem's validated output boundary.
    /// </summary>
    /// <param name="writer">
    /// The writer that receives the deterministic structured representation.
    /// </param>
    /// <param name="value">
    /// The concrete value read or transformed by this operation.
    /// </param>
    public override void Write(SerializationWriter writer, GameSystem value)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(value);
        EngineReferenceToken token = SceneGraphReferenceMap.current.Capture(value, writer.path);
        writer.Write("kind", (int)token.kind);
        writer.Write("sourceId", token.sourceId);
    }

    /// <summary>
    /// Reads and validates the requested value without transferring storage ownership.
    /// </summary>
    /// <param name="reader">
    /// The reader positioned at the structured value to decode.
    /// </param>
    /// <returns>
    /// The validated game system that represents the completed operation.
    /// </returns>
    public override GameSystem Read(SerializationReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        var token = new EngineReferenceToken(
            (EngineReferenceKind)reader.Read<int>("kind"),
            reader.Read<Guid>("sourceId"));
        if (token.kind != EngineReferenceKind.GameSystem)
            throw new InvalidDataException($"Reference token at '{reader.path}' is not a GameSystem reference.");
        return (GameSystem)SceneGraphReferenceMap.current.Resolve(token, reader.valueType, reader.path);
    }
}
