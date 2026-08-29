using System;
using Inno.Core.Serialization;

namespace Inno.Editor.Scripting;

internal sealed class ScriptSourceTypeManifest : ISerializable
{
    public ScriptSourceTypeManifest()
    {
    }

    public ScriptSourceTypeManifest(
        Guid sourcePersistentId,
        string sourcePath,
        ScriptSourceTypeDeclaration[] declarations)
    {
        this.sourcePersistentId = sourcePersistentId;
        this.sourcePath = sourcePath;
        this.declarations = declarations;
    }

    [SerializableProperty]
    public Guid sourcePersistentId { get; set; }

    [SerializableProperty]
    public string sourcePath { get; set; } = string.Empty;

    [SerializableProperty]
    public ScriptSourceTypeDeclaration[] declarations { get; set; } = [];
}

internal struct ScriptSourceTypeDeclaration
{
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

    public string typeName { get; set; }
    public string declarationKind { get; set; }
    public bool partial { get; set; }
    public int line { get; set; }
    public int column { get; set; }
}
