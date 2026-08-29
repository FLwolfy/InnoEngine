using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Inno.Assets.Loader;
using Inno.Core.Serialization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Inno.Editor.Scripting;

[AssetImporterExtension]
internal sealed class CSharpScriptImporter : AssetImporter<ScriptSourceAsset>
{
    public override string importerId => "inno.editor.csharp-script";
    public override IReadOnlyList<string> supportedExtensions { get; } = [".cs"];

    protected override async ValueTask ImportAsync(
        AssetImportContext context,
        AssetImportWriter<ScriptSourceAsset> output,
        CancellationToken cancellationToken)
    {
        string source = context.ReadUtf8Text();
        SyntaxTree tree = CSharpSyntaxTree.ParseText(
            source,
            new CSharpParseOptions(LanguageVersion.Latest, DocumentationMode.Parse),
            context.assetPath.ToString(),
            Encoding.UTF8,
            cancellationToken);
        BaseTypeDeclarationSyntax[] declarations = tree.GetRoot(cancellationToken)
            .DescendantNodes()
            .OfType<BaseTypeDeclarationSyntax>()
            .ToArray();
        string[] declaredTypes = declarations
            .Select(GetDeclaredTypeName)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static value => value, StringComparer.Ordinal)
            .ToArray();
        string[] diagnostics = tree.GetDiagnostics(cancellationToken)
            .Where(static diagnostic => diagnostic.Severity != DiagnosticSeverity.Hidden)
            .Select(static diagnostic => diagnostic.ToString())
            .ToArray();
        ScriptAssemblyScope scope = context.assetPath.localPath.EndsWith(
            ".editor.cs",
            StringComparison.OrdinalIgnoreCase)
            ? ScriptAssemblyScope.Editor
            : ScriptAssemblyScope.Runtime;
        output.SetAsset(new ScriptSourceAsset(scope, declaredTypes, diagnostics));
        await output.WriteArtifactAsync(
            "source",
            Encoding.UTF8.GetBytes(source),
            cancellationToken).ConfigureAwait(false);
        await output.WriteArtifactAsync(
            "diagnostics",
            Encoding.UTF8.GetBytes(string.Join(Environment.NewLine, diagnostics)),
            cancellationToken).ConfigureAwait(false);
        var typeManifest = new ScriptSourceTypeManifest(
            context.persistentId,
            context.assetPath.ToString(),
            declarations.Select(CreateTypeDeclaration).ToArray());
        await output.WriteArtifactAsync(
            "type-manifest",
            SerializationManager.Serialize(typeManifest),
            cancellationToken).ConfigureAwait(false);
    }

    private static ScriptSourceTypeDeclaration CreateTypeDeclaration(
        BaseTypeDeclarationSyntax declaration)
    {
        FileLinePositionSpan span = declaration.Identifier.GetLocation().GetLineSpan();
        return new ScriptSourceTypeDeclaration(
            GetDeclaredTypeName(declaration),
            declaration.Kind().ToString(),
            declaration.Modifiers.Any(static modifier => modifier.IsKind(SyntaxKind.PartialKeyword)),
            span.StartLinePosition.Line + 1,
            span.StartLinePosition.Character + 1);
    }

    private static string GetDeclaredTypeName(BaseTypeDeclarationSyntax declaration)
    {
        string[] containingTypes = declaration.Ancestors()
            .OfType<BaseTypeDeclarationSyntax>()
            .Reverse()
            .Select(static value => value.Identifier.ValueText)
            .Append(declaration.Identifier.ValueText)
            .ToArray();
        string nestedTypeName = string.Join("+", containingTypes);
        string[] namespaces = declaration.Ancestors()
            .OfType<BaseNamespaceDeclarationSyntax>()
            .Reverse()
            .Select(static value => value.Name.ToString())
            .ToArray();
        return namespaces.Length == 0
            ? nestedTypeName
            : string.Join(".", namespaces) + "." + nestedTypeName;
    }
}
