using Inno.Core.Serialization;
using Inno.Core.Serialization.Converters;

namespace Inno.Rendering.Shaders;

internal sealed class ShaderGraphLiteralConverter : SerializationConverter<ShaderGraphLiteral>
{
    /// <inheritdoc />
    public override void Write(SerializationWriter writer, ShaderGraphLiteral value)
    { writer.Write("type", value.type); writer.Write("scalarBits", value.scalarBits); }
    /// <inheritdoc />
    public override ShaderGraphLiteral Read(SerializationReader reader)
        => new() { type = reader.Read<ShaderGraphType>("type"), scalarBits = reader.Read<uint[]>("scalarBits") };
}

internal sealed class ShaderGraphStageSettingsConverter : SerializationConverter<ShaderGraphStageSettings>
{
    /// <inheritdoc />
    public override void Write(SerializationWriter writer, ShaderGraphStageSettings value)
    {
        writer.Write("stage", value.stage);
        writer.Write("outputs", value.outputs);
        writer.Write("threadsX", value.threadsX);
        writer.Write("threadsY", value.threadsY);
        writer.Write("threadsZ", value.threadsZ);
    }
    /// <inheritdoc />
    public override ShaderGraphStageSettings Read(SerializationReader reader) => new()
    {
        stage = reader.Read<ShaderStage>("stage"),
        outputs = reader.Read<ShaderGraphOutput[]>("outputs"),
        threadsX = reader.Read<int>("threadsX"),
        threadsY = reader.Read<int>("threadsY"),
        threadsZ = reader.Read<int>("threadsZ"),
    };
}

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

internal sealed class ShaderGraphNodePortDefinitionConverter : SerializationConverter<ShaderGraphNodePortDefinition>
{
    /// <inheritdoc />
    public override void Write(SerializationWriter writer, ShaderGraphNodePortDefinition value)
    {
        writer.Write("id", value.id);
        writer.Write("type", value.type);
        writer.Write("required", value.required);
    }

    /// <inheritdoc />
    public override ShaderGraphNodePortDefinition Read(SerializationReader reader) => new()
    {
        id = reader.Read<string>("id"),
        type = reader.Read<ShaderGraphType>("type"),
        required = reader.Read<bool>("required")
    };
}

internal sealed class ShaderGraphNodeInputSettingsConverter : SerializationConverter<ShaderGraphNodeInputSettings>
{
    /// <inheritdoc />
    public override void Write(SerializationWriter writer, ShaderGraphNodeInputSettings value)
    {
        writer.Write("displayName", value.displayName);
        writer.Write("createPath", value.createPath);
        writer.Write("createOrder", value.createOrder);
        writer.Write("kind", value.kind);
        writer.Write("role", value.role);
        writer.Write("ports", value.ports);
    }

    /// <inheritdoc />
    public override ShaderGraphNodeInputSettings Read(SerializationReader reader) => new()
    {
        displayName = reader.Read<string>("displayName"),
        createPath = reader.Read<string>("createPath"),
        createOrder = reader.Read<int>("createOrder"),
        kind = reader.Read<ShaderGraphNodeKind>("kind"),
        role = reader.Read<string>("role"),
        ports = reader.Read<ShaderGraphNodePortDefinition[]>("ports")
    };
}

internal sealed class ShaderGraphNodeOutputSettingsConverter : SerializationConverter<ShaderGraphNodeOutputSettings>
{
    /// <inheritdoc />
    public override void Write(SerializationWriter writer, ShaderGraphNodeOutputSettings value)
        => writer.Write("ports", value.ports);

    /// <inheritdoc />
    public override ShaderGraphNodeOutputSettings Read(SerializationReader reader)
        => new() { ports = reader.Read<ShaderGraphNodePortDefinition[]>("ports") };
}

internal sealed class ShaderGraphNodeInterfaceConverter : SerializationConverter<ShaderGraphNodeInterface>
{
    /// <inheritdoc />
    public override void Write(SerializationWriter writer, ShaderGraphNodeInterface value)
    {
        writer.Write("displayName", value.displayName);
        writer.Write("createPath", value.createPath);
        writer.Write("createOrder", value.createOrder);
        writer.Write("kind", value.kind);
        writer.Write("role", value.role);
        writer.Write("inputs", value.inputs);
        writer.Write("outputs", value.outputs);
    }

    /// <inheritdoc />
    public override ShaderGraphNodeInterface Read(SerializationReader reader) => new()
    {
        displayName = reader.Read<string>("displayName"),
        createPath = reader.Read<string>("createPath"),
        createOrder = reader.Read<int>("createOrder"),
        kind = reader.Read<ShaderGraphNodeKind>("kind"),
        role = reader.Read<string>("role"),
        inputs = reader.Read<ShaderGraphNodePortDefinition[]>("inputs"),
        outputs = reader.Read<ShaderGraphNodePortDefinition[]>("outputs")
    };
}

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
