using Inno.Assets.Core;
using Inno.Core.Reflection;
using Inno.Core.Serialization;

namespace Inno.Editor.Scripting;

/// <summary>Describes one managed project plugin and its script visibility.</summary>
[StableTypeId("02ef452b-3894-4f07-857b-94b10aac8112")]
public sealed class ManagedPluginAsset : AssetObject
{
    /// <summary>Gets the managed assembly name.</summary>
    [SerializableProperty]
    public string assemblyName { get; private set; } = string.Empty;

    /// <summary>Gets the script scope inferred for the plugin.</summary>
    [SerializableProperty]
    public ScriptAssemblyScope scope { get; private set; }

    /// <summary>Creates an empty plugin asset for deserialization.</summary>
    public ManagedPluginAsset()
    {
    }

    internal ManagedPluginAsset(string assemblyName, ScriptAssemblyScope scope)
    {
        this.assemblyName = assemblyName;
        this.scope = scope;
    }
}
