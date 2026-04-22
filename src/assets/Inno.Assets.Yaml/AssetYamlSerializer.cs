using System;
using Inno.Assets.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

using Inno.Core.Serialization;

namespace Inno.Assets.Yaml;

public static class AssetYamlSerializer
{
    // ---------------------------------------------------------------------
    // Inno Asset <-> YAML
    // ---------------------------------------------------------------------
    private static readonly ISerializer ASSET_YAML_WRITER = new SerializerBuilder()
        .IncludeNonPublicProperties()
        .WithTypeInspector(_ => new AssetPropertyTypeInspector())
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .Build();
    
    private static readonly IDeserializer ASSET_YAML_READER = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IncludeNonPublicProperties()
        .WithTypeInspector(_ => new AssetPropertyTypeInspector())
        .WithObjectFactory(new AssetObjectFactory())
        .IgnoreUnmatchedProperties()
        .Build();

    public static string SerializeAssetToYaml(InnoAsset innoAsset)
    {
        if (innoAsset == null) throw new ArgumentNullException(nameof(innoAsset));
        return ASSET_YAML_WRITER.Serialize(innoAsset);
    }

    public static T DeserializeAssetFromYaml<T>(string yamlString) where T : InnoAsset
    {
        if (yamlString == null) throw new ArgumentNullException(nameof(yamlString));
        return ASSET_YAML_READER.Deserialize<T>(yamlString);
    }
    
    // ---------------------------------------------------------------------
    // Raw SerializingState <-> YAML
    // ---------------------------------------------------------------------
    
    private static readonly ISerializer STATE_YAML_WRITER = new SerializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .Build();

    private static readonly IDeserializer STATE_YAML_READER = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    public static string SerializeStateToYaml(SerializingState state)
    {
        if (state == null) throw new ArgumentNullException(nameof(state));
        return STATE_YAML_WRITER.Serialize(SerializingStateYamlCodec.EncodeState(state));
    }

    public static SerializingState DeserializeStateFromYaml(string yamlString)
    {
        if (yamlString == null) throw new ArgumentNullException(nameof(yamlString));

        var parsed = STATE_YAML_READER.Deserialize<object>(yamlString);
        parsed = SerializingStateYamlCodec.NormalizeYamlObject(parsed)
                 ?? throw new InvalidOperationException("YAML is empty.");

        return SerializingStateYamlCodec.DecodeState(parsed);
    }
}
