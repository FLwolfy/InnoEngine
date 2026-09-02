using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Inno.Tooling.Architecture;

internal static partial class Program
{
    private static readonly string[] S_PRODUCTION_ROOTS = ["src", "native", "build", "tools"];
    private static readonly HashSet<string> S_IGNORED_DIRECTORIES = new(StringComparer.OrdinalIgnoreCase)
    {
        "bin",
        "obj",
        "Generated"
    };
    private static readonly HashSet<string> S_FORBIDDEN_DIRECTORY_NAMES = new(StringComparer.OrdinalIgnoreCase)
    {
        "Legacy",
        "Compatibility",
        "Migration",
        "Former",
        "Deprecated"
    };

    private static int Main(string[] arguments)
    {
        bool expandXml = arguments.Contains("--expand-xml", StringComparer.Ordinal);
        bool materializeInheritDoc = arguments.Contains("--materialize-inheritdoc", StringComparer.Ordinal);
        bool repairXmlContracts = arguments.Contains("--repair-xml-contracts", StringComparer.Ordinal);
        bool documentPublicApi = arguments.Contains("--document-public-api", StringComparer.Ordinal);
        string[] paths = arguments
            .Where(static argument => argument is not "--expand-xml"
                and not "--materialize-inheritdoc"
                and not "--repair-xml-contracts"
                and not "--document-public-api")
            .ToArray();
        string repositoryRoot = ResolveRepositoryRoot(paths);
        if (expandXml)
        {
            int changedFileCount = ExpandXmlDocumentation(repositoryRoot);
            Console.WriteLine($"Expanded XML documentation in {changedFileCount} file(s).");
            return 0;
        }
        if (materializeInheritDoc)
        {
            int changedFileCount = MaterializeInheritDoc(repositoryRoot);
            Console.WriteLine($"Materialized inherited XML documentation in {changedFileCount} file(s).");
            return 0;
        }
        if (repairXmlContracts)
        {
            int changedFileCount = RepairXmlContracts(repositoryRoot);
            Console.WriteLine($"Repaired XML contract tags in {changedFileCount} file(s).");
            return 0;
        }
        if (documentPublicApi)
        {
            int changedFileCount = PublicApiDocumenter.Document(repositoryRoot, S_PRODUCTION_ROOTS);
            Console.WriteLine($"Documented public API declarations in {changedFileCount} file(s).");
            return 0;
        }

        List<string> failures = [];

        foreach (string rootName in S_PRODUCTION_ROOTS)
        {
            string root = Path.Combine(repositoryRoot, rootName);
            if (!Directory.Exists(root))
                continue;
            ValidateDirectories(repositoryRoot, root, failures);
            ValidateSources(repositoryRoot, root, failures);
            ValidateProjects(repositoryRoot, root, failures);
        }

        ValidateProjectReferences(repositoryRoot, failures);
        ArchitectureRules.Validate(repositoryRoot, failures);
        if (failures.Count == 0)
        {
            Console.WriteLine("InnoEngine architecture validation passed.");
            return 0;
        }

        Console.Error.WriteLine($"InnoEngine architecture validation failed with {failures.Count} violation(s):");
        foreach (string failure in failures.Order(StringComparer.Ordinal))
            Console.Error.WriteLine("  " + failure);
        return 1;
    }

