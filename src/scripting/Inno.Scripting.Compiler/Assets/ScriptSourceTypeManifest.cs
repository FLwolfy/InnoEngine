using System;
using Inno.Core.Serialization;

namespace Inno.Scripting.Compiler;

internal sealed class ScriptSourceTypeManifest : ISerializable
{
    /// <summary>
    /// Creates a validated script source type manifest instance.
    /// </summary>
    public ScriptSourceTypeManifest()
    {
    }

    /// <summary>
    /// Creates a validated script source type manifest instance.
    /// </summary>
    /// <param name="sourcePersistentId">
    /// The source persistent id consumed by script source type manifest; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <param name="sourcePath">
    /// The normalized file-system path used by this operation.
    /// </param>
    /// <param name="declarations">
    /// The declarations consumed by script source type manifest; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    public ScriptSourceTypeManifest(
        Guid sourcePersistentId,
        string sourcePath,
        ScriptSourceTypeDeclaration[] declarations)
    {
        this.sourcePersistentId = sourcePersistentId;
        this.sourcePath = sourcePath;
        this.declarations = declarations;
    }

    /// <summary>
    /// Gets the persistent identity of the script source represented by this manifest.
    /// </summary>
    [SerializableProperty]
    public Guid sourcePersistentId { get; set; }

    /// <summary>
    /// Gets the normalized source path used by the current operation.
    /// </summary>
    [SerializableProperty]
    public string sourcePath { get; set; } = string.Empty;

    /// <summary>
    /// Gets the stable attachable-type declarations discovered in the script source.
    /// </summary>
    [SerializableProperty]
    public ScriptSourceTypeDeclaration[] declarations { get; set; } = [];
}

internal struct ScriptSourceTypeDeclaration
{
    /// <summary>
    /// Creates a validated script source type declaration instance.
    /// </summary>
    /// <param name="typeName">
    /// The type name text validated by the script source type declaration operation.
    /// </param>
    /// <param name="declarationKind">
    /// The declaration kind text validated by the script source type declaration operation.
    /// </param>
    /// <param name="partial">
    /// Whether partial behavior is enabled while script source type declaration executes.
    /// </param>
    /// <param name="line">
    /// The line consumed by script source type declaration; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <param name="column">
    /// The column consumed by script source type declaration; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    public ScriptSourceTypeDeclaration(
        string typeName,
        string declarationKind,
        bool partial,
        int line,
        int column)
    {
        this.typeName = typeName;
        this.declarationKind = declarationKind;
        this.partial = partial;
        this.line = line;
        this.column = column;
    }

    /// <summary>
    /// Gets text used for stable identity, presentation, or diagnostics by this contract.
    /// </summary>
    public string typeName { get; set; }
    /// <summary>
    /// Gets text used for stable identity, presentation, or diagnostics by this contract.
    /// </summary>
    public string declarationKind { get; set; }
    /// <summary>
    /// Gets whether the caller-visible condition represented by this property is satisfied.
    /// </summary>
    public bool partial { get; set; }
    /// <summary>
    /// Gets the scalar measurement or identity associated with the current state.
    /// </summary>
    public int line { get; set; }
    /// <summary>
    /// Gets the scalar measurement or identity associated with the current state.
    /// </summary>
    public int column { get; set; }
}
