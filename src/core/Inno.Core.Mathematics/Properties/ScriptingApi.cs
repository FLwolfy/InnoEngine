using Inno.Scripting.Api;
using Inno.Core.Mathematics;

[assembly: ScriptingApiNamespace(
    "InnoEngine.Mathematics",
    "Inno.Core.Mathematics",
    ScriptingApiScope.Runtime)]

[assembly: ScriptingApiExport(typeof(Color), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(MathHelper), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(Matrix), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(Quaternion), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(Rect), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(RectInt), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(Vector2), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(Vector2Int), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(Vector3), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(Vector3Int), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(Vector4), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(Vector4Int), ScriptingApiScope.Runtime)]