    private static string ResolveRepositoryRoot(IReadOnlyList<string> arguments)
    {
        string start = arguments.Count == 0
            ? Directory.GetCurrentDirectory()
            : Path.GetFullPath(arguments[0]);
        DirectoryInfo? current = new(start);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "InnoEngine.sln")))
                return current.FullName;
            current = current.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate the InnoEngine repository root.");
    }

    private static void ValidateDirectories(string repositoryRoot, string root, ICollection<string> failures)
    {
        foreach (string directory in EnumerateDirectories(root))
        {
            string name = Path.GetFileName(directory);
            if (S_FORBIDDEN_DIRECTORY_NAMES.Contains(name))
            {
                failures.Add($"{Relative(repositoryRoot, directory)}: forbidden compatibility directory name '{name}'.");
            }
        }
    }

    private static void ValidateSources(string repositoryRoot, string root, ICollection<string> failures)
    {
        foreach (string path in EnumerateFiles(root, "*.cs"))
        {
            string source = File.ReadAllText(path);
            if (IsGenerated(source))
                continue;
            string relative = Relative(repositoryRoot, path);
            if (relative.StartsWith("tools/Inno.Tooling.Architecture/", StringComparison.Ordinal))
                continue;
            PublicApiDocumentationValidator.Validate(relative, source, failures);
            AddSourceFailure(source.Contains("InternalsVisibleTo", StringComparison.Ordinal), relative,
                "friend assemblies are forbidden", failures);
            AddSourceFailure(source.Contains("[Obsolete", StringComparison.Ordinal), relative,
                "obsolete compatibility APIs are forbidden", failures);
            AddSourceFailure(source.Contains("TypeForwardedTo", StringComparison.Ordinal), relative,
                "type forwarding is forbidden", failures);
            AddSourceFailure(CompatibilityFieldPattern().IsMatch(source), relative,
                "schema compatibility fields are forbidden", failures);
            AddSourceFailure(GlobalUsingPattern().IsMatch(source), relative,
                "global using directives are forbidden", failures);
            AddSourceFailure(UninformativeXmlPattern().IsMatch(source), relative,
                "public API XML contains an uninformative generated placeholder", failures);

            int lineNumber = 0;
            using var reader = new StringReader(source);
            string? line;
            while ((line = reader.ReadLine()) is not null)
            {
                lineNumber++;
                if (SingleLineXmlPattern().IsMatch(line))
                {
                    failures.Add($"{relative}:{lineNumber}: XML documentation elements must use expanded multi-line form.");
                }
                if (SelfClosingInheritDocPattern().IsMatch(line))
                {
                    failures.Add($"{relative}:{lineNumber}: inheritdoc cannot replace an explicit public API contract.");
                }
            }
        }
    }

    private static int ExpandXmlDocumentation(string repositoryRoot)
    {
        int changedFileCount = 0;
        foreach (string rootName in S_PRODUCTION_ROOTS)
        {
            string root = Path.Combine(repositoryRoot, rootName);
            if (!Directory.Exists(root))
                continue;
            foreach (string path in EnumerateFiles(root, "*.cs"))
            {
                string source = File.ReadAllText(path);
                if (IsGenerated(source))
                    continue;
                string newline = source.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
                string expanded = ExpandableXmlPattern().Replace(source, match =>
                {
                    string indentation = match.Groups[1].Value;
                    string element = match.Groups[2].Value;
                    string attributes = match.Groups[3].Value;
                    string content = match.Groups[4].Value.Trim();
                    return string.Join(
                        newline,
                        $"{indentation}/// <{element}{attributes}>",
                        $"{indentation}/// {content}",
                        $"{indentation}/// </{element}>");
                });
                if (string.Equals(source, expanded, StringComparison.Ordinal))
                    continue;
                File.WriteAllText(path, expanded);
                changedFileCount++;
            }
        }
        return changedFileCount;
    }

    private static int MaterializeInheritDoc(string repositoryRoot)
    {
        int changedFileCount = 0;
        foreach (string rootName in S_PRODUCTION_ROOTS)
        {
            string root = Path.Combine(repositoryRoot, rootName);
            if (!Directory.Exists(root))
                continue;
            foreach (string path in EnumerateFiles(root, "*.cs"))
            {
                string source = File.ReadAllText(path);
                if (IsGenerated(source) || !source.Contains("<inheritdoc", StringComparison.Ordinal))
                    continue;
                string newline = source.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
                string[] lines = source.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
                bool changed = false;
                for (int i = 0; i < lines.Length; i++)
                {
                    if (!SelfClosingInheritDocPattern().IsMatch(lines[i]))
                        continue;
                    string indentation = lines[i][..lines[i].IndexOf("///", StringComparison.Ordinal)];
                    string declaration = ReadFollowingDeclaration(lines, i + 1);
                    lines[i] = CreateExplicitDocumentation(indentation, declaration);
                    changed = true;
                }
                if (!changed)
                    continue;
                File.WriteAllText(path, string.Join(newline, lines));
                changedFileCount++;
            }
        }
        return changedFileCount;
    }

    private static string ReadFollowingDeclaration(IReadOnlyList<string> lines, int start)
    {
        var builder = new StringBuilder();
        bool inAttribute = false;
        int parenthesisDepth = 0;
        bool sawParenthesis = false;
        for (int i = start; i < lines.Count; i++)
        {
            string trimmed = lines[i].Trim();
            if (trimmed.Length == 0)
                continue;
            if (trimmed.StartsWith("[", StringComparison.Ordinal) || inAttribute)
            {
                inAttribute = !trimmed.Contains(']');
                continue;
            }
            if (trimmed.StartsWith("///", StringComparison.Ordinal))
                continue;
            if (builder.Length > 0)
                builder.Append(' ');
            builder.Append(trimmed);
            sawParenthesis |= trimmed.Contains('(');
            parenthesisDepth += trimmed.Count(static character => character == '(');
            parenthesisDepth -= trimmed.Count(static character => character == ')');
            if (sawParenthesis && parenthesisDepth <= 0 ||
                !sawParenthesis && parenthesisDepth <= 0 &&
                (trimmed.Contains("=>", StringComparison.Ordinal) ||
                 trimmed.EndsWith(';') ||
                 trimmed.EndsWith('{') ||
                 trimmed.Contains(" where ", StringComparison.Ordinal)))
            {
                break;
            }
        }
        string declaration = builder.ToString();
        if (!sawParenthesis)
            return declaration;
        int opening = declaration.IndexOf('(');
        int depth = 0;
        for (int i = opening; i < declaration.Length; i++)
        {
            if (declaration[i] == '(')
                depth++;
            else if (declaration[i] == ')' && --depth == 0)
                return declaration[..(i + 1)];
        }
        return declaration;
    }

    private static int RepairXmlContracts(string repositoryRoot)
    {
        int changedFileCount = 0;
        foreach (string rootName in S_PRODUCTION_ROOTS)
        {
            string root = Path.Combine(repositoryRoot, rootName);
            if (!Directory.Exists(root))
                continue;
            foreach (string path in EnumerateFiles(root, "*.cs"))
            {
                string source = File.ReadAllText(path);
                if (IsGenerated(source))
                    continue;
                string newline = source.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
                var lines = source.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').ToList();
                bool changed = false;
                for (int i = 0; i < lines.Count; i++)
                {
                    if (!lines[i].Contains("/// <summary>", StringComparison.Ordinal))
                        continue;
                    int documentationEnd = i;
                    while (documentationEnd + 1 < lines.Count &&
                           lines[documentationEnd + 1].TrimStart().StartsWith("///", StringComparison.Ordinal))
                    {
                        documentationEnd++;
                    }
                    string declaration = ReadFollowingDeclaration(lines, documentationEnd + 1);
                    Match method = MethodDeclarationPattern().Match(declaration);
                    if (!method.Success)
                    {
                        i = documentationEnd;
                        continue;
                    }
                    List<string> documentationLines = lines.Skip(i).Take(documentationEnd - i + 1).ToList();
                    string returnType = method.Groups[1].Value.Trim();
                    string[] parameterNames = ParseParameterNames(method.Groups[3].Value).ToArray();
                    var parameterSet = parameterNames.ToHashSet(StringComparer.Ordinal);
                    var seenParameters = new HashSet<string>(StringComparer.Ordinal);
                    bool seenReturns = false;
                    for (int lineIndex = 0; lineIndex < documentationLines.Count; lineIndex++)
                    {
                        Match parameterTag = ParameterTagPattern().Match(documentationLines[lineIndex]);
                        bool removeBlock = false;
                        string closingTag = string.Empty;
                        if (parameterTag.Success)
                        {
                            string parameterName = parameterTag.Groups[1].Value;
                            removeBlock = !parameterSet.Contains(parameterName) || !seenParameters.Add(parameterName);
                            closingTag = "</param>";
                        }
                        else if (documentationLines[lineIndex].Contains("<returns>", StringComparison.Ordinal))
                        {
                            removeBlock = string.Equals(returnType, "void", StringComparison.Ordinal) || seenReturns;
                            seenReturns = true;
                            closingTag = "</returns>";
                        }
                        if (!removeBlock)
                            continue;
                        int blockEnd = lineIndex;
                        while (blockEnd < documentationLines.Count &&
                               !documentationLines[blockEnd].Contains(closingTag, StringComparison.Ordinal))
                        {
                            blockEnd++;
                        }
                        documentationLines.RemoveRange(lineIndex, Math.Min(
                            documentationLines.Count - lineIndex,
                            blockEnd - lineIndex + 1));
                        lineIndex--;
                        changed = true;
                    }
                    int originalDocumentationCount = documentationEnd - i + 1;
                    if (documentationLines.Count != originalDocumentationCount)
                    {
                        lines.RemoveRange(i, originalDocumentationCount);
                        lines.InsertRange(i, documentationLines);
                        documentationEnd = i + documentationLines.Count - 1;
                    }
                    string documentation = string.Join('\n', documentationLines);
                    string indentation = lines[i][..lines[i].IndexOf("///", StringComparison.Ordinal)];
                    var additions = new List<string>();
                    foreach (string parameter in parameterNames)
                    {
                        if (documentation.Contains($"<param name=\"{parameter}\">", StringComparison.Ordinal))
                            continue;
                        additions.Add($"{indentation}/// <param name=\"{parameter}\">");
                        additions.Add($"{indentation}/// {DescribeParameter(parameter)}");
                        additions.Add($"{indentation}/// </param>");
                    }
                    if (!string.Equals(returnType, "void", StringComparison.Ordinal) &&
                        !documentation.Contains("<returns>", StringComparison.Ordinal))
                    {
                        additions.Add($"{indentation}/// <returns>");
                        additions.Add($"{indentation}/// {DescribeReturn(method.Groups[2].Value, returnType)}");
                        additions.Add($"{indentation}/// </returns>");
                    }
                    if (additions.Count == 0)
                    {
                        i = documentationEnd;
                        continue;
                    }
                    lines.InsertRange(documentationEnd + 1, additions);
                    changed = true;
                    i = documentationEnd + additions.Count;
                }
                if (!changed)
                    continue;
                File.WriteAllText(path, string.Join(newline, lines));
                changedFileCount++;
            }
        }
        return changedFileCount;
    }

    private static string CreateExplicitDocumentation(string indentation, string declaration)
    {
        Match method = MethodDeclarationPattern().Match(declaration);
        string summary;
        string returns = string.Empty;
        List<string> parameters = [];
        if (method.Success)
        {
            string returnType = method.Groups[1].Value.Trim();
            string methodName = method.Groups[2].Value;
            summary = DescribeMethod(methodName);
            parameters.AddRange(ParseParameterNames(method.Groups[3].Value));
            if (!string.Equals(returnType, "void", StringComparison.Ordinal))
                returns = DescribeReturn(methodName, returnType);
        }
        else
        {
            Match property = PropertyDeclarationPattern().Match(declaration);
            string propertyName = property.Success ? property.Groups[2].Value : "contract value";
            string propertyType = property.Success ? property.Groups[1].Value : string.Empty;
            summary = DescribeProperty(propertyName, propertyType, declaration.Contains(" set", StringComparison.Ordinal));
        }

        var documentation = new List<string>
        {
            $"{indentation}/// <summary>",
            $"{indentation}/// {summary}",
            $"{indentation}/// </summary>"
        };
        foreach (string parameter in parameters)
        {
            documentation.Add($"{indentation}/// <param name=\"{parameter}\">");
            documentation.Add($"{indentation}/// {DescribeParameter(parameter)}");
            documentation.Add($"{indentation}/// </param>");
        }
        if (!string.IsNullOrEmpty(returns))
        {
            documentation.Add($"{indentation}/// <returns>");
            documentation.Add($"{indentation}/// {returns}");
            documentation.Add($"{indentation}/// </returns>");
        }
        return string.Join('\n', documentation);
    }

    private static IEnumerable<string> ParseParameterNames(string parameters)
    {
        var current = new StringBuilder();
        int nesting = 0;
        foreach (char character in parameters)
        {
            if (character is '<' or '(' or '[')
                nesting++;
            else if (character is '>' or ')' or ']')
                nesting--;
            if (character == ',' && nesting == 0)
            {
                string? name = ParseParameterName(current.ToString());
                if (name is not null)
                    yield return name;
                current.Clear();
                continue;
            }
            current.Append(character);
        }
        string? finalName = ParseParameterName(current.ToString());
        if (finalName is not null)
            yield return finalName;
    }

    private static string? ParseParameterName(string parameter)
    {
        string withoutDefault = parameter.Split('=')[0].Trim();
        Match match = ParameterNamePattern().Match(withoutDefault);
        return match.Success ? match.Groups[1].Value : null;
    }

    private static string DescribeMethod(string name)
    {
        return name switch
        {
            "Equals" => "Determines whether this instance and the supplied value represent the same logical state.",
            "GetHashCode" => "Computes a hash code from the fields that participate in logical equality.",
            "ToString" => "Formats this value as a human-readable representation.",
            "CompareTo" => "Compares this value with another value using the contract's stable ordering.",
            "Dispose" => "Releases the resources owned by this implementation.",
            "OnStart" => "Initializes this feature when its owning runtime becomes active.",
            "OnStop" => "Stops this feature before its owning runtime releases the active generation.",
            "OnUpdate" => "Advances this feature using the current runtime state.",
            "OnDraw" => "Draws this feature using the current editor presentation context.",
            "Write" => "Writes the complete value through the configured serialization contract.",
            "Read" => "Reconstructs a complete value through the configured serialization contract.",
            "Query" => "Evaluates the operation's current availability and presentation state.",
            "Execute" => "Performs the requested operation against the supplied context.",
            "Receive" => "Consumes the supplied message through this implementation.",
            "Replace" => "Replaces the current state represented by the supplied value.",
            "Clear" => "Removes the state identified by the supplied value.",
            "Prepare" or "PrepareForActivation" => "Prepares candidate state without changing the active generation.",
            "Apply" => "Applies the prepared state at the caller-controlled commit point.",
            "Complete" => "Completes the committed operation and releases temporary state.",
            "Rollback" or "RollbackStructure" or "RestorePreviousState" =>
                "Restores the state that existed before candidate activation began.",
            _ when name.StartsWith("Try", StringComparison.Ordinal) =>
                $"Attempts to {Humanize(name[3..])} without changing state when the operation cannot complete.",
            _ when name.StartsWith("Create", StringComparison.Ordinal) =>
                $"Creates {WithArticle(Humanize(name[6..]))} using this implementation's validated inputs.",
            _ when name.StartsWith("Get", StringComparison.Ordinal) =>
                $"Gets {WithArticle(Humanize(name[3..]))} required by the implemented contract.",
            _ => $"Performs the {Humanize(name)} operation required by the implemented contract."
        };
    }

    private static string DescribeProperty(string name, string type, bool hasSetter)
    {
        string access = hasSetter ? "Gets or sets" : "Gets";
        if (name.StartsWith("is", StringComparison.Ordinal) && name.Length > 2)
            return $"{access} whether this implementation is {Humanize(name[2..])}.";
        if (name.StartsWith("has", StringComparison.Ordinal) && name.Length > 3)
            return $"{access} whether this implementation has {Humanize(name[3..])}.";
        if (name.StartsWith("can", StringComparison.Ordinal) && name.Length > 3)
            return $"{access} whether this implementation can {Humanize(name[3..])}.";
        if (string.Equals(type, "bool", StringComparison.Ordinal))
            return $"{access} whether {Humanize(name)} is enabled for this implementation.";
        return $"{access} the {Humanize(name)} exposed by this implementation.";
    }

    private static string DescribeParameter(string name)
    {
        return name switch
        {
            "context" => "The context that supplies state and services for this operation.",
            "other" => "The value to compare with this instance.",
            "obj" => "The object to compare with this instance.",
            "writer" => "The writer that receives the serialized representation.",
            "reader" => "The reader that supplies the serialized representation.",
            "value" => "The value processed by this operation.",
            "cancellationToken" => "The token that cancels the operation before it commits.",
            "deltaTime" => "The elapsed frame time in seconds.",
            "fixedDeltaTime" => "The fixed simulation step in seconds.",
            _ => $"The {Humanize(name)} supplied to this operation."
        };
    }

    private static string DescribeReturn(string name, string returnType)
    {
        if (name == "Equals")
            return "<see langword=\"true\"/> when both values represent the same logical state; otherwise, <see langword=\"false\"/>.";
        if (name == "GetHashCode")
            return "A hash code consistent with the implemented equality contract.";
        if (name == "ToString")
            return "The human-readable representation of this value.";
        if (returnType == "bool")
            return "<see langword=\"true\"/> when the operation succeeds or its condition is satisfied; otherwise, <see langword=\"false\"/>.";
        return "The value produced by this implementation of the contract.";
    }

    private static string Humanize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "value";
        string spaced = HumanizePattern().Replace(value.Replace('_', ' '), " $1");
        return spaced.Trim().ToLowerInvariant();
    }

    private static string WithArticle(string value)
    {
        char first = value.Length == 0 ? 'v' : value[0];
        string article = "aeiou".Contains(first) ? "an" : "a";
        return $"{article} {value}";
    }

    private static void ValidateProjects(string repositoryRoot, string root, ICollection<string> failures)
    {
        foreach (string path in EnumerateFiles(root, "*.csproj"))
        {
            XDocument project = XDocument.Load(path, LoadOptions.SetLineInfo);
            string relative = Relative(repositoryRoot, path);
            foreach (XElement implicitUsings in project.Descendants("ImplicitUsings"))
            {
                if (string.Equals(implicitUsings.Value.Trim(), "enable", StringComparison.OrdinalIgnoreCase))
                    failures.Add($"{relative}: implicit usings must be disabled.");
            }
            foreach (XElement usingItem in project.Descendants("Using"))
                failures.Add($"{relative}: MSBuild Using items are forbidden ({usingItem.Attribute("Include")?.Value}).");
        }
    }

    private static void ValidateProjectReferences(string repositoryRoot, ICollection<string> failures)
    {
        foreach (string projectPath in EnumerateFiles(repositoryRoot, "*.csproj"))
        {
            if (IsIgnoredPath(projectPath))
                continue;
            string projectRelative = Relative(repositoryRoot, projectPath);
            XDocument project = XDocument.Load(projectPath);
            foreach (XElement reference in project.Descendants("ProjectReference"))
            {
                string? include = reference.Attribute("Include")?.Value;
                if (string.IsNullOrWhiteSpace(include))
                    continue;
                string target = Path.GetFullPath(include, Path.GetDirectoryName(projectPath)!);
                string targetRelative = Relative(repositoryRoot, target);
                if (projectRelative.StartsWith("src/core/", StringComparison.Ordinal) &&
                    IsAnyDomain(targetRelative, "assets", "editor", "engine", "platform", "render", "runtime", "scripting", "plugins"))
                {
                    failures.Add($"{projectRelative}: Core cannot reference business project {targetRelative}.");
                }
                if (projectRelative.StartsWith("build/", StringComparison.Ordinal) &&
                    targetRelative.StartsWith("src/editor/", StringComparison.Ordinal))
                {
                    failures.Add($"{projectRelative}: Build cannot reference Editor project {targetRelative}.");
                }
                if ((projectRelative.StartsWith("src/runtime/", StringComparison.Ordinal) ||
                     projectRelative.StartsWith("src/engine/", StringComparison.Ordinal)) &&
                    (targetRelative.StartsWith("src/editor/", StringComparison.Ordinal) ||
                     targetRelative.StartsWith("build/", StringComparison.Ordinal)))
                {
                    failures.Add($"{projectRelative}: Runtime cannot reference {targetRelative}.");
                }
                if (targetRelative.Contains("Inno.Native.Bgfx", StringComparison.Ordinal) &&
                    !projectRelative.Contains("Inno.Rendering.Bgfx", StringComparison.Ordinal) &&
                    !projectRelative.Contains("Inno.Native.Bgfx", StringComparison.Ordinal) &&
                    !projectRelative.StartsWith("build/toolchains/Inno.Build.Toolchains.Bgfx", StringComparison.Ordinal) &&
                    !projectRelative.StartsWith("tests/", StringComparison.Ordinal))
                {
                    failures.Add($"{projectRelative}: BGFX native code is restricted to the BGFX adapter.");
                }
                if (targetRelative.Contains("Inno.Native.SDL3", StringComparison.Ordinal) &&
                    !projectRelative.Contains("Inno.Platform.Sdl3", StringComparison.Ordinal) &&
                    !projectRelative.Contains("Inno.Native.SDL3", StringComparison.Ordinal) &&
                    !projectRelative.StartsWith("tests/", StringComparison.Ordinal))
                {
                    failures.Add($"{projectRelative}: SDL3 native code is restricted to the SDL3 platform adapter.");
                }
            }
        }
    }

    private static bool IsAnyDomain(string path, params string[] domains)
        => domains.Any(domain => path.StartsWith($"src/{domain}/", StringComparison.Ordinal));

    private static void AddSourceFailure(
        bool condition,
        string path,
        string message,
        ICollection<string> failures)
    {
        if (condition)
            failures.Add($"{path}: {message}.");
    }

    private static IEnumerable<string> EnumerateDirectories(string root)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            string current = pending.Pop();
            foreach (string child in Directory.EnumerateDirectories(current))
            {
                if (S_IGNORED_DIRECTORIES.Contains(Path.GetFileName(child)))
                    continue;
                yield return child;
                pending.Push(child);
            }
        }
    }

    private static IEnumerable<string> EnumerateFiles(string root, string pattern)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            string current = pending.Pop();
            foreach (string file in Directory.EnumerateFiles(current, pattern))
                yield return file;
            foreach (string child in Directory.EnumerateDirectories(current))
            {
                if (!S_IGNORED_DIRECTORIES.Contains(Path.GetFileName(child)))
                    pending.Push(child);
            }
        }
    }

    private static bool IsIgnoredPath(string path)
        => path.Split(Path.DirectorySeparatorChar).Any(S_IGNORED_DIRECTORIES.Contains);

    private static bool IsGenerated(string source)
        => source.Contains("<auto-generated>", StringComparison.OrdinalIgnoreCase) ||
           source.Contains("[GeneratedCode", StringComparison.Ordinal);

    private static string Relative(string repositoryRoot, string path)
        => Path.GetRelativePath(repositoryRoot, path).Replace('\\', '/');

    [GeneratedRegex(@"\b(schemaVersion|formatVersion|formerVersion)\b", RegexOptions.IgnoreCase)]
    private static partial Regex CompatibilityFieldPattern();

    [GeneratedRegex(@"^\s*global\s+using\b", RegexOptions.Multiline)]
    private static partial Regex GlobalUsingPattern();

    [GeneratedRegex(@"Executes this contract at the caller-controlled boundary|Transforms validated inputs into a deterministic result|Gets caller-visible|input consumed by this operation|state exposed by this contract|value associated with this contract|Models the .* domain value|Performs the .* operation|Runs the .* operation|Gets the .* value owned by the current instance")]
    private static partial Regex UninformativeXmlPattern();

    [GeneratedRegex(@"^\s*///\s*<(summary|param|typeparam|returns|exception|remarks)\b[^>]*>.+</\1>\s*$")]
    private static partial Regex SingleLineXmlPattern();

    [GeneratedRegex(@"^\s*///\s*<inheritdoc\b[^>]*/>\s*$")]
    private static partial Regex SelfClosingInheritDocPattern();

    [GeneratedRegex(@"(?m)^([ \t]*)///[ \t]*<(summary|param|typeparam|returns|exception|remarks)([^>]*)>(.+)</\2>[ \t]*\r?$")]
    private static partial Regex ExpandableXmlPattern();

    [GeneratedRegex(@"^(?:(?:public|protected|internal|private|static|virtual|override|abstract|sealed|readonly|async|unsafe|new|partial)\s+)*(?:[\w.<>?,\[\]]+\s+)+([\w.<>?,\[\]]+)\s+(?:[\w.]+\.)?(\w+)(?:<[^>]+>)?\s*\((.*)\)")]
    private static partial Regex MethodDeclarationPattern();

    [GeneratedRegex(@"^(?:(?:public|protected|internal|private|static|virtual|override|abstract|sealed|readonly|unsafe|new|partial)\s+)*([\w.<>?,\[\]]+)\s+(\w+)\s*(?:=>|\{|;)")]
    private static partial Regex PropertyDeclarationPattern();

    [GeneratedRegex(@"([A-Za-z_]\w*)$")]
    private static partial Regex ParameterNamePattern();

    [GeneratedRegex(@"<param name=""([^""]+)"">")]
    private static partial Regex ParameterTagPattern();

    [GeneratedRegex(@"(?<=[a-z0-9])([A-Z])")]
    private static partial Regex HumanizePattern();
}
