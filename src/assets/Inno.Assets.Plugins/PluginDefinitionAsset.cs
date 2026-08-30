using System;
using Inno.Assets.Core;
using Inno.Core.Reflection;
using Inno.Core.Serialization;
using Inno.Core.Settings;

namespace Inno.Assets.Plugins;

/// <summary>Defines project-owned content and settings exported into one local ZIP or directory Plugin.</summary>
[StableTypeId("21ac4d44-c9aa-42ca-b2f6-d2f8d85d6d5f")]
public sealed class PluginDefinitionAsset : AssetObject
{
    /// <summary>Gets or sets the globally stable lowercase Plugin ID.</summary>
    [SerializableProperty]
    public string pluginId { get; set; } = string.Empty;

    /// <summary>Gets or sets the artist-facing Plugin name.</summary>
    [SerializableProperty]
    public string displayName { get; set; } = string.Empty;

    /// <summary>Gets or sets project-local directory roots included recursively.</summary>
    [SerializableProperty]
    public string[] assetRoots { get; set; } = [];

    /// <summary>Gets or sets explicit assets whose dependency closure is included.</summary>
    [SerializableProperty]
    public AssetObject[] assets { get; set; } = [];

    /// <summary>Gets or sets installed Plugin IDs required by exported content.</summary>
    [SerializableProperty]
    public string[] dependencies { get; set; } = [];

    /// <summary>Gets or sets dependency Plugin IDs whose setting defaults may be replaced.</summary>
    [SerializableProperty]
    public string[] overrides { get; set; } = [];

    /// <summary>
    /// Gets or sets setting protocols whose project-authored semantic deltas are exported as Plugin defaults.
    /// </summary>
    [SerializableProperty]
    public ProjectSettingId[] settingIds { get; set; } = [];
}
