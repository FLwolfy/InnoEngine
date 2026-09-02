using Inno.Core.Graphs;
using Inno.Scripting.Api;

[assembly: ScriptingApiNamespace(
    "InnoEngine.Graphs",
    "Inno.Core.Graphs",
    ScriptingApiScope.Runtime)]

[assembly: ScriptingApiExport(typeof(GraphNodeId), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(GraphEdgeId), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(GraphPortId), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(GraphPosition), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(GraphSerializedValue), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(GraphNodeRecord), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(GraphEndpoint), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(GraphEdgeRecord), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(GraphDocument), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(GraphPortDirection), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(GraphPortCapacity), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(GraphPortDefinition), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(GraphNodeExtensionAttribute), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(GraphNodeDefinition), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(IGraphNodeDefinitionResolver), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(IGraphTypeConversion), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(GraphDiagnosticSeverity), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(GraphDiagnostic), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(GraphValidationResult), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(GraphValidator), ScriptingApiScope.Runtime)]
