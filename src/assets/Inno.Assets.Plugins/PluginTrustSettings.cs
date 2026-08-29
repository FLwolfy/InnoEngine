using System;
using System.Linq;
using Inno.Core.Reflection;
using Inno.Core.Serialization;
using Inno.Core.Settings;

namespace Inno.Assets.Plugins;

/// <summary>Stores project-local trust decisions for installed Plugin code.</summary>
[StableTypeId("4c5d71c5-63d1-4c87-a58f-f049fbd97539")]
[ProjectSettingDefinition("inno.plugins.trust")]
public sealed class PluginTrustSettings : ISerializable
{
    /// <summary>Gets the stable Plugin trust setting identifier.</summary>
    public static ProjectSettingId id => new("inno.plugins.trust");

    /// <summary>Gets or sets trusted stable Plugin IDs.</summary>
    [SerializableProperty]
    public string[] trustedPluginIds { get; set; } = [];

    /// <summary>Gets whether code from one Plugin ID may execute.</summary>
    /// <param name="pluginId">Stable Plugin ID.</param>
    /// <returns><see langword="true"/> when the project explicitly trusts the Plugin.</returns>
    public bool IsTrusted(string pluginId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);
        return trustedPluginIds.Contains(pluginId, StringComparer.Ordinal);
    }
}
