using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Inno.Tooling.Architecture;

internal static class PublicApiBoundaryValidator
{
    internal static void Validate(string root, ICollection<string> failures)
    {
        Project[] projects = new[] { "src", "native", "build" }
            .SelectMany(folder => Directory.EnumerateFiles(Path.Combine(root, folder), "*.csproj", SearchOption.AllDirectories))
            .Where(path => !path.Split(Path.DirectorySeparatorChar).Any(segment => segment is "bin" or "obj"))
            .Select(Project.Read).ToArray();
        var byName = projects.ToDictionary(project => project.name, StringComparer.Ordinal);
        var references = new Dictionary<string, PortableExecutableReference>(StringComparer.OrdinalIgnoreCase);
        string[] platform = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? string.Empty).Split(Path.PathSeparator);
        foreach (string path in platform.Where(File.Exists))
            references[Path.GetFileNameWithoutExtension(path)] = MetadataReference.CreateFromFile(path);
        foreach (Project project in projects)
        {
            if (File.Exists(project.output))
                references[project.name] = MetadataReference.CreateFromFile(project.output);
            else
                failures.Add($"{Path.GetRelativePath(root, project.path)}: public API symbol audit requires a Debug solution build; output is missing.");
        }
        CSharpCompilation compilation = CSharpCompilation.Create("Inno.Architecture.SymbolAudit", references: references.Values,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        foreach (Project project in projects.Where(project => project.path.Contains(Path.DirectorySeparatorChar + "src" + Path.DirectorySeparatorChar, StringComparison.Ordinal)))
        {
            if (!references.TryGetValue(project.name, out PortableExecutableReference? reference) ||
                compilation.GetAssemblyOrModuleSymbol(reference) is not IAssemblySymbol assembly)
                continue;
            var dependencies = new HashSet<string>(StringComparer.Ordinal);
            foreach (INamedTypeSymbol type in Types(assembly.GlobalNamespace))
            {
                if (!Exposed(type))
                    continue;
                Inspect(type.BaseType, type);
                foreach (INamedTypeSymbol contract in type.Interfaces)
                    Inspect(contract, type);
                foreach (ITypeParameterSymbol parameter in type.TypeParameters)
                    foreach (ITypeSymbol constraint in parameter.ConstraintTypes)
                        Inspect(constraint, type);
                foreach (ISymbol member in type.GetMembers().Where(Exposed))
                {
                    switch (member)
                    {
                        case IMethodSymbol method:
                            Inspect(method.ReturnType, member);
                            foreach (IParameterSymbol parameter in method.Parameters) Inspect(parameter.Type, member);
                            foreach (ITypeParameterSymbol parameter in method.TypeParameters)
                                foreach (ITypeSymbol constraint in parameter.ConstraintTypes) Inspect(constraint, member);
                            break;
                        case IPropertySymbol property:
                            Inspect(property.Type, member);
                            foreach (IParameterSymbol parameter in property.Parameters) Inspect(parameter.Type, member);
                            break;
                        case IFieldSymbol field: Inspect(field.Type, member); break;
                        case IEventSymbol signal: Inspect(signal.Type, member); break;
                    }
                }
            }
            if (project.name.StartsWith("Inno.Editor.", StringComparison.Ordinal))
            {
                foreach (string dependency in dependencies.Where(byName.ContainsKey).Order(StringComparer.Ordinal))
                {
                    XElement? declaration = project.references.FirstOrDefault(element =>
                        Path.GetFileNameWithoutExtension(element.Attribute("Include")?.Value.Replace('\\', '/') ?? string.Empty) == dependency);
                    if (declaration is null || ((string?)declaration.Attribute("PrivateAssets"))?.Split(';').Contains("compile") == true)
                        failures.Add($"{Path.GetRelativePath(root, project.path)}: public/protected API exposes {dependency}; declare a direct public ProjectReference.");
                }
            }

            void Inspect(ITypeSymbol? value, ISymbol member)
            {
                if (value is null) return;
                switch (value)
                {
                    case IArrayTypeSymbol array: Inspect(array.ElementType, member); return;
                    case IPointerTypeSymbol pointer: Inspect(pointer.PointedAtType, member); return;
                    case IFunctionPointerTypeSymbol pointer:
                        Inspect(pointer.Signature.ReturnType, member);
                        foreach (IParameterSymbol parameter in pointer.Signature.Parameters) Inspect(parameter.Type, member);
                        return;
                    case INamedTypeSymbol named:
                        Inspect(named.ContainingType, member);
                        foreach (ITypeSymbol argument in named.TypeArguments) Inspect(argument, member);
                        break;
                }
                string? dependency = value.ContainingAssembly?.Identity.Name;
                if (dependency is null || dependency == project.name) return;
                dependencies.Add(dependency);
                if (dependency.StartsWith("Inno.Native.Bgfx", StringComparison.Ordinal) ||
                    dependency == "Inno.Native.MiniAudio" || dependency == "Inno.Native.Sdl3")
                    failures.Add($"{Path.GetRelativePath(root, project.path)}: {member.ToDisplayString()} exposes native symbol {value.ToDisplayString()} from {dependency}.");
                if ((project.name == "Inno.Shell" || project.name.StartsWith("Inno.Editor.", StringComparison.Ordinal) || project.name == "Inno.Player") &&
                    dependency.StartsWith("Inno.Adapter.", StringComparison.Ordinal) &&
                    dependency.Split('.').Length >= 4 && !dependency.EndsWith("Default", StringComparison.Ordinal))
                    failures.Add($"{Path.GetRelativePath(root, project.path)}: {member.ToDisplayString()} exposes concrete adapter {value.ToDisplayString()}.");
            }
        }
    }

    private static bool Exposed(ISymbol symbol)
        => symbol.DeclaredAccessibility is Accessibility.Public or Accessibility.Protected or Accessibility.ProtectedOrInternal;

    private static IEnumerable<INamedTypeSymbol> Types(INamespaceOrTypeSymbol owner)
    {
        foreach (ISymbol member in owner.GetMembers())
        {
            if (member is INamespaceSymbol space)
                foreach (INamedTypeSymbol type in Types(space)) yield return type;
            else if (member is INamedTypeSymbol type && Exposed(type))
            {
                yield return type;
                foreach (INamedTypeSymbol nested in Types(type)) yield return nested;
            }
        }
    }

    private sealed record Project(string path, string name, string output, XElement[] references)
    {
        internal static Project Read(string path)
        {
            XDocument document = XDocument.Load(path);
            string name = document.Descendants("AssemblyName").FirstOrDefault()?.Value ?? Path.GetFileNameWithoutExtension(path);
            string framework = document.Descendants("TargetFramework").FirstOrDefault()?.Value ?? "net9.0";
            return new Project(path, name, Path.Combine(Path.GetDirectoryName(path)!, "bin", "Debug", framework, name + ".dll"),
                document.Descendants("ProjectReference").ToArray());
        }
    }
}
