using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Inno.Tooling.Architecture;

internal static partial class ArchitectureRules
{
    private static readonly string[] S_SCAN_ROOTS = ["src", "native", "build", "tests"];
    private static readonly string[] S_FORBIDDEN_PLAYER_PROJECT_FRAGMENTS =
    [
        "Inno.Editor.",
        "Inno.Build",
        "Inno.Scripting.Compiler",
        "Inno.Scripting.Reload",
        "Inno.Assets.Pipeline",
        "Inno.Plugins.Authoring",
        "Toolchains"
    ];
    private static readonly string[] S_FORBIDDEN_IMPLEMENTATION_WORDS =
    [
        "Legacy",
        "Compatibility",
        "Migration",
        "Former",
        "Deprecated"
    ];
    private static readonly string[] S_REMOVED_PROJECT_NAMES =
    [
        "Inno.Core.Framework",
        "Inno.Core.Assemblies",
        "Inno.Core.Reflection",
        "Inno.Core.Scripting",
        "Inno.Engine.Scene",
        "Inno.Rendering.Core",
        "Inno.Native.Dll"
    ];

    internal static void Validate(string repositoryRoot, ICollection<string> failures)
    {
        ValidateRepositorySources(repositoryRoot, failures);
        Dictionary<string, ProjectNode> graph = LoadProjectGraph(repositoryRoot, failures);
        ValidateCycles(repositoryRoot, graph, failures);
        ValidatePlayerClosure(repositoryRoot, graph, failures);
        ValidateRemovedProjects(repositoryRoot, failures);
    }

    private static void ValidateRepositorySources(string repositoryRoot, ICollection<string> failures)
    {
        foreach (string rootName in S_SCAN_ROOTS)
        {
            string root = Path.Combine(repositoryRoot, rootName);
            if (!Directory.Exists(root))
                continue;
            foreach (string path in EnumerateFiles(root, "*.cs"))
            {
                string relative = Relative(repositoryRoot, path);
                string source = File.ReadAllText(path);
                if (IsGenerated(source))
                    continue;
                ValidateForbiddenImplementationNames(relative, source, failures);
                if (relative.StartsWith("tests/", StringComparison.Ordinal))
                    ValidateTestSource(relative, source, failures);
                else
                    ValidateProductionSource(relative, source, failures);
            }
        }
    }

    private static void ValidateForbiddenImplementationNames(
        string relative,
        string source,
        ICollection<string> failures)
    {
        string fileName = Path.GetFileNameWithoutExtension(relative);
        foreach (string word in S_FORBIDDEN_IMPLEMENTATION_WORDS)
        {
            if (fileName.Contains(word, StringComparison.OrdinalIgnoreCase))
                failures.Add($"{relative}: implementation file names cannot contain '{word}'.");
        }
        foreach (Match match in DeclaredTypePattern().Matches(source))
        {
            string name = match.Groups[2].Value;
            foreach (string word in S_FORBIDDEN_IMPLEMENTATION_WORDS)
            {
                if (name.Contains(word, StringComparison.OrdinalIgnoreCase))
                    failures.Add($"{relative}: declared implementation '{name}' cannot contain '{word}'.");
            }
        }
    }

