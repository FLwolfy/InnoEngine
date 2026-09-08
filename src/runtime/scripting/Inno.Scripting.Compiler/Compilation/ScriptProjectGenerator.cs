using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using Inno.Assets;
using Inno.Assets.Pipeline;
using Inno.Plugins.Authoring;

namespace Inno.Scripting.Compiler;

internal static class ScriptProjectGenerator
{
    internal static void Generate(
        ScriptCompilerOptions options,
        AssetPipeline assets,
        PluginEnvironment plugins,
        ScriptCompilationResult? compiledGeneration)
    {
        Directory.CreateDirectory(options.projectRootDirectory);
        ScriptSourceSet sources = ScriptSourceSet.Discover(assets, plugins, includeEditor: true);
        ScriptAssemblyInput[] userAssemblies = sources.assemblies
            .Where(static assembly => assembly.domain == Inno.Extensibility.Modules.AssemblyDomain.InnoScripting)
            .ToArray();
        ScriptApiProfile runtimeApi = ScriptApiCatalog.Build(includeEditor: false);
        ScriptApiProfile editorApi = ScriptApiCatalog.Build(includeEditor: true);
        ScriptApiReferenceSet runtimeApiReferences = ScriptApiReferenceBuilder.Build(options, runtimeApi);
        ScriptApiReferenceSet editorApiReferences = ScriptApiReferenceBuilder.Build(
            options,
            editorApi,
            runtimeApi,
            runtimeApiReferences);
        string apiMapDirectory = Path.Combine(options.ideDirectory, "ScriptApiMaps");
        Directory.CreateDirectory(apiMapDirectory);
        string codeAnalysisPath = CopyCodeAnalysisAssembly(options.ideDirectory);
        foreach (ScriptAssemblyInput assembly in userAssemblies)
        {
            bool editor = assembly.scope == ScriptAssemblyScope.Editor;
            ScriptApiProfile api = editor ? editorApi : runtimeApi;
            ScriptApiReferenceSet references = editor ? editorApiReferences : runtimeApiReferences;
            string apiMapPath = Path.Combine(
                apiMapDirectory,
                assembly.name + ScriptApiMapBuilder.C_FILE_EXTENSION);
            File.WriteAllText(apiMapPath, ScriptApiMapBuilder.Build(api));
            CreateProject(
                    assembly.name,
                    ToProjectRelativePaths(
                        options,
                        assembly.sources.Select(static source => source.sourcePath).ToArray()),
                    api,
                    references,
                    ResolvePluginReferences(sources, assembly, compiledGeneration),
                    apiMapPath,
                    codeAnalysisPath,
                    assembly.references
                        .Where(reference => userAssemblies.Any(candidate => string.Equals(
                            candidate.name,
                            reference,
                            StringComparison.OrdinalIgnoreCase)))
                        .Select(static reference => reference + ".csproj")
                        .ToArray(),
                    assembly.defines,
                    assembly.nullable,
                    assembly.allowUnsafe)
                .Save(Path.Combine(options.projectRootDirectory, assembly.name + ".csproj"));
        }
        RemoveStalePluginProjectionFiles(options);
        File.WriteAllText(
            Path.Combine(options.projectRootDirectory, "InnoProject.sln"),
            CreateSolution(userAssemblies));
    }

