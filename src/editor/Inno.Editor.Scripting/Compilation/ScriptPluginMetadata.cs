using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace Inno.Editor.Scripting;

internal static class ScriptPluginMetadata
{
    private const string C_GLOBAL_USINGS_KEY = "Inno.ScriptGlobalUsings";

    internal static ScriptApiProfile AddGlobalUsings(
        ScriptApiProfile profile,
        IEnumerable<string> pluginPaths)
    {
        var globalUsings = new HashSet<string>(profile.globalUsings, StringComparer.Ordinal);
        foreach (string pluginPath in pluginPaths)
        {
            foreach ((string key, string value) in ReadAssemblyMetadata(pluginPath))
            {
                if (!string.Equals(key, C_GLOBAL_USINGS_KEY, StringComparison.Ordinal))
                    continue;
                foreach (string declaredUsing in value.Split(
                             ';',
                             StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    globalUsings.Add(declaredUsing);
                }
            }
        }

        return new ScriptApiProfile(
            profile.name,
            profile.exports,
            profile.implementationAssemblies,
            globalUsings.OrderBy(static value => value, StringComparer.Ordinal).ToArray(),
            profile.apiNamespaces,
            profile.namespaceMappings);
    }

    private static IEnumerable<(string key, string value)> ReadAssemblyMetadata(string path)
    {
        using FileStream stream = File.OpenRead(path);
        using var peReader = new PEReader(stream);
        if (!peReader.HasMetadata)
            yield break;

        MetadataReader reader = peReader.GetMetadataReader();
        AssemblyDefinition assembly = reader.GetAssemblyDefinition();
        foreach (CustomAttributeHandle attributeHandle in assembly.GetCustomAttributes())
        {
            CustomAttribute attribute = reader.GetCustomAttribute(attributeHandle);
            if (!IsAssemblyMetadataAttribute(reader, attribute.Constructor))
                continue;

            BlobReader valueReader = reader.GetBlobReader(attribute.Value);
            if (valueReader.ReadUInt16() != 1)
                continue;
            string? key = valueReader.ReadSerializedString();
            string? value = valueReader.ReadSerializedString();
            if (key is not null && value is not null)
                yield return (key, value);
        }
    }

    private static bool IsAssemblyMetadataAttribute(MetadataReader reader, EntityHandle constructor)
    {
        EntityHandle declaringType = constructor.Kind switch
        {
            HandleKind.MemberReference => reader.GetMemberReference(
                (MemberReferenceHandle)constructor).Parent,
            HandleKind.MethodDefinition => reader.GetMethodDefinition(
                (MethodDefinitionHandle)constructor).GetDeclaringType(),
            _ => default
        };
        return declaringType.Kind switch
        {
            HandleKind.TypeReference => IsAssemblyMetadataAttribute(
                reader.GetTypeReference((TypeReferenceHandle)declaringType),
                reader),
            HandleKind.TypeDefinition => IsAssemblyMetadataAttribute(
                reader.GetTypeDefinition((TypeDefinitionHandle)declaringType),
                reader),
            _ => false
        };
    }

    private static bool IsAssemblyMetadataAttribute(TypeReference type, MetadataReader reader)
        => reader.StringComparer.Equals(type.Namespace, "System.Reflection") &&
           reader.StringComparer.Equals(type.Name, "AssemblyMetadataAttribute");

    private static bool IsAssemblyMetadataAttribute(TypeDefinition type, MetadataReader reader)
        => reader.StringComparer.Equals(type.Namespace, "System.Reflection") &&
           reader.StringComparer.Equals(type.Name, "AssemblyMetadataAttribute");
}
