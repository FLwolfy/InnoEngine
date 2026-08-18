using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace Inno.Editor.Scripting;

internal static class ScriptProjectGenerator
{
    private static readonly Guid C_GAME_PROJECT_ID = Guid.Parse("F61A6A9D-C87D-4964-BE8A-A28361A1D3A1");
    private static readonly Guid C_EDITOR_PROJECT_ID = Guid.Parse("CA7049EB-739C-4EB8-A994-F937E71BCFC4");

    internal static void Generate(ScriptManagerOptions options)
    {
        Directory.CreateDirectory(options.projectRootDirectory);
        DeleteStaleGeneratedGlobalUsings(options.ideDirectory);
        ScriptSourceSet sources = ScriptSourceSet.Discover(options.assetDirectory);
        ScriptApiProfile runtimeApi = ScriptPluginMetadata.AddGlobalUsings(
            ScriptApiCatalog.Build(includeEditor: false),
            sources.runtimePlugins);
        ScriptApiProfile editorApi = ScriptPluginMetadata.AddGlobalUsings(
            ScriptApiCatalog.Build(includeEditor: true),
            sources.runtimePlugins.Concat(sources.editorPlugins));
        ScriptApiReferenceSet runtimeApiReferences = ScriptApiReferenceBuilder.Build(options, runtimeApi);
        ScriptApiReferenceSet editorApiReferences = ScriptApiReferenceBuilder.Build(
            options,
            editorApi,
            runtimeApi,
            runtimeApiReferences);
        string apiMapDirectory = Path.Combine(options.ideDirectory, "ScriptApiMaps");
        Directory.CreateDirectory(apiMapDirectory);
        string gameApiMapPath = Path.Combine(
            apiMapDirectory,
            "Inno.GameScripts" + ScriptApiMapBuilder.C_FILE_EXTENSION);
        string editorApiMapPath = Path.Combine(
            apiMapDirectory,
            "Inno.EditorScripts" + ScriptApiMapBuilder.C_FILE_EXTENSION);
        File.WriteAllText(gameApiMapPath, ScriptApiMapBuilder.Build(runtimeApi));
        File.WriteAllText(editorApiMapPath, ScriptApiMapBuilder.Build(editorApi));
        string codeAnalysisPath = CopyCodeAnalysisAssembly(options.ideDirectory);
        string gameProjectPath = Path.Combine(options.projectRootDirectory, "Inno.GameScripts.csproj");
        string editorProjectPath = Path.Combine(options.projectRootDirectory, "Inno.EditorScripts.csproj");

        CreateProject(
            "Inno.GameScripts",
            "Assets/**/*.cs",
            "Assets/**/*.editor.cs",
            runtimeApi,
            runtimeApiReferences,
            sources.runtimePlugins,
            gameApiMapPath,
            codeAnalysisPath,
            projectReference: null)
            .Save(gameProjectPath);
        CreateProject(
            "Inno.EditorScripts",
            "Assets/**/*.editor.cs",
            exclude: null,
            editorApi,
            editorApiReferences,
            sources.runtimePlugins.Concat(sources.editorPlugins).ToArray(),
            editorApiMapPath,
            codeAnalysisPath,
            "Inno.GameScripts.csproj")
            .Save(editorProjectPath);
        File.WriteAllText(
            Path.Combine(options.projectRootDirectory, "InnoProject.sln"),
            CreateSolution());
    }

    private static void DeleteStaleGeneratedGlobalUsings(string ideDirectory)
    {
        if (!Directory.Exists(ideDirectory))
            return;
        foreach (string path in Directory.EnumerateFiles(
                     ideDirectory,
                     "*.GlobalUsings.g.cs",
                     SearchOption.AllDirectories))
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
        string include,
        string? exclude,
        ScriptApiProfile api,
        ScriptApiReferenceSet apiReferences,
        IReadOnlyList<string> plugins,
        string apiMapPath,
        string codeAnalysisPath,
        string? projectReference)
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
            new XElement("Nullable", "enable"),
            new XElement("LangVersion", "latest"));
        var compile = new XElement("Compile", new XAttribute("Include", include));
        if (exclude is not null)
            compile.Add(new XAttribute("Exclude", exclude));
        var compileGroup = new XElement("ItemGroup", compile);

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
        if (api.globalUsings.Count > 0)
        {
            project.Add(new XElement("ItemGroup",
                api.globalUsings.Select(static value =>
                    new XElement("Using", new XAttribute("Include", value)))));
        }
        project.Add(folderGroup);
        if (projectReference is not null)
        {
            project.Add(new XElement("ItemGroup",
                new XElement("ProjectReference", new XAttribute("Include", projectReference))));
        }
        project.Add(new XElement("Import",
            new XAttribute("Project", "Sdk.targets"),
            new XAttribute("Sdk", "Microsoft.NET.Sdk")));
        return new XDocument(project);
    }

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

    private static string CreateSolution()
    {
        string gameProjectId = "{" + C_GAME_PROJECT_ID.ToString().ToUpperInvariant() + "}";
        string editorProjectId = "{" + C_EDITOR_PROJECT_ID.ToString().ToUpperInvariant() + "}";
        return $$"""
            Microsoft Visual Studio Solution File, Format Version 12.00
            # Visual Studio Version 17
            VisualStudioVersion = 17.0.31903.59
            MinimumVisualStudioVersion = 10.0.40219.1
            Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "Inno.GameScripts", "Inno.GameScripts.csproj", "{{gameProjectId}}"
            EndProject
            Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "Inno.EditorScripts", "Inno.EditorScripts.csproj", "{{editorProjectId}}"
            EndProject
            Global
            	GlobalSection(SolutionConfigurationPlatforms) = preSolution
            		Debug|Any CPU = Debug|Any CPU
            	EndGlobalSection
            	GlobalSection(ProjectConfigurationPlatforms) = postSolution
            		{{gameProjectId}}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
            		{{gameProjectId}}.Debug|Any CPU.Build.0 = Debug|Any CPU
            		{{editorProjectId}}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
            		{{editorProjectId}}.Debug|Any CPU.Build.0 = Debug|Any CPU
            	EndGlobalSection
            EndGlobal
            """;
    }
}
