using System;

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

    /// <summary>Creates a configured script assembly definition for native asset export.</summary>
    /// <param name="assemblyName">Stable managed assembly name.</param>
    /// <param name="scope">Runtime or Editor API scope.</param>
    /// <param name="references">Referenced script assembly names.</param>
    /// <param name="defines">Preprocessor symbols.</param>
    /// <param name="nullable">Whether nullable reference analysis is enabled.</param>
    /// <param name="allowUnsafe">Whether unsafe source is permitted.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="assemblyName"/> is empty.</exception>
    public ScriptAssemblyDefinitionAsset(
        string assemblyName,
        ScriptAssemblyScope scope,
        string[]? references = null,
        string[]? defines = null,
        bool nullable = true,
        bool allowUnsafe = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assemblyName);
        this.assemblyName = assemblyName;
        this.scope = scope;
        this.references = references is null ? [] : [.. references];
        this.defines = defines is null ? [] : [.. defines];
        this.nullable = nullable;
        this.allowUnsafe = allowUnsafe;
    }
}
