using System;
using System.Collections.Generic;

namespace Inno.Editor.Scripting;

internal sealed record ScriptSourceTypeManifest(
    int schemaVersion,
    Guid sourcePersistentId,
    string sourcePath,
    IReadOnlyList<ScriptSourceTypeDeclaration> declarations);

internal sealed record ScriptSourceTypeDeclaration(
    string typeName,
    string declarationKind,
    bool partial,
    int line,
    int column);
