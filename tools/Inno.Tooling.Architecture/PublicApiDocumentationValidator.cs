using System;
using System.Collections.Generic;
using System.Linq;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Inno.Tooling.Architecture;

internal static class PublicApiDocumentationValidator
{
    internal static void Validate(string relativePath, string source, ICollection<string> failures)
    {
        SyntaxNode root = CSharpSyntaxTree.ParseText(source, path: relativePath).GetRoot();
        foreach (MemberDeclarationSyntax member in root.DescendantNodes().OfType<MemberDeclarationSyntax>())
        {
            if (!RequiresDocumentation(member))
                continue;

            DocumentationCommentTriviaSyntax? documentation = member.GetLeadingTrivia()
                .Select(static trivia => trivia.GetStructure())
                .OfType<DocumentationCommentTriviaSyntax>()
                .LastOrDefault();
            FileLinePositionSpan span = member.GetLocation().GetLineSpan();
            string location = $"{relativePath}:{span.StartLinePosition.Line + 1}";
            if (documentation is null)
            {
                failures.Add($"{location}: public or protected declaration is missing explicit XML documentation.");
                continue;
            }
            if (documentation.ContainsDiagnostics)
                failures.Add($"{location}: public or protected XML documentation is malformed.");
            if (documentation.Content.OfType<XmlEmptyElementSyntax>().Any(static element =>
                    element.Name.LocalName.Text == "inheritdoc"))
            {
                failures.Add($"{location}: inheritdoc cannot replace an explicit public API contract.");
            }

            XmlElementSyntax? summary = FindElement(documentation, "summary");
            if (summary is null || string.IsNullOrWhiteSpace(GetText(summary)))
                failures.Add($"{location}: public or protected declaration requires a meaningful summary.");

            ValidateTypeParameters(location, member, documentation, failures);
            ValidateParameters(location, member, documentation, failures);
            if (RequiresReturns(member) && FindElement(documentation, "returns") is null)
                failures.Add($"{location}: non-void public or protected operation requires a returns contract.");
        }
    }

    private static bool RequiresDocumentation(MemberDeclarationSyntax member)
    {
        if (member is EnumMemberDeclarationSyntax enumMember)
            return enumMember.Parent is EnumDeclarationSyntax declaration && RequiresDocumentation(declaration);
        if (member.Parent is InterfaceDeclarationSyntax)
            return true;
        return member.Modifiers.Any(static modifier => modifier.IsKind(SyntaxKind.PublicKeyword)
            || modifier.IsKind(SyntaxKind.ProtectedKeyword));
    }

    private static void ValidateTypeParameters(
        string location,
        MemberDeclarationSyntax member,
        DocumentationCommentTriviaSyntax documentation,
        ICollection<string> failures)
    {
        TypeParameterListSyntax? parameters = member switch
        {
            TypeDeclarationSyntax type => type.TypeParameterList,
            MethodDeclarationSyntax method => method.TypeParameterList,
            DelegateDeclarationSyntax callback => callback.TypeParameterList,
            _ => null
        };
        if (parameters is null)
            return;
        HashSet<string> documented = GetNamedElements(documentation, "typeparam");
        foreach (TypeParameterSyntax parameter in parameters.Parameters)
        {
            if (!documented.Contains(parameter.Identifier.ValueText))
                failures.Add($"{location}: type parameter '{parameter.Identifier.ValueText}' is missing XML documentation.");
        }
    }

    private static void ValidateParameters(
        string location,
        MemberDeclarationSyntax member,
        DocumentationCommentTriviaSyntax documentation,
        ICollection<string> failures)
    {
        IEnumerable<ParameterSyntax> parameters = member switch
        {
            ClassDeclarationSyntax type => type.ParameterList?.Parameters ?? default,
            StructDeclarationSyntax type => type.ParameterList?.Parameters ?? default,
            RecordDeclarationSyntax type => type.ParameterList?.Parameters ?? default,
            MethodDeclarationSyntax method => method.ParameterList.Parameters,
            ConstructorDeclarationSyntax constructor => constructor.ParameterList.Parameters,
            DelegateDeclarationSyntax callback => callback.ParameterList.Parameters,
            OperatorDeclarationSyntax operation => operation.ParameterList.Parameters,
            ConversionOperatorDeclarationSyntax conversion => conversion.ParameterList.Parameters,
            IndexerDeclarationSyntax indexer => indexer.ParameterList.Parameters,
            _ => []
        };
        HashSet<string> documented = GetNamedElements(documentation, "param");
        foreach (ParameterSyntax parameter in parameters)
        {
            if (!documented.Contains(parameter.Identifier.ValueText))
                failures.Add($"{location}: parameter '{parameter.Identifier.ValueText}' is missing XML documentation.");
        }
    }

    private static bool RequiresReturns(MemberDeclarationSyntax member)
        => member switch
        {
            MethodDeclarationSyntax method => !IsVoid(method.ReturnType),
            DelegateDeclarationSyntax callback => !IsVoid(callback.ReturnType),
            OperatorDeclarationSyntax => true,
            ConversionOperatorDeclarationSyntax => true,
            _ => false
        };

    private static bool IsVoid(TypeSyntax type)
        => type is PredefinedTypeSyntax predefined && predefined.Keyword.IsKind(SyntaxKind.VoidKeyword);

    private static XmlElementSyntax? FindElement(DocumentationCommentTriviaSyntax documentation, string name)
        => documentation.Content.OfType<XmlElementSyntax>().FirstOrDefault(element =>
            element.StartTag.Name.LocalName.Text == name);

    private static HashSet<string> GetNamedElements(DocumentationCommentTriviaSyntax documentation, string name)
        => documentation.Content
            .OfType<XmlElementSyntax>()
            .Where(element => element.StartTag.Name.LocalName.Text == name)
            .SelectMany(static element => element.StartTag.Attributes.OfType<XmlNameAttributeSyntax>())
            .Where(static attribute => attribute.Name.LocalName.Text == "name")
            .Select(static attribute => attribute.Identifier.Identifier.ValueText)
            .ToHashSet(StringComparer.Ordinal);

    private static string GetText(XmlElementSyntax element)
        => string.Concat(element.Content.Select(static content => content.ToString())).Trim();
}
