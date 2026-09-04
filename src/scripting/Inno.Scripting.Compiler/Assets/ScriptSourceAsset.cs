using Inno.Assets;
using Inno.Extensibility.Types;
using Inno.Core.Serialization;

namespace Inno.Scripting.Compiler;

/// <summary>
/// Describes an imported C# source snapshot and its parse diagnostics.
/// </summary>
[StableTypeId("10de711f-c33e-4ae2-a2e3-df285a86e976")]
public sealed class ScriptSourceAsset : AssetObject
{
    /// <summary>
    /// Gets the default assembly scope inferred for the source.
    /// </summary>
    [SerializableProperty]
    public ScriptAssemblyScope scope { get; private set; }

    /// <summary>
    /// Gets syntax-level type names declared by the source.
    /// </summary>
    [SerializableProperty]
    public string[] declaredTypeNames { get; private set; } = [];

    /// <summary>
    /// Gets parse diagnostics associated with the source snapshot.
    /// </summary>
    [SerializableProperty]
    public string[] parseDiagnostics { get; private set; } = [];

    /// <summary>
    /// Creates an empty script source asset for deserialization.
    /// </summary>
    public ScriptSourceAsset()
    {
    }

    internal ScriptSourceAsset(
        ScriptAssemblyScope scope,
        string[] declaredTypeNames,
        string[] parseDiagnostics)
    {
        this.scope = scope;
        this.declaredTypeNames = declaredTypeNames;
        this.parseDiagnostics = parseDiagnostics;
    }
}
