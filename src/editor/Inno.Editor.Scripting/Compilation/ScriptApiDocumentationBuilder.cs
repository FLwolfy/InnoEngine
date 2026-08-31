using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;

namespace Inno.Editor.Scripting;

internal static class ScriptApiDocumentationBuilder
{
    internal static void Write(
        string outputPath,
        string assemblyName,
        IReadOnlyList<ScriptApiTypeExport> exports,
        IReadOnlyDictionary<string, string> namespaceMappings,
        IReadOnlyList<ScriptApiTypeMapping> typeMappings)
    {
        var outputMembers = new List<XElement>();
        var emittedNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (IGrouping<Assembly, ScriptApiTypeExport> group in exports.GroupBy(
                     static export => export.type.Assembly))
        {
            string documentationPath = Path.ChangeExtension(group.Key.Location, ".xml");
            if (!File.Exists(documentationPath))
                continue;
            XDocument source = XDocument.Load(documentationPath, LoadOptions.PreserveWhitespace);
            XElement[] members = source.Root?
                .Element("members")?
                .Elements("member")
                .ToArray() ?? [];
            foreach (ScriptApiTypeExport export in group)
            {
                Type type = export.type;
                string typeName = GetDocumentationTypeName(type);
                foreach (XElement member in members.Where(member => BelongsToType(member, typeName)))
                {
                    var copy = new XElement(member);
                    RewriteDocumentationIdentities(copy, namespaceMappings, typeMappings);
                    string? name = copy.Attribute("name")?.Value;
                    if (name is not null && emittedNames.Add(name))
                        outputMembers.Add(copy);
                }
            }
        }
        foreach (ScriptApiTypeExport export in exports.Where(export => !string.Equals(
                     export.name,
                     GetRuntimeTypeName(export.type),
                     StringComparison.Ordinal)))
        {
            ScriptApiTypeMapping mapping = typeMappings.Single(value =>
                string.Equals(
                    value.implementationNamespace,
                    export.type.Namespace ?? string.Empty,
                    StringComparison.Ordinal) &&
                string.Equals(value.implementationName, GetRuntimeTypeName(export.type), StringComparison.Ordinal) &&
                string.Equals(value.apiName, export.name, StringComparison.Ordinal));
            string facadeTypeName = mapping.apiNamespace + "." + mapping.apiName;
            AddFallbackDocumentation(
                outputMembers,
                emittedNames,
                "T:" + facadeTypeName,
                $"Provides the {mapping.apiName} values exposed by the script API.");
            foreach (FieldInfo field in export.type.GetFields(
                         BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                AddFallbackDocumentation(
                    outputMembers,
                    emittedNames,
                    "F:" + facadeTypeName + "." + field.Name,
                    $"Provides the {field.Name} value from {mapping.apiName}.");
            }
        }

        var document = new XDocument(
            new XDeclaration("1.0", "utf-8", null),
            new XElement("doc",
                new XElement("assembly", new XElement("name", assemblyName)),
                new XElement("members", outputMembers.OrderBy(
                    static member => member.Attribute("name")?.Value,
                    StringComparer.Ordinal))));
        string temporaryPath = outputPath + ".tmp";
        document.Save(temporaryPath);
        File.Move(temporaryPath, outputPath, overwrite: true);
    }

    private static bool BelongsToType(XElement member, string typeName)
    {
        string? name = member.Attribute("name")?.Value;
        if (string.IsNullOrEmpty(name) || name.Length < 3 || name[1] != ':')
            return false;
        string declarationName = name[2..];
        return name[0] == 'T'
            ? string.Equals(declarationName, typeName, StringComparison.Ordinal)
            : declarationName.StartsWith(typeName + ".", StringComparison.Ordinal);
    }

    private static string GetDocumentationTypeName(Type type)
        => (type.FullName ?? type.Name).Replace('+', '.');

    private static string GetRuntimeTypeName(Type type)
    {
        int aritySeparator = type.Name.IndexOf('`');
        return aritySeparator < 0 ? type.Name : type.Name[..aritySeparator];
    }

    private static void AddFallbackDocumentation(
        ICollection<XElement> outputMembers,
        ISet<string> emittedNames,
        string memberName,
        string summary)
    {
        if (!emittedNames.Add(memberName))
            return;
        outputMembers.Add(new XElement(
            "member",
            new XAttribute("name", memberName),
            new XElement("summary", summary)));
    }

    private static void RewriteDocumentationIdentities(
        XElement member,
        IReadOnlyDictionary<string, string> namespaceMappings,
        IReadOnlyList<ScriptApiTypeMapping> typeMappings)
    {
        foreach (XAttribute attribute in member.DescendantsAndSelf().Attributes())
        {
            if (attribute.Name.LocalName is not ("name" or "cref"))
                continue;
            attribute.Value = RewriteTypeNames(
                RewriteNamespace(attribute.Value, namespaceMappings),
                typeMappings);
        }
    }

    private static string RewriteNamespace(
        string value,
        IReadOnlyDictionary<string, string> namespaceMappings)
    {
        foreach ((string implementationNamespace, string apiNamespace) in namespaceMappings
                     .OrderByDescending(static pair => pair.Key.Length))
        {
            value = value.Replace(
                implementationNamespace,
                apiNamespace,
                StringComparison.Ordinal);
        }
        return value;
    }

    private static string RewriteTypeNames(
        string value,
        IReadOnlyList<ScriptApiTypeMapping> typeMappings)
    {
        foreach (ScriptApiTypeMapping mapping in typeMappings
                     .OrderByDescending(static mapping =>
                         mapping.apiNamespace.Length + mapping.implementationName.Length))
        {
            value = value.Replace(
                mapping.apiNamespace + "." + mapping.implementationName,
                mapping.apiNamespace + "." + mapping.apiName,
                StringComparison.Ordinal);
        }
        return value;
    }
}
