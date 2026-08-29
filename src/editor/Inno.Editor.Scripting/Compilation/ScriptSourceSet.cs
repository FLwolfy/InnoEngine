using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

using Inno.Assets;
using Inno.Assets.Core;
using Inno.Assets.File;
using Inno.Assets.Plugins;
using Inno.Core.Assemblies;

namespace Inno.Editor.Scripting;

internal sealed record ScriptSourceSet(
    IReadOnlyList<string> gameSources,
    IReadOnlyList<string> editorSources,
    IReadOnlyList<ScriptAssemblyDefinition> definitions,
    IReadOnlyList<ScriptAssemblyInput> assemblies)
{
    private const string C_GAME_ASSEMBLY_NAME = "Inno.GameScripts";
    private const string C_EDITOR_ASSEMBLY_NAME = "Inno.EditorScripts";

    internal static ScriptSourceSet Discover()
    {
        if (!AssetManager.isInitialized)
            throw new InvalidOperationException("Script discovery requires the Asset Database.");

        AssetSourceMountTransaction? candidateAssets = PluginManager.compilationAssets;
        AssetFileEntry[] entries = (candidateAssets?.GetFileSystemEntries(includeDirectories: false)
                ?? AssetManager.GetFileSystemEntries(includeDirectories: false))
            .OrderBy(static entry => entry.assetPath.source.value, StringComparer.Ordinal)
            .ThenBy(static entry => entry.assetPath.localPath, StringComparer.Ordinal)
            .ToArray();
        ScriptAssemblyDefinition[] explicitDefinitions = entries
            .Where(static entry => string.Equals(entry.extension, ".iasmdef", StringComparison.OrdinalIgnoreCase))
            .Select(entry => ParseDefinition(entry, candidateAssets))
            .OrderBy(static definition => definition.source.value, StringComparer.Ordinal)
            .ThenBy(static definition => definition.directory, StringComparer.Ordinal)
            .ToArray();

        var builders = new Dictionary<string, AssemblyBuilder>(StringComparer.OrdinalIgnoreCase);
        foreach (ScriptAssemblyDefinition definition in explicitDefinitions)
            AddBuilder(builders, definition);
        if (builders.ContainsKey(C_GAME_ASSEMBLY_NAME) || builders.ContainsKey(C_EDITOR_ASSEMBLY_NAME))
        {
            throw new InvalidDataException(
                $"Assembly definitions cannot use the reserved {C_GAME_ASSEMBLY_NAME} or {C_EDITOR_ASSEMBLY_NAME} names.");
        }

        PluginArchiveCandidate[] codePlugins = PluginManager.compilationPlugins
            .Where(candidate => entries.Any(entry =>
                entry.assetPath.source == candidate.sourceMount.id
                && string.Equals(entry.extension, ".cs", StringComparison.OrdinalIgnoreCase)))
            .ToArray();
        ValidateManifestDefinitions(entries, explicitDefinitions, codePlugins);

        var pluginDefaultNames = new Dictionary<string, PluginDefaultNames>(StringComparer.Ordinal);
        foreach (PluginArchiveCandidate plugin in codePlugins)
        {
            string runtimeName = CreatePluginAssemblyName(plugin.manifest.pluginId);
            string editorName = runtimeName + ".Editor";
            pluginDefaultNames.Add(plugin.manifest.pluginId, new PluginDefaultNames(runtimeName, editorName));
            AddBuilder(builders, CreateDefaultDefinition(
                runtimeName,
                plugin.sourceMount.id,
                ScriptAssemblyScope.Runtime,
                AssemblyDomain.InnoPlugin,
                plugin.manifest.pluginId,
                "plugin-default-runtime"));
            AddBuilder(builders, CreateDefaultDefinition(
                editorName,
                plugin.sourceMount.id,
                ScriptAssemblyScope.Editor,
                AssemblyDomain.InnoPlugin,
                plugin.manifest.pluginId,
                "plugin-default-editor"));
        }

        AddBuilder(builders, CreateDefaultDefinition(
            C_GAME_ASSEMBLY_NAME,
            AssetSourceId.project,
            ScriptAssemblyScope.Runtime,
            AssemblyDomain.InnoScripting,
            string.Empty,
            "project-default-runtime"));
        AddBuilder(builders, CreateDefaultDefinition(
            C_EDITOR_ASSEMBLY_NAME,
            AssetSourceId.project,
            ScriptAssemblyScope.Editor,
            AssemblyDomain.InnoScripting,
            string.Empty,
            "project-default-editor"));

        ConfigureDefaultReferences(builders, explicitDefinitions, codePlugins, pluginDefaultNames);
        foreach (AssetFileEntry entry in entries.Where(static entry =>
                     string.Equals(entry.extension, ".cs", StringComparison.OrdinalIgnoreCase)))
        {
            ScriptAssemblyDefinition? definition = FindNearestDefinition(entry.assetPath, explicitDefinitions);
            ScriptAssemblyScope scope = definition?.scope
                ?? (entry.assetPath.localPath.EndsWith(".editor.cs", StringComparison.OrdinalIgnoreCase)
                    ? ScriptAssemblyScope.Editor
                    : ScriptAssemblyScope.Runtime);
            AssemblyBuilder builder;
            if (definition is not null)
            {
                builder = builders[definition.name];
            }
            else if (entry.assetPath.source == AssetSourceId.project)
            {
                builder = builders[scope == ScriptAssemblyScope.Editor
                    ? C_EDITOR_ASSEMBLY_NAME
                    : C_GAME_ASSEMBLY_NAME];
            }
            else
            {
                if (!PluginManager.TryGetCompilationPlugin(
                        entry.assetPath.source,
                        out PluginArchiveCandidate? plugin)
                    || plugin is null
                    || !pluginDefaultNames.TryGetValue(plugin.manifest.pluginId, out PluginDefaultNames names))
                {
                    throw new InvalidDataException(
                        $"Script '{entry.relativePath}' does not belong to an active trusted Plugin.");
                }
                builder = builders[scope == ScriptAssemblyScope.Editor ? names.editor : names.runtime];
            }
            builder.sources.Add(CreateSourceInput(entry, candidateAssets));
        }

        ScriptAssemblyInput[] assemblies = ValidateAndOrderAssemblies(builders);
        return new ScriptSourceSet(
            builders[C_GAME_ASSEMBLY_NAME].sources.Select(static source => source.sourcePath).ToArray(),
            builders[C_EDITOR_ASSEMBLY_NAME].sources.Select(static source => source.sourcePath).ToArray(),
            explicitDefinitions,
            assemblies);
    }

    private static ScriptAssemblyDefinition ParseDefinition(
        AssetFileEntry entry,
        AssetSourceMountTransaction? candidateAssets)
    {
        ScriptAssemblyDefinitionAsset asset = candidateAssets is null
            ? AssetManager.Load<ScriptAssemblyDefinitionAsset>(entry.assetPath)
            : candidateAssets.Load<ScriptAssemblyDefinitionAsset>(entry.assetPath);
        bool hasInfo = candidateAssets is null
            ? AssetManager.TryGetInfo(entry.relativePath, out AssetInfo? info)
            : candidateAssets.TryGetInfo(entry.assetPath, out info);
        if (!hasInfo || info is null)
            throw new InvalidDataException($"Script assembly definition '{entry.relativePath}' has no metadata.");
        bool isPlugin = entry.assetPath.source != AssetSourceId.project;
        return new ScriptAssemblyDefinition(
            asset.assemblyName,
            entry.assetPath.source,
            Path.GetDirectoryName(entry.assetPath.localPath)?.Replace('\\', '/') ?? string.Empty,
            asset.scope,
            isPlugin ? AssemblyDomain.InnoPlugin : AssemblyDomain.InnoScripting,
            isPlugin ? entry.assetPath.source.value : string.Empty,
            asset.references,
            asset.defines,
            asset.nullable,
            asset.allowUnsafe,
            info.artifactKey.value);
    }

    private static ScriptSourceInput CreateSourceInput(
        AssetFileEntry entry,
        AssetSourceMountTransaction? candidateAssets)
    {
        bool hasInfo = candidateAssets is null
            ? AssetManager.TryGetInfo(entry.relativePath, out AssetInfo? info)
            : candidateAssets.TryGetInfo(entry.assetPath, out info);
        if (!hasInfo
            || info is null
            || info.persistentId == Guid.Empty
            || !(candidateAssets is null
                ? AssetManager.TryGetArtifact(info.persistentId, "source", out AssetArtifactInfo? source)
                : candidateAssets.TryGetArtifact(info.persistentId, "source", out source))
            || source is null)
        {
            throw new InvalidDataException($"Script source '{entry.relativePath}' has no committed source artifact.");
        }
        IReadOnlyList<AssetSourceMount> mounts = candidateAssets?.sourceMounts ?? AssetManager.sourceMounts;
        AssetSourceMount mount = mounts.Single(candidate => candidate.id == entry.assetPath.source);
        return new ScriptSourceInput(
            entry.assetPath,
            mount.Resolve(entry.assetPath.localPath),
            source.absolutePath,
            info.persistentId,
            info.artifactKey.value);
    }

    private static void ValidateManifestDefinitions(
        IReadOnlyList<AssetFileEntry> entries,
        IReadOnlyList<ScriptAssemblyDefinition> definitions,
        IReadOnlyList<PluginArchiveCandidate> plugins)
    {
        _ = definitions;
        foreach (PluginArchiveCandidate plugin in plugins)
        {
            string[] declared = plugin.manifest.assemblyDefinitions
                .Select(static path => path.StartsWith("Assets/", StringComparison.Ordinal)
                    ? path["Assets/".Length..]
                    : path)
                .OrderBy(static path => path, StringComparer.Ordinal)
                .ToArray();
            string[] discovered = entries
                .Where(entry => entry.assetPath.source == plugin.sourceMount.id
                    && string.Equals(entry.extension, ".iasmdef", StringComparison.OrdinalIgnoreCase))
                .Select(static entry => entry.assetPath.localPath)
                .OrderBy(static path => path, StringComparer.Ordinal)
                .ToArray();
            if (!declared.SequenceEqual(discovered, StringComparer.Ordinal))
            {
                throw new InvalidDataException(
                    $"Plugin '{plugin.manifest.pluginId}' assemblyDefinitions do not match its imported .iasmdef sources.");
            }
        }
    }

    private static void ConfigureDefaultReferences(
        IReadOnlyDictionary<string, AssemblyBuilder> builders,
        IReadOnlyList<ScriptAssemblyDefinition> explicitDefinitions,
        IReadOnlyList<PluginArchiveCandidate> plugins,
        IReadOnlyDictionary<string, PluginDefaultNames> defaultNames)
    {
        AssemblyBuilder game = builders[C_GAME_ASSEMBLY_NAME];
        AssemblyBuilder editor = builders[C_EDITOR_ASSEMBLY_NAME];
        foreach (PluginArchiveCandidate plugin in plugins)
        {
            PluginDefaultNames names = defaultNames[plugin.manifest.pluginId];
            AssemblyBuilder runtime = builders[names.runtime];
            AssemblyBuilder pluginEditor = builders[names.editor];
            pluginEditor.references.Add(names.runtime);
            foreach (string dependencyId in plugin.manifest.dependencies)
            {
                foreach (ScriptAssemblyDefinition dependency in explicitDefinitions.Where(definition =>
                             definition.ownerPluginId == dependencyId))
                {
                    if (dependency.scope == ScriptAssemblyScope.Runtime)
                        runtime.references.Add(dependency.name);
                    pluginEditor.references.Add(dependency.name);
                }
                if (defaultNames.TryGetValue(dependencyId, out PluginDefaultNames dependencyDefaults))
                {
                    runtime.references.Add(dependencyDefaults.runtime);
                    pluginEditor.references.Add(dependencyDefaults.runtime);
                    pluginEditor.references.Add(dependencyDefaults.editor);
                }
            }
            game.references.Add(names.runtime);
            editor.references.Add(names.runtime);
            editor.references.Add(names.editor);
            foreach (ScriptAssemblyDefinition definition in explicitDefinitions.Where(candidate =>
                         candidate.ownerPluginId == plugin.manifest.pluginId))
            {
                if (definition.scope == ScriptAssemblyScope.Runtime)
                    game.references.Add(definition.name);
                editor.references.Add(definition.name);
            }
        }
        editor.references.Add(C_GAME_ASSEMBLY_NAME);
    }

    private static ScriptAssemblyDefinition? FindNearestDefinition(
        AssetPath path,
        IReadOnlyList<ScriptAssemblyDefinition> definitions)
    {
        string directory = Path.GetDirectoryName(path.localPath)?.Replace('\\', '/') ?? string.Empty;
        return definitions
            .Where(definition => definition.source == path.source && IsWithin(directory, definition.directory))
            .OrderByDescending(static definition => definition.directory.Length)
            .FirstOrDefault();
    }

    private static ScriptAssemblyInput[] ValidateAndOrderAssemblies(
        IReadOnlyDictionary<string, AssemblyBuilder> builders)
    {
        foreach (AssemblyBuilder builder in builders.Values)
        {
            foreach (string reference in builder.references.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!builders.TryGetValue(reference, out AssemblyBuilder? dependency))
                {
                    throw new InvalidDataException(
                        $"Script assembly '{builder.definition.name}' references unknown assembly '{reference}'.");
                }
                if (builder.definition.scope == ScriptAssemblyScope.Runtime
                    && dependency.definition.scope == ScriptAssemblyScope.Editor)
                {
                    throw new InvalidDataException(
                        $"Runtime script assembly '{builder.definition.name}' cannot reference editor assembly '{reference}'.");
                }
                ValidateDomainDependency(builder.definition, dependency.definition);
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
            foreach (string reference in builder.references.Distinct(StringComparer.OrdinalIgnoreCase))
                Visit(reference);
            states[name] = 2;
            ordered.Add(new ScriptAssemblyInput(
                builder.definition.name,
                builder.definition.scope,
                builder.definition.domain,
                builder.definition.ownerPluginId,
                builder.sources.OrderBy(static value => value.assetPath.ToString(), StringComparer.Ordinal).ToArray(),
                builder.references.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                builder.definition.defines,
                builder.definition.nullable,
                builder.definition.allowUnsafe,
                builder.definition.artifactKey));
        }
    }

    private static void ValidateDomainDependency(
        ScriptAssemblyDefinition assembly,
        ScriptAssemblyDefinition dependency)
    {
        if (assembly.domain != AssemblyDomain.InnoPlugin)
            return;
        if (dependency.domain != AssemblyDomain.InnoPlugin)
        {
            throw new InvalidDataException(
                $"Plugin assembly '{assembly.name}' cannot reference project assembly '{dependency.name}'.");
        }
        if (string.Equals(assembly.ownerPluginId, dependency.ownerPluginId, StringComparison.Ordinal))
            return;
        if (!PluginManager.TryGetCompilationPlugin(
                new AssetSourceId(assembly.ownerPluginId),
                out PluginArchiveCandidate? owner)
            || owner is null
            || !owner.manifest.dependencies.Contains(dependency.ownerPluginId, StringComparer.Ordinal))
        {
            throw new InvalidDataException(
                $"Plugin '{assembly.ownerPluginId}' must declare dependency '{dependency.ownerPluginId}' " +
                $"before assembly '{assembly.name}' can reference '{dependency.name}'.");
        }
    }

    private static ScriptAssemblyDefinition CreateDefaultDefinition(
        string name,
        AssetSourceId source,
        ScriptAssemblyScope scope,
        AssemblyDomain domain,
        string ownerPluginId,
        string artifactKey)
        => new(
            name,
            source,
            string.Empty,
            scope,
            domain,
            ownerPluginId,
            [],
            ["DEBUG", "TRACE"],
            nullable: true,
            allowUnsafe: false,
            artifactKey);

    private static void AddBuilder(
        IDictionary<string, AssemblyBuilder> builders,
        ScriptAssemblyDefinition definition)
    {
        if (!builders.TryAdd(definition.name, new AssemblyBuilder(definition)))
            throw new InvalidDataException($"Script assembly name '{definition.name}' is declared more than once.");
    }

    private static string CreatePluginAssemblyName(string pluginId)
    {
        var builder = new StringBuilder("Inno.Plugin.");
        foreach (string segment in pluginId.Split(['.', '-', '_'], StringSplitOptions.RemoveEmptyEntries))
        {
            builder.Append(char.ToUpperInvariant(segment[0]));
            for (int i = 1; i < segment.Length; i++)
                builder.Append(char.IsLetterOrDigit(segment[i]) ? segment[i] : '_');
        }
        return builder.ToString();
    }

    private static bool IsWithin(string directory, string ancestor)
        => string.IsNullOrEmpty(ancestor)
           || string.Equals(directory, ancestor, StringComparison.OrdinalIgnoreCase)
           || directory.StartsWith(ancestor + "/", StringComparison.OrdinalIgnoreCase);

    private sealed class AssemblyBuilder
    {
        internal AssemblyBuilder(ScriptAssemblyDefinition definition)
        {
            this.definition = definition;
            references.AddRange(definition.references);
        }

        internal ScriptAssemblyDefinition definition { get; }
        internal List<ScriptSourceInput> sources { get; } = [];
        internal List<string> references { get; } = [];
    }

    private readonly record struct PluginDefaultNames(string runtime, string editor);
}

internal sealed record ScriptAssemblyDefinition(
    string name,
    AssetSourceId source,
    string directory,
    ScriptAssemblyScope scope,
    AssemblyDomain domain,
    string ownerPluginId,
    IReadOnlyList<string> references,
    IReadOnlyList<string> defines,
    bool nullable,
    bool allowUnsafe,
    string artifactKey);

internal sealed record ScriptAssemblyInput(
    string name,
    ScriptAssemblyScope scope,
    AssemblyDomain domain,
    string ownerPluginId,
    IReadOnlyList<ScriptSourceInput> sources,
    IReadOnlyList<string> references,
    IReadOnlyList<string> defines,
    bool nullable,
    bool allowUnsafe,
    string definitionArtifactKey);

internal sealed record ScriptSourceInput(
    AssetPath assetPath,
    string sourcePath,
    string snapshotPath,
    Guid persistentId,
    string artifactKey)
{
    internal string relativePath => assetPath.ToString();
}
