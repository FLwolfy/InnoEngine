using Inno.Core.Serialization;
using Inno.Core.Serialization.Converters;

namespace Inno.Rendering.Shaders;

[SerializationExtension]
internal sealed class ShaderGraphStageSettingsConverter : SerializationConverter<ShaderGraphStageSettings>
{
    /// <inheritdoc />
    public override void Write(SerializationWriter writer, ShaderGraphStageSettings value)
    {
        writer.Write("pass", value.pass);
        writer.Write("stage", value.stage);
        writer.Write("outputs", value.outputs);
        writer.Write("threadsX", value.threadsX);
        writer.Write("threadsY", value.threadsY);
        writer.Write("threadsZ", value.threadsZ);
    }
    /// <inheritdoc />
    public override ShaderGraphStageSettings Read(SerializationReader reader) => new()
    {
        pass = reader.Read<string>("pass"),
        stage = reader.Read<ShaderStage>("stage"),
        outputs = reader.Read<ShaderGraphOutput[]>("outputs"),
        threadsX = reader.Read<int>("threadsX"),
        threadsY = reader.Read<int>("threadsY"),
        threadsZ = reader.Read<int>("threadsZ"),
    };
}

[SerializationExtension]
internal sealed class ShaderGraphInputSettingsConverter : SerializationConverter<ShaderGraphInputSettings>
{
    /// <inheritdoc />
    public override void Write(SerializationWriter writer, ShaderGraphInputSettings value)
    {
        writer.Write("id", value.id);
        writer.Write("type", value.type);
        writer.Write("kind", value.kind);
        writer.Write("semantic", value.semantic);
        writer.Write("location", value.location);
    }
    /// <inheritdoc />
    public override ShaderGraphInputSettings Read(SerializationReader reader) => new()
    {
        id = reader.Read<string>("id"),
        type = reader.Read<ShaderGraphType>("type"),
        kind = reader.Read<ShaderIrInputKind>("kind"),
        semantic = reader.Read<string>("semantic"),
        location = reader.Read<int>("location"),
    };
}

[SerializationExtension]
internal sealed class ShaderGraphTypeConverter : SerializationConverter<ShaderGraphType>
{
    /// <inheritdoc />
    public override void Write(SerializationWriter writer, ShaderGraphType value)
    {
        writer.Write("id", value.id);
        writer.Write("element", value.element);
        writer.Write("length", value.length);
        writer.Write("fieldNames", value.fieldNames);
        writer.Write("fieldTypes", value.fieldTypes);
        writer.Write("isStorage", value.isStorage);
        writer.Write("isImage", value.isImage);
        writer.Write("storageElement", value.storageElement);
        writer.Write("access", value.access);
        writer.Write("format", value.format);
        writer.Write("dimension", value.dimension);
        writer.Write("isArray", value.isArray);
    }
    /// <inheritdoc />
    public override ShaderGraphType Read(SerializationReader reader) => new()
    {
        id = reader.Read<string>("id"),
        element = reader.Read<ShaderGraphType?>("element"),
        length = reader.Read<int>("length"),
        fieldNames = reader.Read<string[]>("fieldNames"),
        fieldTypes = reader.Read<ShaderGraphType[]>("fieldTypes"),
        isStorage = reader.Read<bool>("isStorage"),
        isImage = reader.Read<bool>("isImage"),
        storageElement = reader.Read<ShaderGraphType?>("storageElement"),
        access = reader.Read<RenderStorageAccess>("access"),
        format = reader.Read<RenderTextureFormat>("format"),
        dimension = reader.Read<RenderTextureDimension>("dimension"),
        isArray = reader.Read<bool>("isArray"),
    };
}
