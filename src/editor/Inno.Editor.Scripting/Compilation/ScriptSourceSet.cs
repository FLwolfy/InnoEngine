using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Inno.Assets;
using Inno.Assets.Core;
using Inno.Assets.File;

namespace Inno.Editor.Scripting;

internal sealed record ScriptSourceSet(
    IReadOnlyList<string> gameSources,
    IReadOnlyList<string> editorSources,
    IReadOnlyList<ScriptPluginInput> runtimePlugins,
    IReadOnlyList<ScriptPluginInput> editorPlugins,
    IReadOnlyList<ScriptAssemblyDefinition> definitions,
    IReadOnlyList<ScriptAssemblyInput> assemblies)
{
    internal static ScriptSourceSet Discover()
    {
        if (!AssetManager.isInitialized)
            throw new InvalidOperationException("Script discovery requires the Asset Database.");

        AssetFileEntry[] entries = AssetManager.GetFileSystemEntries(includeDirectories: false)
            .OrderBy(static entry => entry.relativePath, StringComparer.Ordinal)
            .ToArray();
        ScriptAssemblyDefinition[] definitions = entries
            .Where(static entry => string.Equals(
                entry.extension,
                ".iasmdef",
                StringComparison.OrdinalIgnoreCase))
            .Select(ParseDefinition)
            .OrderBy(static definition => definition.directory, StringComparer.Ordinal)
            .ToArray();

        var assemblyBuilders = definitions.ToDictionary(
            static definition => definition.name,
            static definition => new AssemblyBuilder(definition),
            StringComparer.OrdinalIgnoreCase);
        if (assemblyBuilders.ContainsKey("Inno.GameScripts") ||
            assemblyBuilders.ContainsKey("Inno.EditorScripts"))
        {
            throw new InvalidDataException(
                "Assembly definitions cannot use the reserved Inno.GameScripts or Inno.EditorScripts names.");
        }
        var gameBuilder = new AssemblyBuilder(new ScriptAssemblyDefinition(
            "Inno.GameScripts",
            string.Empty,
            ScriptAssemblyScope.Runtime,
            [],
            ["DEBUG", "TRACE"],
            nullable: true,
            allowUnsafe: false,
            artifactKey: "builtin-runtime"));
        var editorBuilder = new AssemblyBuilder(new ScriptAssemblyDefinition(
            "Inno.EditorScripts",
            string.Empty,
            ScriptAssemblyScope.Editor,
            ["Inno.GameScripts"],
            ["DEBUG", "TRACE"],
            nullable: true,
            allowUnsafe: false,
            artifactKey: "builtin-editor"));
        assemblyBuilders.Add(gameBuilder.definition.name, gameBuilder);
        assemblyBuilders.Add(editorBuilder.definition.name, editorBuilder);
        var runtimePlugins = new List<ScriptPluginInput>();
        var editorPlugins = new List<ScriptPluginInput>();
        foreach (AssetFileEntry entry in entries)
        {
            if (string.Equals(entry.extension, ".cs", StringComparison.OrdinalIgnoreCase))
            {
                ScriptAssemblyDefinition? definition = FindNearestDefinition(entry.relativePath, definitions);
                ScriptAssemblyScope scope = definition?.scope ??
                    (entry.relativePath.EndsWith(".editor.cs", StringComparison.OrdinalIgnoreCase)
                        ? ScriptAssemblyScope.Editor
                        : ScriptAssemblyScope.Runtime);
                AssemblyBuilder builder = definition is null
                    ? scope == ScriptAssemblyScope.Editor ? editorBuilder : gameBuilder
                    : assemblyBuilders[definition.name];
                builder.sources.Add(CreateSourceInput(entry));
                continue;
            }
            if (!string.Equals(entry.extension, ".dll", StringComparison.OrdinalIgnoreCase) ||
                !IsPluginPath(entry.relativePath))
            {
                continue;
            }
            ScriptPluginInput plugin = CreatePluginInput(entry);
            (plugin.scope == ScriptAssemblyScope.Editor ? editorPlugins : runtimePlugins).Add(plugin);
        }

        ScriptAssemblyInput[] assemblies = ValidateAndOrderAssemblies(assemblyBuilders);
        return new ScriptSourceSet(
            gameBuilder.sources.Select(static source => source.sourcePath).ToArray(),
            editorBuilder.sources.Select(static source => source.sourcePath).ToArray(),
            runtimePlugins,
            editorPlugins,
            definitions,
            assemblies);
    }

    private static ScriptAssemblyDefinition ParseDefinition(AssetFileEntry entry)
    {
        ScriptAssemblyDefinitionAsset asset = AssetManager.Load<ScriptAssemblyDefinitionAsset>(
            entry.relativePath);
        if (!AssetManager.TryGetInfo(entry.relativePath, out AssetInfo? info) || info is null)
            throw new InvalidDataException($"Script assembly definition '{entry.relativePath}' has no metadata.");
        return new ScriptAssemblyDefinition(
            asset.assemblyName,
            Path.GetDirectoryName(entry.relativePath)?.Replace('\\', '/') ?? string.Empty,
            asset.scope,
            asset.references,
            asset.defines,
            asset.nullable,
            asset.allowUnsafe,
            info.artifactKey.value);
    }

    private static ScriptSourceInput CreateSourceInput(AssetFileEntry entry)
    {
        if (!AssetManager.TryGetInfo(entry.relativePath, out AssetInfo? info) ||
            info is null ||
            info.persistentId == Guid.Empty ||
            !AssetManager.TryGetArtifact(info.persistentId, "source", out AssetArtifactInfo? source) ||
            source is null)
        {
            throw new InvalidDataException(
                $"Script source '{entry.relativePath}' has no committed source artifact.");
        }
        return new ScriptSourceInput(
            entry.relativePath,
            GetAbsolutePath(entry.relativePath),
            source.absolutePath,
            info.persistentId,
            info.artifactKey.value);
    }

    private static ScriptPluginInput CreatePluginInput(AssetFileEntry entry)
    {
        ManagedPluginAsset descriptor = AssetManager.Load<ManagedPluginAsset>(entry.relativePath);
        if (!AssetManager.TryGetInfo(entry.relativePath, out AssetInfo? info) ||
            info is null ||
            info.persistentId == Guid.Empty ||
            !AssetManager.TryGetArtifact(info.persistentId, "assembly", out AssetArtifactInfo? assembly) ||
            assembly is null)
        {
            throw new InvalidDataException(
                $"Managed plugin '{entry.relativePath}' has no committed assembly artifact.");
        }
        _ = AssetManager.TryGetArtifact(info.persistentId, "symbols", out AssetArtifactInfo? symbols);
        _ = AssetManager.TryGetArtifact(info.persistentId, "dependencies", out AssetArtifactInfo? dependencies);
        return new ScriptPluginInput(
            GetAbsolutePath(entry.relativePath),
            assembly.absolutePath,
            symbols?.absolutePath,
            dependencies?.absolutePath,
            descriptor.scope,
            info.artifactKey.value);
    }

    private static ScriptAssemblyDefinition? FindNearestDefinition(
        string relativePath,
        IReadOnlyList<ScriptAssemblyDefinition> definitions)
    {
        string directory = Path.GetDirectoryName(relativePath)?.Replace('\\', '/') ?? string.Empty;
        return definitions
            .Where(definition => IsWithin(directory, definition.directory))
            .OrderByDescending(static definition => definition.directory.Length)
            .FirstOrDefault();
    }

    private static bool IsWithin(string directory, string ancestor)
        => string.IsNullOrEmpty(ancestor) ||
           string.Equals(directory, ancestor, StringComparison.OrdinalIgnoreCase) ||
           directory.StartsWith(ancestor + "/", StringComparison.OrdinalIgnoreCase);

    private static bool IsPluginPath(string relativePath)
        => relativePath.StartsWith("Plugins/", StringComparison.OrdinalIgnoreCase) ||
           relativePath.Contains("/Plugins/", StringComparison.OrdinalIgnoreCase);

    private static string GetAbsolutePath(string relativePath)
        => Path.Combine(AssetManager.assetRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));

    private static ScriptAssemblyInput[] ValidateAndOrderAssemblies(
        IReadOnlyDictionary<string, AssemblyBuilder> builders)
    {
        foreach (AssemblyBuilder builder in builders.Values)
        {
            foreach (string reference in builder.definition.references)
            {
                if (!builders.TryGetValue(reference, out AssemblyBuilder? dependency))
                {
                    throw new InvalidDataException(
                        $"Script assembly '{builder.definition.name}' references unknown assembly '{reference}'.");
                }
                if (builder.definition.scope == ScriptAssemblyScope.Runtime &&
                    dependency.definition.scope == ScriptAssemblyScope.Editor)
                {
                    throw new InvalidDataException(
                        $"Runtime script assembly '{builder.definition.name}' cannot reference editor assembly '{reference}'.");
                }
            }
        }

        var ordered = new List<ScriptAssemblyInput>(builders.Count);
        var states = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (string name in builders.Keys.OrderBy(static value => value, StringComparer.Ordinal))
            Visit(name);
        return ordered.ToArray();

        void Visit(string name)
        {
            int state = states.GetValueOrDefault(name);
            if (state == 2)
                return;
            if (state == 1)
                throw new InvalidDataException($"Script assembly reference cycle contains '{name}'.");
            states[name] = 1;
            AssemblyBuilder builder = builders[name];
            foreach (string reference in builder.definition.references)
                Visit(reference);
            states[name] = 2;
            ordered.Add(new ScriptAssemblyInput(
                builder.definition.name,
                builder.definition.scope,
                builder.sources.OrderBy(static value => value.sourcePath, StringComparer.Ordinal).ToArray(),
                builder.definition.references,
                builder.definition.defines,
                builder.definition.nullable,
                builder.definition.allowUnsafe,
                builder.definition.artifactKey));
        }
    }

    private sealed class AssemblyBuilder(ScriptAssemblyDefinition definition)
    {
        internal ScriptAssemblyDefinition definition { get; } = definition;
        internal List<ScriptSourceInput> sources { get; } = [];
    }
}

internal sealed record ScriptAssemblyDefinition(
    string name,
    string directory,
    ScriptAssemblyScope scope,
    IReadOnlyList<string> references,
    IReadOnlyList<string> defines,
    bool nullable,
    bool allowUnsafe,
    string artifactKey);

internal sealed record ScriptAssemblyInput(
    string name,
    ScriptAssemblyScope scope,
    IReadOnlyList<ScriptSourceInput> sources,
    IReadOnlyList<string> references,
    IReadOnlyList<string> defines,
    bool nullable,
    bool allowUnsafe,
    string definitionArtifactKey);

internal sealed record ScriptSourceInput(
    string relativePath,
    string sourcePath,
    string snapshotPath,
    Guid persistentId,
    string artifactKey);

internal sealed record ScriptPluginInput(
    string sourcePath,
    string assemblyArtifactPath,
    string? symbolsArtifactPath,
    string? dependenciesArtifactPath,
    ScriptAssemblyScope scope,
    string artifactKey);
