using Inno.Scripting.Api;
using Inno.Text;

[assembly: ScriptingApiNamespace("InnoEngine.Text", "Inno.Text", ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(Text), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(FontAsset), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(FontMetadata), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(TextDirection), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(TextFontStyle), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(TextStyle), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(TextShapingOptions), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(TextGlyph), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(TextMetrics), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(TextLayout), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(GlyphBitmap), ScriptingApiScope.Runtime)]
