using Inno.Assets.Core;
using Inno.Core.Reflection;
using Inno.Core.Serialization;

namespace Inno.Editor.Scripting;

/// <summary>Defines one project script assembly and its compilation policy.</summary>
[StableTypeId("038c096e-81e9-41dd-a8fb-fd0200ca2acb")]
public sealed class ScriptAssemblyDefinitionAsset : AssetObject
{
    /// <summary>Gets the stable assembly name.</summary>
    [SerializableProperty]
    public string assemblyName { get; private set; } = string.Empty;

    /// <summary>Gets the assembly API scope.</summary>
    [SerializableProperty]
    public ScriptAssemblyScope scope { get; private set; }

    /// <summary>Gets referenced script assembly names.</summary>
    [SerializableProperty]
    public string[] references { get; private set; } = [];

    /// <summary>Gets preprocessor symbols applied to this assembly.</summary>
    [SerializableProperty]
    public string[] defines { get; private set; } = [];

    /// <summary>Gets whether nullable reference analysis is enabled.</summary>
    [SerializableProperty]
    public bool nullable { get; private set; } = true;

    /// <summary>Gets whether unsafe source is permitted.</summary>
    [SerializableProperty]
    public bool allowUnsafe { get; private set; }

    /// <summary>Creates an empty definition asset for deserialization.</summary>
    public ScriptAssemblyDefinitionAsset()
    {
    }

    internal ScriptAssemblyDefinitionAsset(
        string assemblyName,
        ScriptAssemblyScope scope,
        string[] references,
        string[] defines,
        bool nullable,
        bool allowUnsafe)
    {
        this.assemblyName = assemblyName;
        this.scope = scope;
        this.references = references;
        this.defines = defines;
        this.nullable = nullable;
        this.allowUnsafe = allowUnsafe;
    }
}