    private static string[] ResolvePluginReferences(
        ScriptSourceSet sources,
        ScriptAssemblyInput userAssembly,
        ScriptCompilationResult? compiledGeneration)
    {
        if (compiledGeneration?.success != true || compiledGeneration.outputDirectory is null)
            return [];

        IReadOnlyDictionary<string, ScriptAssemblyInput> assemblies = sources.assemblies.ToDictionary(
            static assembly => assembly.name,
            StringComparer.OrdinalIgnoreCase);
        var pending = new Queue<string>(userAssembly.references);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var references = new List<string>();
        while (pending.Count > 0)
        {
            string name = pending.Dequeue();
            if (!visited.Add(name) || !assemblies.TryGetValue(name, out ScriptAssemblyInput? dependency))
                continue;
            foreach (string transitiveReference in dependency.references)
                pending.Enqueue(transitiveReference);
            if (dependency.domain != Inno.Extensibility.Modules.AssemblyDomain.InnoPlugin)
                continue;

            string path = Path.Combine(compiledGeneration.outputDirectory, dependency.name + ".dll");
            if (!File.Exists(path))
            {
                throw new InvalidDataException(
                    $"Compiled Plugin reference '{dependency.name}' is absent from generation " +
                    $"'{compiledGeneration.outputDirectory}'.");
            }
            references.Add(path);
        }
        return references
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static path => path, StringComparer.Ordinal)
            .ToArray();
    }

    private static void RemoveStalePluginProjectionFiles(ScriptCompilerOptions options)
    {
        foreach (string path in Directory.EnumerateFiles(
                     options.projectRootDirectory,
                     "Inno.Plugin.*.csproj",
                     SearchOption.TopDirectoryOnly))
        {
            File.Delete(path);
        }
        string apiMapDirectory = Path.Combine(options.ideDirectory, "ScriptApiMaps");
        if (!Directory.Exists(apiMapDirectory))
            return;
        foreach (string path in Directory.EnumerateFiles(
                     apiMapDirectory,
                     "Inno.Plugin.*" + ScriptApiMapBuilder.C_FILE_EXTENSION,
                     SearchOption.TopDirectoryOnly))
        {
            File.Delete(path);
        }
    }

    private static string CopyCodeAnalysisAssembly(string ideDirectory)
    {
        string analyzerDirectory = Path.Combine(ideDirectory, "Analyzers");
        Directory.CreateDirectory(analyzerDirectory);
        string sourcePath = typeof(LogicalScriptingApiAnalyzer).Assembly.Location;
        string destinationPath = Path.Combine(analyzerDirectory, Path.GetFileName(sourcePath));
        File.Copy(sourcePath, destinationPath, overwrite: true);
        return destinationPath;
    }

    private static XDocument CreateProject(
        string assemblyName,
        IReadOnlyList<string> sourcePaths,
        ScriptApiProfile api,
        ScriptApiReferenceSet apiReferences,
        IReadOnlyList<string> plugins,
        string apiMapPath,
        string codeAnalysisPath,
        IReadOnlyList<string> projectReferences,
        IReadOnlyList<string> defines,
        bool nullable,
        bool allowUnsafe)
    {
        var earlyPropertyGroup = new XElement("PropertyGroup",
            new XElement("BaseOutputPath", "Library/IDE/bin/" + assemblyName + "/"),
            new XElement("BaseIntermediateOutputPath", "Library/IDE/obj/" + assemblyName + "/"),
            new XElement("MSBuildProjectExtensionsPath", "Library/IDE/obj/" + assemblyName + "/"),
            new XElement("RestoreOutputPath", "Library/IDE/obj/" + assemblyName + "/"));
        var propertyGroup = new XElement("PropertyGroup",
            new XElement("TargetFramework", "net9.0"),
            new XElement("AssemblyName", assemblyName),
            new XElement("DefaultItemExcludes", "$(DefaultItemExcludes);Library/**"),
            new XElement("EnableDefaultItems", "false"),
            new XElement("EnableDefaultCompileItems", "false"),
            new XElement("ImplicitUsings", "disable"),
            new XElement("Nullable", nullable ? "enable" : "disable"),
            new XElement("AllowUnsafeBlocks", allowUnsafe ? "true" : "false"),
            new XElement("DefineConstants", string.Join(";", defines)),
            new XElement("LangVersion", "latest"));
        var compileGroup = new XElement(
            "ItemGroup",
            sourcePaths.Select(static path =>
                new XElement("Compile", new XAttribute("Include", path))));

        var referenceGroup = new XElement("ItemGroup");
        foreach (string path in apiReferences.ideReferencePaths)
            AddReference(referenceGroup, path);
        foreach (string path in plugins
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .OrderBy(static value => value, StringComparer.Ordinal))
        {
            AddReference(referenceGroup, path);
        }

        var folderGroup = new XElement("ItemGroup",
            new XElement("Folder", new XAttribute("Include", "Assets/")));
        var codeAnalysisGroup = new XElement("ItemGroup",
            new XElement("Analyzer",
                new XAttribute("Include", codeAnalysisPath),
                new XElement("Visible", "false")),
            new XElement("AdditionalFiles",
                new XAttribute("Include", apiMapPath),
                new XElement("Visible", "false")));
        var project = new XElement("Project",
            earlyPropertyGroup,
            new XElement("Import",
                new XAttribute("Project", "Sdk.props"),
                new XAttribute("Sdk", "Microsoft.NET.Sdk")),
            propertyGroup,
            compileGroup,
            referenceGroup,
            codeAnalysisGroup);
        project.Add(folderGroup);
        if (projectReferences.Count > 0)
        {
            project.Add(new XElement("ItemGroup",
                projectReferences.Select(static reference =>
                    new XElement("ProjectReference", new XAttribute("Include", reference)))));
        }
        project.Add(new XElement("Import",
            new XAttribute("Project", "Sdk.targets"),
            new XAttribute("Sdk", "Microsoft.NET.Sdk")));
        return new XDocument(project);
    }

    private static string[] ToProjectRelativePaths(
        ScriptCompilerOptions options,
        IReadOnlyList<string> absolutePaths)
        => absolutePaths
            .Select(path => Path.GetRelativePath(options.projectRootDirectory, path).Replace('\\', '/'))
            .OrderBy(static path => path, StringComparer.Ordinal)
            .ToArray();

    private static void AddReference(XElement group, string path)
    {
        var reference = new XElement("Reference",
            new XAttribute("Include", Path.GetFileNameWithoutExtension(path)),
            new XElement("HintPath", path),
            new XElement("Private", "false"));
        string documentationPath = Path.ChangeExtension(path, ".xml");
        if (File.Exists(documentationPath))
            reference.Add(new XElement("DocumentationFile", documentationPath));
        group.Add(reference);
    }

    private static string CreateSolution(IReadOnlyList<ScriptAssemblyInput> assemblies)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Microsoft Visual Studio Solution File, Format Version 12.00");
        builder.AppendLine("# Visual Studio Version 17");
        builder.AppendLine("VisualStudioVersion = 17.0.31903.59");
        builder.AppendLine("MinimumVisualStudioVersion = 10.0.40219.1");
        foreach (ScriptAssemblyInput assembly in assemblies)
        {
            string id = FormatProjectId(assembly.name);
            builder.AppendLine(
                $"Project(\"{{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}}\") = " +
                $"\"{assembly.name}\", \"{assembly.name}.csproj\", \"{id}\"");
            builder.AppendLine("EndProject");
        }
        builder.AppendLine("Global");
        builder.AppendLine("\tGlobalSection(SolutionConfigurationPlatforms) = preSolution");
        builder.AppendLine("\t\tDebug|Any CPU = Debug|Any CPU");
        builder.AppendLine("\tEndGlobalSection");
        builder.AppendLine("\tGlobalSection(ProjectConfigurationPlatforms) = postSolution");
        foreach (ScriptAssemblyInput assembly in assemblies)
        {
            string id = FormatProjectId(assembly.name);
            builder.AppendLine($"\t\t{id}.Debug|Any CPU.ActiveCfg = Debug|Any CPU");
            builder.AppendLine($"\t\t{id}.Debug|Any CPU.Build.0 = Debug|Any CPU");
        }
        builder.AppendLine("\tEndGlobalSection");
        builder.AppendLine("EndGlobal");
        return builder.ToString();
    }

    private static string FormatProjectId(string assemblyName)
    {
        byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes("Inno.ScriptProject:" + assemblyName));
        return "{" + new Guid(bytes.AsSpan(0, 16)).ToString().ToUpperInvariant() + "}";
    }
}
