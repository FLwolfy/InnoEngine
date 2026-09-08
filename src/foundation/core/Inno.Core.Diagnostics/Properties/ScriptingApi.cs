using Inno.Core.Diagnostics;
using Inno.Scripting.Api;

[assembly: ScriptingApiNamespace("InnoEngine.Diagnostics", "Inno.Core.Diagnostics", ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(Diagnostic), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(DiagnosticSeverity), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(DiagnosticLocation), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(IDiagnosticReporter), ScriptingApiScope.Runtime)]