    private static void ValidateProductionSource(
        string relative,
        string source,
        ICollection<string> failures)
    {
        if (relative.StartsWith("tools/Inno.Tooling.Architecture/", StringComparison.Ordinal))
            return;
        if (StaticManagerDeclarationPattern().IsMatch(source))
            failures.Add($"{relative}: process-wide static Manager ownership is forbidden.");
        if (StaticLogFacadeCallPattern().IsMatch(source))
            failures.Add($"{relative}: engine implementation must use an explicitly owned Logger.");
        int lineNumber = 0;
        using var reader = new StringReader(source);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            lineNumber++;
            string trimmed = line.TrimStart();
            if (trimmed.StartsWith("///", StringComparison.Ordinal) ||
                trimmed.StartsWith("//", StringComparison.Ordinal))
            {
                continue;
            }
            if (PublicNativeSignaturePattern().IsMatch(line) &&
                !relative.StartsWith("native/", StringComparison.Ordinal))
            {
                failures.Add($"{relative}:{lineNumber}: public or protected API leaks a native implementation type.");
            }
        }
    }

    private static void ValidateTestSource(
        string relative,
        string source,
        ICollection<string> failures)
    {
        if (source.Contains("InternalsVisibleTo", StringComparison.Ordinal))
            failures.Add($"{relative}: tests cannot introduce friend-assembly access.");
        if (NonPublicReflectionPattern().IsMatch(source))
            failures.Add($"{relative}: tests cannot penetrate non-public implementation state through reflection.");
    }

    private static Dictionary<string, ProjectNode> LoadProjectGraph(
        string repositoryRoot,
        ICollection<string> failures)
    {
        var graph = new Dictionary<string, ProjectNode>(StringComparer.OrdinalIgnoreCase);
        foreach (string path in EnumerateFiles(repositoryRoot, "*.csproj"))
        {
            string relative = Relative(repositoryRoot, path);
            if (relative.StartsWith("extern/", StringComparison.Ordinal) ||
                relative.StartsWith("demo/", StringComparison.Ordinal))
            {
                continue;
            }
            XDocument document = XDocument.Load(path);
            string name = document.Descendants("AssemblyName").Select(static value => value.Value.Trim())
                .FirstOrDefault(static value => value.Length > 0)
                ?? Path.GetFileNameWithoutExtension(path);
            var node = new ProjectNode(path, relative, name);
            graph.Add(Path.GetFullPath(path), node);
        }
        foreach (ProjectNode node in graph.Values)
        {
            XDocument document = XDocument.Load(node.path);
            ValidateProjectProperties(node, document, failures);
            foreach (XElement reference in document.Descendants("ProjectReference"))
            {
                string? include = reference.Attribute("Include")?.Value;
                if (string.IsNullOrWhiteSpace(include))
                    continue;
                string normalizedInclude = include
                    .Replace('\\', Path.DirectorySeparatorChar)
                    .Replace('/', Path.DirectorySeparatorChar);
                string targetPath = Path.GetFullPath(normalizedInclude, Path.GetDirectoryName(node.path)!);
                if (!graph.TryGetValue(targetPath, out ProjectNode? target))
                {
                    failures.Add($"{node.relative}: project reference '{include}' does not resolve to a repository project.");
                    continue;
                }
                node.references.Add(target);
                ValidateReferenceBoundary(node, target, failures);
            }
        }
        return graph;
    }

    private static void ValidateProjectProperties(
        ProjectNode project,
        XDocument document,
        ICollection<string> failures)
    {
        foreach (XElement noWarn in document.Descendants("NoWarn"))
        {
            string value = noWarn.Value;
            if (value.Contains("CS1572", StringComparison.OrdinalIgnoreCase) ||
                value.Contains("CS1573", StringComparison.OrdinalIgnoreCase) ||
                value.Contains("CS1591", StringComparison.OrdinalIgnoreCase))
            {
                failures.Add($"{project.relative}: public XML contract warnings cannot be suppressed.");
            }
        }
    }

    private static void ValidateReferenceBoundary(
        ProjectNode project,
        ProjectNode target,
        ICollection<string> failures)
    {
        string sourcePath = project.relative;
        string targetPath = target.relative;
        if (sourcePath.StartsWith("native/", StringComparison.Ordinal) &&
            !targetPath.StartsWith("native/", StringComparison.Ordinal))
        {
            failures.Add($"{sourcePath}: Native cannot reference upper-layer project {targetPath}.");
        }
        if (sourcePath.StartsWith("src/core/", StringComparison.Ordinal) &&
            !targetPath.StartsWith("src/core/", StringComparison.Ordinal) &&
            !targetPath.StartsWith("src/extensibility/", StringComparison.Ordinal) &&
            !targetPath.Contains("Inno.Scripting.Api", StringComparison.Ordinal))
        {
            failures.Add($"{sourcePath}: Core cannot reference upper-layer project {targetPath}.");
        }
        if (sourcePath.StartsWith("build/", StringComparison.Ordinal) &&
            targetPath.StartsWith("src/editor/", StringComparison.Ordinal))
        {
            failures.Add($"{sourcePath}: Build cannot reference Editor project {targetPath}.");
        }
        if (sourcePath.Contains("Inno.Rendering/", StringComparison.Ordinal) &&
            (targetPath.Contains("ShaderGraph", StringComparison.Ordinal) ||
             targetPath.StartsWith("src/scene/", StringComparison.Ordinal) ||
             targetPath.StartsWith("src/editor/", StringComparison.Ordinal)))
        {
            failures.Add($"{sourcePath}: backend-neutral Rendering cannot reference {targetPath}.");
        }
        if (target.name.Contains("Inno.Native.Bgfx", StringComparison.Ordinal) &&
            !IsAllowedBgfxConsumer(project.name))
        {
            failures.Add($"{sourcePath}: BGFX native code is restricted to BGFX adapters and toolchains.");
        }
        if (target.name.Contains("Inno.Native.Sdl3", StringComparison.OrdinalIgnoreCase) &&
            !IsAllowedSdlConsumer(project.name))
        {
            failures.Add($"{sourcePath}: SDL3 native code is restricted to SDL3 adapters and toolchains.");
        }
    }

    private static void ValidateCycles(
        string repositoryRoot,
        IReadOnlyDictionary<string, ProjectNode> graph,
        ICollection<string> failures)
    {
        var states = new Dictionary<ProjectNode, VisitState>();
        var stack = new List<ProjectNode>();
        foreach (ProjectNode node in graph.Values)
            Visit(node);

        void Visit(ProjectNode node)
        {
            if (states.TryGetValue(node, out VisitState state))
            {
                if (state == VisitState.Visiting)
                {
                    int start = stack.IndexOf(node);
                    string cycle = string.Join(" -> ", stack.Skip(start).Append(node).Select(static value => value.name));
                    failures.Add($"{Relative(repositoryRoot, node.path)}: project reference cycle detected: {cycle}.");
                }
                return;
            }
            states[node] = VisitState.Visiting;
            stack.Add(node);
            foreach (ProjectNode target in node.references)
                Visit(target);
            stack.RemoveAt(stack.Count - 1);
            states[node] = VisitState.Visited;
        }
    }

    private static void ValidatePlayerClosure(
        string repositoryRoot,
        IReadOnlyDictionary<string, ProjectNode> graph,
        ICollection<string> failures)
    {
        ProjectNode? player = graph.Values.SingleOrDefault(static value => value.name == "Inno.Player");
        if (player is null)
        {
            failures.Add("src/runtime/Inno.Player: Player composition project is missing.");
            return;
        }
        var visited = new HashSet<ProjectNode>();
        var pending = new Stack<ProjectNode>();
        pending.Push(player);
        while (pending.Count > 0)
        {
            ProjectNode current = pending.Pop();
            if (!visited.Add(current))
                continue;
            if (!ReferenceEquals(current, player) &&
                S_FORBIDDEN_PLAYER_PROJECT_FRAGMENTS.Any(fragment =>
                    current.name.Contains(fragment, StringComparison.OrdinalIgnoreCase)))
            {
                failures.Add($"{Relative(repositoryRoot, player.path)}: Player dependency closure contains forbidden project {current.name}.");
            }
            foreach (ProjectNode target in current.references)
                pending.Push(target);
        }
    }

    private static void ValidateRemovedProjects(string repositoryRoot, ICollection<string> failures)
    {
        string solution = File.ReadAllText(Path.Combine(repositoryRoot, "InnoEngine.sln"));
        foreach (string removed in S_REMOVED_PROJECT_NAMES)
        {
            if (solution.Contains(removed, StringComparison.Ordinal))
                failures.Add($"InnoEngine.sln: removed project '{removed}' remains in the solution.");
        }
    }

    private static bool IsAllowedBgfxConsumer(string name)
        => name.StartsWith("Inno.Rendering.Bgfx", StringComparison.Ordinal) ||
           name.StartsWith("Inno.Build.Toolchains.Bgfx", StringComparison.Ordinal) ||
           name.StartsWith("Inno.Native.Bgfx", StringComparison.Ordinal) ||
           name.EndsWith(".Tests", StringComparison.Ordinal);

    private static bool IsAllowedSdlConsumer(string name)
        => name.StartsWith("Inno.Platform.Sdl3", StringComparison.Ordinal) ||
           name.StartsWith("Inno.Build.Toolchains.Sdl3", StringComparison.Ordinal) ||
           name.StartsWith("Inno.Native.Sdl3", StringComparison.Ordinal) ||
           name.EndsWith(".Tests", StringComparison.Ordinal);

    private static IEnumerable<string> EnumerateFiles(string root, string pattern)
    {
        foreach (string path in Directory.EnumerateFiles(root, pattern, SearchOption.AllDirectories))
        {
            string normalized = path.Replace('\\', '/');
            if (normalized.Contains("/bin/", StringComparison.Ordinal) ||
                normalized.Contains("/obj/", StringComparison.Ordinal) ||
                normalized.Contains("/extern/", StringComparison.Ordinal))
            {
                continue;
            }
            yield return path;
        }
    }

    private static bool IsGenerated(string source)
        => source.Contains("<auto-generated>", StringComparison.OrdinalIgnoreCase) ||
           source.Contains("[GeneratedCode", StringComparison.Ordinal);

    private static string Relative(string repositoryRoot, string path)
        => Path.GetRelativePath(repositoryRoot, path).Replace('\\', '/');

    [GeneratedRegex(@"\b(class|struct|interface|enum|record)\s+([A-Za-z_]\w*)")]
    private static partial Regex DeclaredTypePattern();

    [GeneratedRegex(@"\bstatic\s+class\s+(DiagnosticManager|LogManager|IdentityManager|JobSystemManager|AssemblyManager|TypeCacheManager|SerializationManager|ProjectSettingsManager|AssetManager|PluginManager|Shell)\b")]
    private static partial Regex StaticManagerDeclarationPattern();

    [GeneratedRegex(@"\bLog\.(Debug|Info|Warn|Error|Fatal)\s*\(")]
    private static partial Regex StaticLogFacadeCallPattern();

    [GeneratedRegex(@"\b(public|protected)\b[^\r\n;{]*(Inno\.Native\.|bgfx_|SDL[A-Z]|ImGuiPtr)")]
    private static partial Regex PublicNativeSignaturePattern();

    [GeneratedRegex(@"BindingFlags\s*\.[^\r\n;]*(NonPublic|Private)|(GetField|GetMethod|GetProperty|GetConstructor)\s*\([^\r\n;]*BindingFlags\s*\.[^\r\n;]*(NonPublic|Private)")]
    private static partial Regex NonPublicReflectionPattern();

    private sealed class ProjectNode(string path, string relative, string name)
    {
        internal string path { get; } = path;
        internal string relative { get; } = relative;
        internal string name { get; } = name;
        internal List<ProjectNode> references { get; } = [];
    }

    private enum VisitState
    {
        Visiting,
        Visited
    }
}
