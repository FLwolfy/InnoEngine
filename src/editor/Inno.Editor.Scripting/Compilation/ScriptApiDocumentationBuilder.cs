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
        IReadOnlyList<Type> types,
        IReadOnlyDictionary<string, string> namespaceMappings)
    {
        var outputMembers = new List<XElement>();
        var emittedNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (IGrouping<Assembly, Type> group in types.GroupBy(static type => type.Assembly))
        {
            string documentationPath = Path.ChangeExtension(group.Key.Location, ".xml");
            if (!File.Exists(documentationPath))
                continue;
            XDocument source = XDocument.Load(documentationPath, LoadOptions.PreserveWhitespace);
            XElement[] members = source.Root?
                .Element("members")?
                .Elements("member")
                .ToArray() ?? [];
            foreach (Type type in group)
            {
                string typeName = GetDocumentationTypeName(type);
                foreach (XElement member in members.Where(member => BelongsToType(member, typeName)))
                {
                    var copy = new XElement(member);
                    RewriteDocumentationIdentities(copy, namespaceMappings);
                    string? name = copy.Attribute("name")?.Value;
                    if (name is not null && emittedNames.Add(name))
                        outputMembers.Add(copy);
                }
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

    private static void RewriteDocumentationIdentities(
        XElement member,
        IReadOnlyDictionary<string, string> namespaceMappings)
    {
        foreach (XAttribute attribute in member.DescendantsAndSelf().Attributes())
        {
            if (attribute.Name.LocalName is not ("name" or "cref"))
                continue;
            attribute.Value = RewriteNamespace(attribute.Value, namespaceMappings);
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
}
