using System;

using Inno.Core.Diagnose;

namespace Inno.Editor.Panel.Logging;

internal readonly record struct EditorDiagnosticEntry(
    long id,
    DiagnosticSource source,
    DateTime time,
    string code,
    DiagnosticSeverity severity,
    string message,
    string file,
    int line,
    int column);
