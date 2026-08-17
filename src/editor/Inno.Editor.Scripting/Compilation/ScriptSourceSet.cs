using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Inno.Editor.Scripting;

internal sealed record ScriptSourceSet(
    IReadOnlyList<string> gameSources,
    IReadOnlyList<string> editorSources,
    IReadOnlyList<string> runtimePlugins,
    IReadOnlyList<string> editorPlugins)
{
    internal static ScriptSourceSet Discover(string assetDirectory)
    {
        if (!Directory.Exists(assetDirectory))
            return new ScriptSourceSet([], [], [], []);

        string[] sources = Directory.EnumerateFiles(assetDirectory, "*", SearchOption.AllDirectories)
            .Where(static path => path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            .OrderBy(static path => path, StringComparer.Ordinal)
            .ToArray();
        string[] editorSources = sources
            .Where(static path => path.EndsWith(".editor.cs", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        string[] gameSources = sources.Except(editorSources, StringComparer.OrdinalIgnoreCase).ToArray();

        string pluginDirectory = Path.Combine(assetDirectory, "Plugins");
        if (!Directory.Exists(pluginDirectory))
            return new ScriptSourceSet(gameSources, editorSources, [], []);
        string[] plugins = Directory.EnumerateFiles(pluginDirectory, "*", SearchOption.AllDirectories)
            .Where(static path => path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            .OrderBy(static path => path, StringComparer.Ordinal)
            .ToArray();
        string[] editorPlugins = plugins
            .Where(static path => path.EndsWith(".editor.dll", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        string[] runtimePlugins = plugins.Except(editorPlugins, StringComparer.OrdinalIgnoreCase).ToArray();
        return new ScriptSourceSet(gameSources, editorSources, runtimePlugins, editorPlugins);
    }
}
