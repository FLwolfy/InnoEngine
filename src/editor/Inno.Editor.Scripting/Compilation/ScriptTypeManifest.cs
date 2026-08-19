using System;
using System.Collections.Generic;

namespace Inno.Editor.Scripting;

internal sealed record ScriptTypeManifest(
    int schemaVersion,
    string assemblyName,
    IReadOnlyList<ScriptTypeManifestEntry> types);

internal sealed record ScriptTypeManifestEntry(
    string typeName,
    string kind,
    Guid stableTypeId,
    Guid sourcePersistentId,
    string sourcePath,
    int line,
    int column,
    bool explicitIdentity,
    bool canonicalSource);

internal sealed record ScriptTypeMapping(
    string typeName,
    Guid stableTypeId,
    Guid formerStableTypeId);

internal sealed record ScriptTypeAnalysisResult(
    ScriptTypeManifest manifest,
    IReadOnlyList<ScriptTypeMapping> mappings,
    IReadOnlyList<ScriptDiagnostic> diagnostics);
