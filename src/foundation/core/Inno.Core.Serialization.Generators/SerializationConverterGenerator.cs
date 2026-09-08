using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Inno.Core.Serialization.Generators;

/// <summary>
/// Generates stateless serialization converters for explicitly annotated closed data-transfer types.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class SerializationConverterGenerator : IIncrementalGenerator
{
    private const string C_GENERATE_ATTRIBUTE =
        "Inno.Core.Serialization.GenerateSerializationConverterAttribute";
    private const string C_PROPERTY_ATTRIBUTE =
        "Inno.Core.Serialization.SerializablePropertyAttribute";
    private const string C_SERIALIZABLE_INTERFACE =
        "Inno.Core.Serialization.ISerializable";

    private static readonly DiagnosticDescriptor s_invalidType = new(
        "INNOSER001",
        "Invalid generated serialization type",
        "Type '{0}' must be a non-abstract, non-generic class implementing ISerializable",
        "Inno.Serialization",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor s_missingConstructor = new(
        "INNOSER002",
        "Generated serialization type requires a constructor",
        "Type '{0}' must expose a parameterless constructor accessible to generated code",
        "Inno.Serialization",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor s_invalidMember = new(
        "INNOSER003",
        "Invalid generated serialization member",
        "Member '{0}' cannot participate in generated {1}",
        "Inno.Serialization",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor s_duplicateKey = new(
        "INNOSER004",
        "Duplicate generated serialization key",
        "Type '{0}' declares serialization key '{1}' more than once across its inheritance chain",
        "Inno.Serialization",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>
    /// Registers the incremental syntax and source-production pipeline.
    /// </summary>
    /// <param name="context">
    /// The Roslyn initialization context that receives generator registrations.
    /// </param>
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        IncrementalValuesProvider<INamedTypeSymbol> types = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                C_GENERATE_ATTRIBUTE,
                static (node, _) => node is ClassDeclarationSyntax,
                static (attributeContext, _) => (INamedTypeSymbol)attributeContext.TargetSymbol);

        context.RegisterSourceOutput(types, static (productionContext, type) =>
            Generate(productionContext, type));
    }

    private static void Generate(SourceProductionContext context, INamedTypeSymbol type)
    {
        Location location = type.Locations.FirstOrDefault() ?? Location.None;
        if (type.TypeKind != TypeKind.Class
            || type.IsAbstract
            || type.IsGenericType
            || !ImplementsSerializable(type)
            || !IsTypeAccessible(type))
        {
            context.ReportDiagnostic(Diagnostic.Create(s_invalidType, location, type.ToDisplayString()));
            return;
        }

        if (!HasAccessibleParameterlessConstructor(type))
        {
            context.ReportDiagnostic(Diagnostic.Create(s_missingConstructor, location, type.ToDisplayString()));
            return;
        }

        ImmutableArray<SerializableMember> members = CollectMembers(type);
        bool failed = false;
        foreach (IGrouping<string, SerializableMember> duplicate in members
                     .GroupBy(static member => member.name, StringComparer.Ordinal)
                     .Where(static group => group.Count() > 1))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                s_duplicateKey,
                duplicate.First().location,
                type.ToDisplayString(),
                duplicate.Key));
            failed = true;
        }

        foreach (SerializableMember member in members)
        {
            if (member.serialize && !member.canRead)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    s_invalidMember,
                    member.location,
                    member.displayName,
                    "serialization because it is not readable by generated code"));
                failed = true;
            }
            if (member.deserialize && !member.canWrite)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    s_invalidMember,
                    member.location,
                    member.displayName,
                    "deserialization because it is not writable by generated code"));
                failed = true;
            }
        }

        if (failed)
            return;

        string source = BuildSource(type, members);
        context.AddSource(GetGeneratedName(type) + ".g.cs", SourceText.From(source, Encoding.UTF8));
    }

    private static ImmutableArray<SerializableMember> CollectMembers(INamedTypeSymbol type)
    {
        var hierarchy = new Stack<INamedTypeSymbol>();
        for (INamedTypeSymbol? current = type; current is not null; current = current.BaseType)
            hierarchy.Push(current);

        var members = ImmutableArray.CreateBuilder<SerializableMember>();
        while (hierarchy.Count > 0)
        {
            INamedTypeSymbol current = hierarchy.Pop();
            foreach (ISymbol symbol in current.GetMembers().OrderBy(GetSourceOrder))
            {
                AttributeData? attribute = symbol.GetAttributes().FirstOrDefault(static candidate =>
                    candidate.AttributeClass?.ToDisplayString() == C_PROPERTY_ATTRIBUTE);
                if (attribute is null)
                    continue;

                int visibility = attribute.ConstructorArguments.Length == 0
                    ? 15
                    : (int)(attribute.ConstructorArguments[0].Value ?? 15);
                bool serialize = (visibility & 1) != 0;
                bool deserialize = (visibility & 2) != 0;
                switch (symbol)
                {
                    case IPropertySymbol property:
                        members.Add(new SerializableMember(
                            property.Name,
                            property.ToDisplayString(),
                            property.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                            serialize,
                            deserialize,
                            IsAccessible(property.GetMethod),
                            IsAccessible(property.SetMethod) && !property.IsReadOnly,
                            property.Locations.FirstOrDefault() ?? Location.None));
                        break;
                    case IFieldSymbol field:
                        members.Add(new SerializableMember(
                            field.Name,
                            field.ToDisplayString(),
                            field.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                            serialize,
                            deserialize,
                            IsAccessible(field),
                            IsAccessible(field) && !field.IsReadOnly && !field.IsConst,
                            field.Locations.FirstOrDefault() ?? Location.None));
                        break;
                }
            }
        }
        return members.ToImmutable();
    }

    private static string BuildSource(INamedTypeSymbol type, ImmutableArray<SerializableMember> members)
    {
        string targetType = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        string converterName = "__InnoGeneratedSerializationConverter_" + GetGeneratedName(type);
        var source = new StringBuilder();
        source.AppendLine("// <auto-generated/>");
        source.AppendLine("#nullable enable");
        source.AppendLine();
        source.AppendLine("namespace " + GetNamespace(type));
        source.AppendLine("{");
        source.AppendLine("    [global::System.CodeDom.Compiler.GeneratedCode(\"Inno.Core.Serialization.Generators\", \"1\")]");
        source.AppendLine("    [global::Inno.Core.Serialization.Converters.SerializationExtension]");
        source.Append("    internal sealed class ").Append(converterName)
            .Append(" : global::Inno.Core.Serialization.Converters.SerializationConverter<")
            .Append(targetType).AppendLine(">");
        source.AppendLine("    {");
        source.Append("        public override void Write(global::Inno.Core.Serialization.SerializationWriter writer, ")
            .Append(targetType).AppendLine(" value)");
        source.AppendLine("        {");
        source.AppendLine("            global::System.ArgumentNullException.ThrowIfNull(writer);");
        source.AppendLine("            global::System.ArgumentNullException.ThrowIfNull(value);");
        foreach (SerializableMember member in members.Where(static member => member.serialize))
        {
            source.Append("            writer.Write<").Append(member.typeName).Append(">(\"")
                .Append(Escape(member.name)).Append("\", value.").Append(member.name).AppendLine(");");
        }
        source.AppendLine("        }");
        source.AppendLine();
        source.Append("        public override ").Append(targetType)
            .AppendLine(" Read(global::Inno.Core.Serialization.SerializationReader reader)");
        source.AppendLine("        {");
        source.AppendLine("            global::System.ArgumentNullException.ThrowIfNull(reader);");
        source.Append("            var value = new ").Append(targetType).AppendLine("();");
        AppendAssignments(source, members);
        source.AppendLine("            return value;");
        source.AppendLine("        }");
        source.AppendLine();
        source.Append("        public override void Restore(global::Inno.Core.Serialization.SerializationReader reader, ")
            .Append(targetType).AppendLine(" target)");
        source.AppendLine("        {");
        source.AppendLine("            global::System.ArgumentNullException.ThrowIfNull(reader);");
        source.AppendLine("            global::System.ArgumentNullException.ThrowIfNull(target);");
        foreach (SerializableMember member in members.Where(static member => member.deserialize))
        {
            source.Append("            target.").Append(member.name).Append(" = reader.Read<")
                .Append(member.typeName).Append(">(\"").Append(Escape(member.name)).AppendLine("\");");
        }
        source.AppendLine("        }");
        source.AppendLine("    }");
        source.AppendLine("}");
        return source.ToString();
    }

    private static void AppendAssignments(StringBuilder source, ImmutableArray<SerializableMember> members)
    {
        foreach (SerializableMember member in members.Where(static member => member.deserialize))
        {
            source.Append("            value.").Append(member.name).Append(" = reader.Read<")
                .Append(member.typeName).Append(">(\"").Append(Escape(member.name)).AppendLine("\");");
        }
    }

    private static bool ImplementsSerializable(INamedTypeSymbol type)
        => type.AllInterfaces.Any(static contract => contract.ToDisplayString() == C_SERIALIZABLE_INTERFACE);

    private static bool HasAccessibleParameterlessConstructor(INamedTypeSymbol type)
        => type.InstanceConstructors.Any(static constructor =>
            constructor.Parameters.Length == 0 && IsAccessible(constructor));

    private static bool IsTypeAccessible(INamedTypeSymbol type)
    {
        for (INamedTypeSymbol? current = type; current is not null; current = current.ContainingType)
        {
            if (current.DeclaredAccessibility is Accessibility.Private
                or Accessibility.Protected
                or Accessibility.ProtectedAndInternal)
            {
                return false;
            }
        }
        return true;
    }

    private static bool IsAccessible(ISymbol? symbol)
        => symbol?.DeclaredAccessibility is Accessibility.Public
            or Accessibility.Internal
            or Accessibility.ProtectedOrInternal;

    private static int GetSourceOrder(ISymbol symbol)
        => symbol.Locations.FirstOrDefault(static location => location.IsInSource)?.SourceSpan.Start ?? int.MaxValue;

    private static string GetNamespace(INamedTypeSymbol type)
        => type.ContainingNamespace.IsGlobalNamespace
            ? "InnoGeneratedSerialization"
            : type.ContainingNamespace.ToDisplayString();

    private static string GetGeneratedName(INamedTypeSymbol type)
    {
        string name = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var result = new StringBuilder(name.Length);
        foreach (char character in name)
            result.Append(char.IsLetterOrDigit(character) ? character : '_');
        return result.ToString();
    }

    private static string Escape(string value)
        => value.Replace("\\", "\\\\").Replace("\"", "\\\"");

    private sealed class SerializableMember
    {
        internal SerializableMember(
            string name,
            string displayName,
            string typeName,
            bool serialize,
            bool deserialize,
            bool canRead,
            bool canWrite,
            Location location)
        {
            this.name = name;
            this.displayName = displayName;
            this.typeName = typeName;
            this.serialize = serialize;
            this.deserialize = deserialize;
            this.canRead = canRead;
            this.canWrite = canWrite;
            this.location = location;
        }

        internal string name { get; }
        internal string displayName { get; }
        internal string typeName { get; }
        internal bool serialize { get; }
        internal bool deserialize { get; }
        internal bool canRead { get; }
        internal bool canWrite { get; }
        internal Location location { get; }
    }
}
